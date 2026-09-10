using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;

namespace RaylibTackleAlley.Game;

public enum OpponentPace { Jog, Run, Sprint }

public sealed class Opponent
{
    private readonly Vector3 _spawnPosition;
    private readonly TackleAlleyConfig _config;
    private Vector3 _position;
    private const float VisualHeight = 2f;
    private ModelInstance? _model;
    private readonly Dictionary<string, AnimationPlayer> _animations = new();
    private AnimationPlayer? _animation;
    private float _visualScale;
    private float _groundOffset;
    private float _yawDegrees = 180f;

    public OpponentPace Pace { get; private set; } = OpponentPace.Jog;
    public Vector3 Position => _position;
    public float MovementSpeed => Pace switch
    {
        OpponentPace.Jog => _config.OpponentJogSpeed,
        OpponentPace.Run => _config.OpponentRunSpeed,
        _ => _config.OpponentSprintSpeed
    };

    public string AnimationName => Pace switch
    {
        OpponentPace.Jog => "Jog",
        OpponentPace.Run => "Run",
        OpponentPace.Sprint => "Sprint",
        _ => throw new InvalidOperationException("Unknown opponent pace.")
    };

    // AssetManager owns instances and borrowed clips; instances are released first.
    public unsafe void InitializeVisual(AssetManager assets)
    {
        if (_model is not null)
            return;

        var clips = new Dictionary<string, ModelAnimation>();
        foreach (string key in new[] { "FootballPlayerAnimations", "FootballPlayerRunAnimations", "FootballPlayerSprintAnimations" })
            foreach (ModelAnimation clip in assets.GetModelAnimations(key))
                clips[new string(clip.Name)] = clip;

        ModelInstance model = assets.CreateModelInstance("FootballPlayer");
        try
        {
            foreach (string name in new[] { "Jog", "Run", "Sprint" })
            {
                if (!clips.TryGetValue(name, out ModelAnimation clip) || clip.KeyFrameCount <= 0 ||
                    !Raylib.IsModelAnimationValid(model.Model, clip))
                    throw new InvalidDataException($"{name} must be nonempty and compatible with FootballPlayer.");
                // Each opponent owns its playback clocks and deformable model instance.
                // Raylib resamples glTF animation at 60 Hz independently of Blender FPS.
                _animations.Add(name, new AnimationPlayer(model, clip, loop: true));
            }
            BoundingBox bounds = Raylib.GetModelBoundingBox(model.Model);
            float height = bounds.Max.Y - bounds.Min.Y;
            if (!float.IsFinite(height) || height <= 0f)
                throw new InvalidDataException("FootballPlayer must have a finite positive height.");
            _visualScale = VisualHeight / height;
            _groundOffset = -bounds.Min.Y * _visualScale;
            _model = model;
            SelectAnimation();
        }
        catch
        {
            _animations.Clear();
            assets.ReleaseModelInstance(model);
            throw;
        }
    }

    public Opponent(Vector3 spawnPosition, TackleAlleyConfig config)
    {
        config.ValidateOpponentLocomotion();
        _spawnPosition = spawnPosition;
        _config = config;
        Reset();
    }

    public void Reset()
    {
        _position = _spawnPosition;
        Pace = OpponentPace.Jog;
        _yawDegrees = 180f;
        SelectAnimation();
        _animation?.SeekTime(0f);
    }

    private void SelectAnimation()
    {
        if (!_animations.TryGetValue(AnimationName, out AnimationPlayer? next) ||
            ReferenceEquals(next, _animation))
            return;
        _animation = next;
        _animation.SeekTime(0f);
    }

    private OpponentPace SelectPace(float distance)
    {
        float margin = _config.OpponentPaceHysteresis;
        float sprintExit = _config.OpponentSprintDistance +
            (Pace == OpponentPace.Sprint ? margin : 0f);
        if (distance <= sprintExit)
            return OpponentPace.Sprint;
        float runExit = _config.OpponentRunDistance +
            (Pace != OpponentPace.Jog ? margin : 0f);
        return distance <= runExit ? OpponentPace.Run : OpponentPace.Jog;
    }

    public void Update(Vector3 playerPosition, float deltaTime)
    {
        OpponentPace next = SelectPace(Vector3.Distance(_position, playerPosition));
        if (next != Pace)
        {
            Pace = next;
            SelectAnimation();
        }
        // Advance the active clip at authored speed; world movement remains game-driven.
        _animation?.Update(Math.Max(0f, deltaTime));
        Vector3 direction = playerPosition - _position;
        direction.Y = 0;
        if (direction.LengthSquared() > 0.001f)
        {
            _position += Vector3.Normalize(direction) * MovementSpeed * Math.Max(0, deltaTime);
            // Rotate authored -Z forward to actual movement; clips never move the world position.
            if (MovementSpeed > 0f && deltaTime > 0f)
                _yawDegrees = MathF.Atan2(-direction.X, -direction.Z) * (180f / MathF.PI);
        }
    }

    public bool IsTouching(Vector3 playerPosition) =>
        Vector3.Distance(_position, playerPosition) <= _config.TackleDistance;

    public void Draw()
    {
        if (_model is null)
            throw new InvalidOperationException("Initialize opponent visuals after loading assets.");
        Raylib.DrawModelEx(_model.Model, _position + new Vector3(0, _groundOffset, 0),
            Vector3.UnitY, _yawDegrees, new Vector3(_visualScale), Color.White);
    }
}
