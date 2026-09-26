using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;

namespace RaylibTackleAlley.Game;

public enum OpponentPace { Jog, Run, Sprint }

public sealed partial class Opponent : IDisposable
{
    private readonly Vector3 _spawnPosition;
    private readonly TackleAlleyConfig _config;
    private Vector3 _position;
    private Vector3 _movementDirection;
    private ModelInstance? _model;
    private readonly Dictionary<string, AnimationPlayer> _animations = new();
    private AnimationPlayer? _animation;
    private float _visualScale;
    private Vector3 _modelScale;
    public PlayerProfile Profile { get; }
    public PlayerVisualProfile VisualProfile { get; }
    public PlayerMovementAttributes Movement { get; }
    public PlayerPhysicalAttributes Physical { get; }
    public TackleImpact? LastTackle { get; internal set; }
    public Vector3 Momentum => Ragdoll.IsActive ? Ragdoll.Momentum : Physical.Momentum(Velocity);
    private float _groundOffset;
    private float _yawDegrees;

    public bool HasEngaged { get; private set; }
    public OpponentPace Pace { get; private set; } = OpponentPace.Jog;
    public Vector3 Position => _position;
    public float CurrentSpeed { get; private set; }
    private float _readyTargetSpeed;
    public float TargetSpeed => Ragdoll.IsActive || IsRecovering || State == DefenderState.Taunt ? 0f :
        State == DefenderState.TackleReady ? _readyTargetSpeed : MovementSpeed;
    public float MovementSpeed => State is DefenderState.LungeTackle or DefenderState.LungeLand ? CurrentSpeed : IsTackleCommitted ? 0f :
        State == DefenderState.TackleReady ? TackleReadySpeed : Pace switch
    {
        OpponentPace.Jog => Movement.JogSpeed,
        OpponentPace.Run => Movement.RunningSpeed,
        _ => Movement.SprintSpeed
    };

    public string AnimationName => _tackleAnimation ?? (Pace switch
    {
        OpponentPace.Jog => "Jog",
        OpponentPace.Run => "Run",
        OpponentPace.Sprint => "Sprint",
        _ => throw new InvalidOperationException("Unknown opponent pace.")
    });

    /// <summary>Apply a loaded texture asset to this player's Uniform material.</summary>
    private Texture2D _numberedUniform;
    public void ApplyUniform(AssetManager assets, string textureKey)
    {
        var next = PlayerUniform.ApplyNumberedUniform(_model ??
            throw new InvalidOperationException("Initialize player visuals before selecting a uniform."),
            assets, textureKey, Profile.JerseyNumber);
        if (_numberedUniform.Id != 0) Raylib.UnloadTexture(_numberedUniform);
        _numberedUniform = next;
    }

    public void Dispose()
    {
        if (_numberedUniform.Id != 0) Raylib.UnloadTexture(_numberedUniform);
        _numberedUniform = default;
    }

    // AssetManager owns instances and borrowed clips; instances are released first.
    public unsafe void InitializeVisual(AssetManager assets)
    {
        if (_model is not null)
            return;

        var clips = new Dictionary<string, ModelAnimation>();
        foreach (string key in AnimationAssetKeys)
            foreach (ModelAnimation clip in assets.GetModelAnimations(key))
                clips[new string(clip.Name)] = clip;

        ModelInstance model = assets.CreateModelInstance("FootballPlayer");
        try
        {
            foreach (string name in TackleAnimationNames.Concat(new[] { "Jog", "Run", "Sprint" }))
            {
                if (!clips.TryGetValue(name, out ModelAnimation clip) || clip.KeyFrameCount <= 0 ||
                    !Raylib.IsModelAnimationValid(model.Model, clip))
                    throw new InvalidDataException($"{name} must be nonempty and compatible with FootballPlayer.");
                // Each opponent owns its playback clocks and deformable model instance.
                // Raylib resamples glTF animation at 60 Hz independently of Blender FPS.
                _animations.Add(name, new AnimationPlayer(model, clip, loop: !name.StartsWith("SetWrap") && !name.StartsWith("LungeTackle") && name != "LungeLand" && name != "GetUp" && name != "TauntBicepFlex"));
            }
            BoundingBox bounds = Raylib.GetModelBoundingBox(model.Model);
            float height = bounds.Max.Y - bounds.Min.Y;
            if (!float.IsFinite(height) || height <= 0f)
                throw new InvalidDataException("FootballPlayer must have a finite positive height.");
            _modelScale = VisualProfile.ModelScale(bounds);
            _visualScale = _modelScale.Y;
            _groundOffset = VisualProfile.GroundOffset(bounds);
            _model = model;
            SelectAnimation();
            // A completed headless lunge waits for a real pose rather than fabricating one.
            if (State == DefenderState.LungeTackle && _tackleRemaining <= 0f && _animation is { } lunge)
                lunge.SeekTime(OneShotDuration);
        }
        catch
        {
            _animations.Clear();
            assets.ReleaseModelInstance(model);
            throw;
        }
    }

    public Opponent(Vector3 spawnPosition, TackleAlleyConfig config, PlayerProfile? profile = null,
        DefenderProfile? behaviorProfile = null)
    {
        config.ValidateOpponentLocomotion();
        config.ValidateTackle();
        config.ValidateRagdollRecovery();
        _spawnPosition = spawnPosition;
        _config = config;
        BehaviorProfile = behaviorProfile ?? DefenderProfile.Balanced(config);
        BehaviorProfile.Validate();
        _aimingConfig = config.ForDefender(BehaviorProfile);
        Profile = (profile ?? new PlayerProfile()) with { };
        VisualProfile = new(Profile);
        Movement = new(Profile, config, defender: true);
        Physical = new(Profile, config);
        _defaultCarrierPhysical = new(config.BallCarrierProfile, config);
        Ragdoll = new(config, Physical);
        _contactPose = new(config, Physical);
        Reset();
    }

    public void Reset()
    {
        LastTackle = null;
        ContactCooldown = WrapRemaining = 0;
        _tauntRequested = false; _tauntElapsed = 0f;
        Ragdoll.Deactivate();
        _recovery = null;
        _position = _spawnPosition;
        _movementDirection = Vector3.Zero;
        ResetAiming();
        ResetDecisions();
        State = DefenderState.Locomotion;
        _tackleAnimation = null;
        _tackleRemaining = 0f;
        VerticalVelocity = 0f;
        foreach (var animation in _animations.Values) animation.SeekTime(0f);
        HasEngaged = false;
        Pace = OpponentPace.Jog;
        CurrentSpeed = TargetSpeed;
        _yawDegrees = _config.OpponentInitialYawDegrees;
        SelectAnimation();
        _animation?.SeekTime(0f);
        RestoreAnimatedOwnership();
        if (_model is not null && _animation is not null)
            Raylib.UpdateModelAnimation(_model.Model, _animation.Animation, _animation.CurrentFrame);
    }

    private void SelectAnimation()
    {
        if (!_animations.TryGetValue(AnimationName, out AnimationPlayer? next) ||
            ReferenceEquals(next, _animation))
            return;
        float phase = 0f;
        if (State == DefenderState.Locomotion && _animation is { } previous)
        {
            float duration = previous.FrameCount / previous.FramesPerSecond;
            if (float.IsFinite(duration) && duration > 0f &&
                float.IsFinite(previous.CurrentTime))
                phase = previous.CurrentTime / duration;
        }
        // Preserve the gait cycle across clips with different durations.
        _animation = next;
        _animation.SeekPhase(float.IsFinite(phase) ? phase : 0f);
    }

    private OpponentPace SelectPace(float distance)
    {
        if (HasEngaged || distance <= _config.OpponentSprintDistance * BehaviorProfile.PursuitAggression)
        {
            HasEngaged = true;
            return OpponentPace.Sprint;
        }
        float margin = _config.OpponentPaceHysteresis;
        float runExit = _config.OpponentRunDistance * BehaviorProfile.PursuitAggression +
            (Pace != OpponentPace.Jog ? margin : 0f);
        return distance <= runExit ? OpponentPace.Run : OpponentPace.Jog;
    }

    private void UpdatePursuit(Vector3 playerPosition, float deltaTime, Vector3? pursuitTarget = null)
    {
        OpponentPace next = SelectPace(Vector3.Distance(_position, playerPosition));
        if (next != Pace)
        {
            Pace = next;
            SelectAnimation();
        }
        // Locomotion playback follows actual pace; action clocks remain authored.
        var step = SpeedRamp.Advance(CurrentSpeed, TargetSpeed,
            Movement.AccelerationRate, _config.ForwardDeceleration, deltaTime);
        CurrentSpeed = step.Speed;
        float baseline = Movement.BaselineSpeed(Pace == OpponentPace.Jog ? 1 : Pace == OpponentPace.Sprint ? 3 : 2);
        _animation?.Update(baseline > 0 ? Math.Clamp(step.Distance / baseline,
            Math.Max(0, deltaTime) * .35f, Math.Max(0, deltaTime) * 1.5f) : Math.Max(0, deltaTime) * .35f);
        Vector3 direction = (pursuitTarget ?? playerPosition) - _position;
        direction.Y = 0;
        if (direction.LengthSquared() > _config.OpponentPursuitStopDistance * _config.OpponentPursuitStopDistance)
        {
            _movementDirection = Movement.TrackDirection(_movementDirection, Vector3.Normalize(direction), deltaTime);
            _position += _movementDirection * step.Distance;
            // Rotate authored -Z forward to actual movement; clips never move the world position.
            if (MovementSpeed > 0f && deltaTime > 0f)
                _yawDegrees = MathF.Atan2(-_movementDirection.X, -_movementDirection.Z) * (180f / MathF.PI);
        }
    }

    public float LocomotionPlaybackRate => PlayerMovementAttributes.PlaybackRate(CurrentSpeed,
        State == DefenderState.TackleReady ? Movement.BaselineReadySpeed :
        Movement.BaselineSpeed(Pace == OpponentPace.Jog ? 1 : Pace == OpponentPace.Sprint ? 3 : 2));

    public bool IsTouching(Vector3 playerPosition) =>
        Vector3.Distance(_position, playerPosition) <= _config.TackleDistance;

    public void Draw()
    {
        if (Ragdoll.IsActive) { DrawRagdoll(); return; }
        if (_recovery is not null) { _recovery.Draw(); return; }
        RestoreAnimatedOwnership();
        if (_model is null)
            throw new InvalidOperationException("Initialize opponent visuals after loading assets.");
        Raylib.DrawModelEx(_model.Model, _position + new Vector3(0, _groundOffset, 0),
            Vector3.UnitY, _yawDegrees, _modelScale, Color.White);
    }
}
