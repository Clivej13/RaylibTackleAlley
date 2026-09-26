using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibTackleAlley.Game;

namespace RaylibTackleAlley.Application;

/// <summary>One reusable visual instance and render target; no input, physics or BallCarrier.</summary>
internal sealed class ReturnerPreview : IDisposable
{
    private readonly AssetManager _assets;
    private readonly string _uniform;
    private ModelInstance? _model;
    private BoundingBox _bounds;
    private BoundingBox _tauntBounds;
    private readonly Dictionary<string, BoundingBox> _tauntEnvelopes = new();
    private readonly Dictionary<string, Color> _accents = new();
    private Texture2D _numberedUniform;
    private RenderTexture2D _target;
    private string? _id;
    private PlayerVisualProfile? _visual;
    private RaylibGameFramework.ThreeD.AnimationPlayer? _animation;

    public ReturnerPreview(AssetManager assets, string uniform)
    {
        _assets = assets;
        _uniform = uniform;
    }

    public void Draw(ReturnerDefinition selected, Rectangle bounds)
    {
        if (bounds.Width < 32 || bounds.Height < 32) return;
        if (_model is null)
        {
            _model = _assets.CreateModelInstance("FootballPlayer");
            _bounds = Raylib.GetModelBoundingBox(_model.Model);
        }
        if (_id != selected.Id)
        {
            var clips = _assets.GetModelAnimations(selected.Taunt ?? ReturnerCatalog.DefaultTaunt);
            if (clips.Length != 1 || clips[0].KeyFrameCount < 2 ||
                !Raylib.IsModelAnimationValid(_model.Model, clips[0]))
                throw new InvalidDataException("A preview taunt must contain one nonempty FootballPlayer-compatible clip.");
            _animation = new RaylibGameFramework.ThreeD.AnimationPlayer(_model, clips[0], loop: true);
            _animation.SeekTime(0);
            var texture = PlayerUniform.ApplyNumberedUniform(_model, _assets, selected.Uniform ?? _uniform, selected.Profile.JerseyNumber);
            if (_numberedUniform.Id != 0) Raylib.UnloadTexture(_numberedUniform);
            _numberedUniform = texture;
            _visual = new(selected.Profile);
            if (!_tauntEnvelopes.TryGetValue(selected.Id, out _tauntBounds))
                _tauntEnvelopes.Add(selected.Id, _tauntBounds = MeasureTaunt());
            _animation.SeekTime(0);
            _id = selected.Id;
        }

        else
            AdvanceAnimation(Raylib.GetFrameTime());

        int width = Math.Max(1, (int)bounds.Width), height = Math.Max(1, (int)bounds.Height);
        if (_target.Id == 0 || _target.Texture.Width != width || _target.Texture.Height != height)
        {
            if (_target.Id != 0) Raylib.UnloadRenderTexture(_target);
            _target = Raylib.LoadRenderTexture(width, height);
        }

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(new Color(17, 29, 42, 255));
        // Fit the complete taunt envelope once per selection, avoiding camera pumping
        // as arms move. Orthographic framing keeps both head and boots legible.
        Vector3 direction = Vector3.Normalize(new Vector3(.38f, .10f, -1));
        Vector3 right = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, direction));
        Vector3 center = (_tauntBounds.Min + _tauntBounds.Max) * .5f;
        Vector3 extent = (_tauntBounds.Max - _tauntBounds.Min) * .5f;
        float projectedWidth = 2 * Vector3.Dot(Vector3.Abs(right), extent);
        float projectedHeight = 2 * Vector3.Dot(Vector3.Abs(up), extent);
        float fit = Math.Max(projectedHeight, projectedWidth * height / width) * 1.12f;
        var camera = new Camera3D(center + direction * 6, center, Vector3.UnitY,
            fit, CameraProjection.Orthographic);
        Raylib.BeginMode3D(camera);
        Color accent = Accent(selected);
        Raylib.DrawCylinder(new Vector3(center.X, -.025f, center.Z), .65f, .65f, .018f, 64,
            new Color((int)accent.R / 5, (int)accent.G / 5, (int)accent.B / 5, 255));
        Raylib.DrawCylinder(new Vector3(center.X, -.004f, center.Z), .36f, .36f, .002f, 48, new Color(9, 17, 25, 255));
        Raylib.DrawModelEx(_model.Model, new Vector3(0, _visual!.GroundOffset(_bounds), 0),
            Vector3.UnitY, 0, _visual.ModelScale(_bounds), Color.White);
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
        Raylib.DrawTexturePro(_target.Texture, new Rectangle(0, 0, width, -height),
            bounds, Vector2.Zero, 0, Color.White);
    }

    public Color Accent(ReturnerDefinition entry)
    {
        string key = entry.Uniform ?? _uniform;
        if (_accents.TryGetValue(key, out var accent)) return accent;
        Image atlas = Raylib.LoadImageFromTexture(_assets.GetTexture(key));
        try
        {
            // Chest panel above the number, using the same atlas convention as PlayerUniform.
            Color jersey = Raylib.GetImageColor(atlas, (int)(atlas.Width * .16f), (int)(atlas.Height * .09f));
            // Lift dark jerseys enough to remain readable against the dark card.
            accent = new Color((int)(jersey.R * .72f + 71), (int)(jersey.G * .72f + 71),
                (int)(jersey.B * .72f + 71), 255);
            _accents.Add(key, accent);
            return accent;
        }
        finally { Raylib.UnloadImage(atlas); }
    }

    private unsafe BoundingBox MeasureTaunt()
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        Vector3 scale = _visual!.ModelScale(_bounds);
        Vector3 offset = new(0, _visual.GroundOffset(_bounds), 0);
        // Include every authored animation frame, including jumps and raised hands.
        for (int frame = 0; frame < _animation!.FrameCount; frame++)
        {
            _animation.SeekTime(frame / _animation.FramesPerSecond);
            Model model = _model!.Model;
            for (int m = 0; m < model.MeshCount; m++)
            {
                Mesh mesh = model.Meshes[m];
                float* vertices = mesh.AnimVertices != null ? mesh.AnimVertices : mesh.Vertices;
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    Vector3 point = new Vector3(vertices[v * 3], vertices[v * 3 + 1], vertices[v * 3 + 2]) * scale + offset;
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
            }
        }
        return new BoundingBox(min, max);
    }

    // Advance only while drawn/highlighted; switching returner creates a fresh clock.
    private void AdvanceAnimation(float deltaTime)
    {
        if (float.IsFinite(deltaTime) && deltaTime > 0)
            _animation?.Update(deltaTime);
    }

    public void Hide()
    {
        _id = null;
        _animation = null;
    }

    public void Dispose()
    {
        if (_model is not null) _assets.ReleaseModelInstance(_model);
        _model = null;
        Hide();
        if (_numberedUniform.Id != 0) Raylib.UnloadTexture(_numberedUniform);
        if (_target.Id != 0) Raylib.UnloadRenderTexture(_target);
        _numberedUniform = default;
        _target = default;
    }
}
