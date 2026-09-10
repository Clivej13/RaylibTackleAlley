using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.Assets;

namespace RaylibTackleAlley.Game;

public sealed class FootballField
{
    public float Width { get; }
    public float Length { get; }
    public float EndZoneLength { get; }
    public float HalfWidth => Width / 2f;
    public float GoalLineZ => -(Length - EndZoneLength);
    public float FinishLineZ => -Length;

    private readonly float _surfaceWidth;
    private readonly float _surfaceLength;
    private readonly float _stadiumWidth;
    private readonly float _stadiumLength;
    private readonly bool _drawGameplayDebug;
    private readonly AssetManager _assets;
    private Vector3 _fieldScale;
    private Vector3 _stadiumScale;
    private Vector3 _fieldPosition;
    private Vector3 _stadiumPosition;
    private bool _meshesAligned;

    public FootballField(TackleAlleyConfig config, AssetManager assets)
    {
        _assets = assets;
        Width = config.FieldWidth;
        Length = config.FieldLength;
        EndZoneLength = config.EndZoneLength;
        _surfaceWidth = config.FieldAssetWidth;
        _surfaceLength = config.FieldAssetLength;
        _stadiumWidth = config.StadiumAssetWidth;
        _stadiumLength = config.StadiumAssetLength;
        _drawGameplayDebug = config.DrawGameplayDebug;
    }

    private void AlignMeshes()
    {
        // Assets are loaded after construction, once the graphics context exists.
        BoundingBox fieldBounds = Raylib.GetModelBoundingBox(_assets.GetModel("FootballField"));
        BoundingBox stadiumBounds = Raylib.GetModelBoundingBox(_assets.GetModel("Stadium"));
        // The run occupies the attacking section of a larger field. Anchor its
        // finish to the back of the end zone, leaving the rest behind the start.
        float centerZ = FinishLineZ + _surfaceLength * 0.5f;
        (_fieldScale, _fieldPosition) = FitBounds(fieldBounds, _surfaceWidth, _surfaceLength, centerZ);
        (_stadiumScale, _stadiumPosition) = FitBounds(stadiumBounds, _stadiumWidth, _stadiumLength, centerZ);

        // The turf is the gameplay ground; preserve the stadium's authored elevation.
        _fieldPosition.Y = -fieldBounds.Max.Y;
        _meshesAligned = true;
    }

    private static (Vector3 Scale, Vector3 Position) FitBounds(BoundingBox bounds, float width, float length, float centerZ)
    {
        Vector3 size = bounds.Max - bounds.Min;
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Z) || size.X <= 0f || size.Z <= 0f)
            throw new InvalidOperationException("Field and stadium models must have nonzero horizontal bounds.");

        Vector3 scale = new(width / size.X, 1f, length / size.Z);
        Vector3 center = (bounds.Min + bounds.Max) * 0.5f;
        Vector3 position = new(-center.X * scale.X, 0f, centerZ - center.Z * scale.Z);
        return (scale, position);
    }

    // Solid turf limits also keep the carrier away from the surrounding stands.
    public Vector3 ClampToOuterBoundary(Vector3 position, float radius)
    {
        float halfWidth = Math.Min(_surfaceWidth, _stadiumWidth) * 0.5f;
        float centerZ = FinishLineZ + _surfaceLength * 0.5f;
        float halfLength = Math.Min(_surfaceLength, _stadiumLength) * 0.5f;
        position.X = Math.Clamp(position.X, -halfWidth + radius, halfWidth - radius);
        position.Z = Math.Clamp(position.Z, centerZ - halfLength + radius, centerZ + halfLength - radius);
        return position;
    }

    public bool IsOutOfBounds(Vector3 position) => MathF.Abs(position.X) > HalfWidth;

    public void DrawOutOfBounds()
    {
        float stripWidth = _surfaceWidth * 0.5f - HalfWidth;
        if (stripWidth <= 0f)
            return;

        float centerZ = FinishLineZ + _surfaceLength * 0.5f;
        float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 2f);
        for (int side = -1; side <= 1; side += 2)
        {
            Raylib.DrawPlane(new Vector3(side * (HalfWidth + stripWidth * 0.5f), 0.025f, centerZ),
                new Vector2(stripWidth, _surfaceLength), new Color(235, 40, 45, 48));
            // Adjacent bands fade outward from the exact failure boundary.
            const int bands = 12;
            float glowWidth = Math.Min(1.8f, stripWidth);
            for (int i = 0; i < bands; i++)
            {
                float width = glowWidth / bands;
                int alpha = (int)((1f - i / (float)bands) * (65f + pulse * 25f));
                Raylib.DrawPlane(new Vector3(side * (HalfWidth + (i + 0.5f) * width), 0.035f, centerZ),
                    new Vector2(width, _surfaceLength), new Color(255, 55, 60, alpha));
            }
            Raylib.DrawLine3D(new Vector3(side * HalfWidth, 0.045f, FinishLineZ),
                new Vector3(side * HalfWidth, 0.045f, FinishLineZ + _surfaceLength),
                new Color(255, 90, 90, 190));
        }
    }

    public void Draw()
    {
        if (!_meshesAligned)
            AlignMeshes();

        Raylib.DrawModelEx(_assets.GetModel("FootballField"), _fieldPosition, Vector3.UnitY, 0f, _fieldScale, Color.White);
        Raylib.DrawModelEx(_assets.GetModel("Stadium"), _stadiumPosition, Vector3.UnitY, 0f, _stadiumScale, Color.White);
        // Optional debug lines supplement the translucent danger overlay.

        if (_drawGameplayDebug)
        {
            float half = HalfWidth;
            Raylib.DrawLine3D(new Vector3(-half, 0.05f, 0), new Vector3(-half, 0.05f, FinishLineZ), Color.Yellow);
            Raylib.DrawLine3D(new Vector3(half, 0.05f, 0), new Vector3(half, 0.05f, FinishLineZ), Color.Yellow);
            Raylib.DrawLine3D(new Vector3(-half, 0.06f, GoalLineZ), new Vector3(half, 0.06f, GoalLineZ), Color.Red);
        }
    }
}
