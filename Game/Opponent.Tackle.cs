using System.Numerics;

namespace RaylibTackleAlley.Game;

public enum DefenderState { Locomotion, TackleReady, SetWrap, LungeTackle, LungeLand, Down, GetUp, Taunt }

public sealed partial class Opponent
{
    private bool _tauntRequested;
    private float _tauntElapsed;
    public bool TauntComplete => State == DefenderState.Taunt && _tauntElapsed >= 2.6f;
    public void CelebrateTackle() => _tauntRequested = true;

    // Speed 1 uses the same configured pace as the carrier's lowest speed tier.
    public float TackleReadySpeed => Movement.ReadySpeed;
    // Authored Set/Wrap clips run from frame 1 through 25 at 60 Hz.
    private const float AuthoredSetWrapDuration = 24f / 60f;
    private const float AuthoredLungeDuration = 36f / 60f;
    private bool IsTackleCommitted => State is DefenderState.SetWrap or DefenderState.LungeTackle
        or DefenderState.LungeLand or DefenderState.Down or DefenderState.GetUp;
    // Only used by headless gameplay, where no AnimationPlayer has been loaded.
    private const float AuthoredLungeLandDuration = 36f / 60f;
    private const float AuthoredGetUpDuration = 100f / 60f;
    public float VerticalVelocity { get; private set; }
    public bool IsGrounded => _position.Y <= _spawnPosition.Y && VerticalVelocity == 0f;
    private string? _tackleAnimation;
    private float _tackleRemaining;
    public DefenderState State { get; private set; }
    public float FacingYawDegrees => _yawDegrees;

    public static readonly string[] TackleAnimationNames =
    [
        "TackleReady", "TackleReadyForward", "TackleReadyBackward",
        "TackleReadyLeft", "TackleReadyRight",
        "SetWrapForward", "SetWrapLeft", "SetWrapRight",
        "LungeTackleForward", "LungeTackleLeft", "LungeTackleRight",
        "LungeLand", "Down", "GetUp", "TauntBicepFlex"
    ];

    public static readonly string[] AnimationAssetKeys =
    [
        "FootballPlayerAnimations", "FootballPlayerRunAnimations", "FootballPlayerSprintAnimations",
        .. TackleAnimationNames.Select(name => $"FootballPlayer{name}Animations")
    ];


    private float ReadyYaw(Vector3 target, float dt)
    {
        Vector3 offset = target - _position;
        if (offset.X * offset.X + offset.Z * offset.Z < 0.0001f) return _yawDegrees;
        float desired = MathF.Atan2(-offset.X, -offset.Z) * 180f / MathF.PI;
        float difference = MathF.IEEERemainder(desired - _yawDegrees, 360f);
        return _yawDegrees + Math.Clamp(difference,
            -Movement.ReadyFacingResponse * dt, Movement.ReadyFacingResponse * dt);
    }

    // Generic intent: movement is defender-local (X right, Y forward).
    // Visual facing stays with the committed clip; early travel correction is separately bounded.
    public void Update(Vector3 playerPosition, float deltaTime, bool readyHeld,
        Vector2 movement, bool tacklePressed, Vector3? pursuitTarget = null, Vector3? tackleFacingTarget = null)
    {
        if (UpdatePhysicsAndRecovery(deltaTime)) return;
        float dt = Math.Max(0f, deltaTime);
        if (IsTackleCommitted)
        {
            bool wasWrap = State == DefenderState.SetWrap;
            float wrapTime = wasWrap ? Math.Min(dt, OneShotTimeRemaining) : 0f;
            UpdateCommitted(dt, readyHeld);
            if (wasWrap && !IsTackleCommitted)
                Update(playerPosition, Math.Max(0f, dt - wrapTime), readyHeld, movement, false, pursuitTarget);
            return;
        }

        if (!readyHeld)
        {
            State = DefenderState.Locomotion;
            _tackleAnimation = null;
            SelectAnimation();
            UpdatePursuit(playerPosition, dt, pursuitTarget);
            return;
        }

        // Preserve incoming speed; the normal ramp breaks down toward Speed 1.
        HasEngaged = true;
        Pace = OpponentPace.Sprint;
        State = DefenderState.TackleReady;
        if (movement.LengthSquared() > 1f) movement = Vector2.Normalize(movement);
        if (movement.LengthSquared() < _config.OpponentReadyInputDeadzone * _config.OpponentReadyInputDeadzone) movement = Vector2.Zero;
        if (tacklePressed)
        {
            CommitTackle(playerPosition, DefenderState.SetWrap);
            Update(playerPosition, dt, readyHeld, movement, false, pursuitTarget);
            return;
        }

        _yawDegrees = ReadyYaw(tackleFacingTarget ?? playerPosition, dt);
        float amount = movement.Length();
        _readyTargetSpeed = TackleReadySpeed * amount;
        var step = SpeedRamp.Advance(CurrentSpeed, _readyTargetSpeed,
            Movement.AccelerationRate, _config.ForwardDeceleration, dt);
        CurrentSpeed = Math.Max(0f, step.Speed);
        if (amount > 0f)
        {
            Vector3 local = new(movement.X / amount, 0f, -movement.Y / amount);
            _movementDirection = Movement.TrackDirection(_movementDirection, Vector3.Transform(local,
                Matrix4x4.CreateRotationY(_yawDegrees * MathF.PI / 180f)), dt);
        }
        // Continue residual travel during deceleration even when intent becomes neutral.
        _position += _movementDirection * step.Distance;
        Vector3 localVelocity = Vector3.Transform(_movementDirection * CurrentSpeed,
            Matrix4x4.CreateRotationY(-_yawDegrees * MathF.PI / 180f));
        _tackleAnimation = SelectReadyAnimation(new Vector2(localVelocity.X, -localVelocity.Z));
        SelectAnimation();
        _animation?.Update(dt * (AnimationName == "TackleReady" ? 1f : LocomotionPlaybackRate));
    }

    // Imported lunge clips contain a trailing return-to-ready sample after the
    // authored 0.6-second dive. Hand off before playback interpolates toward it.
    private float OneShotDuration => _animation is { } clip
        ? State == DefenderState.LungeTackle
            ? Math.Min(AuthoredLungeDuration, (clip.FrameCount - 1) / clip.FramesPerSecond)
            : (clip.FrameCount - 1) / clip.FramesPerSecond
        : State == DefenderState.LungeTackle ? AuthoredLungeDuration : AuthoredSetWrapDuration;

    private float OneShotTimeRemaining => _animation is { } clip
        ? Math.Max(0f, OneShotDuration - clip.CurrentTime)
        : _tackleRemaining;

    private float TimeUntilGround()
    {
        if (IsGrounded) return 0f;
        float height = Math.Max(0f, _position.Y - _spawnPosition.Y);
        return (VerticalVelocity + MathF.Sqrt(VerticalVelocity * VerticalVelocity +
            2f * _config.OpponentFallGravity * height)) / _config.OpponentFallGravity;
    }

    private void AdvanceFall(float dt)
    {
        // A low lunge reaches ground level before its animation finishes. Keep
        // driving forward until the pose hands off to ragdoll physics.
        bool lunging = State == DefenderState.LungeTackle;
        if (lunging) AdvanceAimedLunge(dt);
        if (IsGrounded) return;
        float airborneTime = Math.Min(dt, TimeUntilGround());
        if (!lunging) _position += _movementDirection * CurrentSpeed * airborneTime;
        _position.Y += VerticalVelocity * airborneTime -
            0.5f * _config.OpponentFallGravity * airborneTime * airborneTime;
        VerticalVelocity -= _config.OpponentFallGravity * airborneTime;
        if (airborneTime < dt || _position.Y <= _spawnPosition.Y + 0.000001f && VerticalVelocity <= 0f)
        {
            _position.Y = _spawnPosition.Y;
            VerticalVelocity = 0f;
            if (!lunging)
            {
                CurrentSpeed = 0f;
                _movementDirection = Vector3.Zero;
            }
        }
    }

    private void EnterRecovery(DefenderState state)
    {
        State = state;
        _tackleAnimation = state.ToString();
        SelectAnimation();
        _animation?.SeekTime(0f);
        _tackleRemaining = state switch
        {
            DefenderState.Down => _config.OpponentDownDuration,
            DefenderState.LungeLand => AuthoredLungeLandDuration,
            _ => AuthoredGetUpDuration
        };
    }

    private void UpdateCommitted(float dt, bool readyHeld)
    {
        // Consume each boundary separately, so a long frame still lands and recovers.
        while (IsTackleCommitted)
        {
            float remaining = State == DefenderState.Down ? _tackleRemaining : OneShotTimeRemaining;
            if (State == DefenderState.LungeLand)
                remaining = Math.Max(remaining, TimeUntilGround());
            float step = Math.Min(dt, remaining);
            if (State is DefenderState.LungeTackle or DefenderState.LungeLand)
                AdvanceFall(step);
            _animation?.Update(step);
            _tackleRemaining = Math.Max(0f, _tackleRemaining - step);
            dt = Math.Max(0f, dt - step);
            bool completed = State == DefenderState.Down
                ? _tackleRemaining <= 0.000001f : OneShotTimeRemaining <= 0.000001f;
            if (!completed || State == DefenderState.LungeLand && !IsGrounded) return;

            switch (State)
            {
                case DefenderState.LungeTackle:
                    // Apply the final authored pose above before handing off. Velocity still
                    // contains the locked launch direction, actual speed and vertical fall speed.
                    // No additional impulse is needed: the simulation inherits that momentum.
                    if (ActivateRagdoll()) Ragdoll.Update(dt);
                    // A headless defender has no pose to capture. Hold the completed commitment
                    // until visuals exist; never invent a default pose or resume pursuit.
                    return;
                case DefenderState.LungeLand:
                    EnterRecovery(DefenderState.Down);
                    break;
                case DefenderState.Down:
                    EnterRecovery(DefenderState.GetUp);
                    break;
                default:
                    bool ready = State == DefenderState.SetWrap && readyHeld;
                    State = ready ? DefenderState.TackleReady : DefenderState.Locomotion;
                    _tackleAnimation = ready ? "TackleReady" : null;
                    _tackleRemaining = 0f;
                    SelectAnimation();
                    _animation?.SeekTime(0f);
                    return;
            }
            if (dt <= 0f) return;
        }
    }

    // Finish committed physics and recovery before the successful tackler celebrates.
    // Other defenders only finish their existing lunge; pursuit stays stopped.
    public void UpdateLungeAfterOutcome(float deltaTime)
    {
        float dt = Math.Max(0f, deltaTime);
        if (UpdatePhysicsAndRecovery(dt)) return;
        if (State == DefenderState.LungeTackle) UpdateCommitted(dt, false);
        else if (_tauntRequested)
        {
            State = DefenderState.Taunt;
            _tackleAnimation = "TauntBicepFlex";
            CurrentSpeed = VerticalVelocity = 0f;
            _movementDirection = Vector3.Zero;
            SelectAnimation();
            _animation?.Update(dt);
            _tauntElapsed += dt;
        }
    }

    private string SelectReadyAnimation(Vector2 movement)
    {
        if (movement.LengthSquared() < _config.OpponentReadyAnimationSpeedThreshold * _config.OpponentReadyAnimationSpeedThreshold) return "TackleReady";
        // Forward/back wins exact diagonal ties.
        if (Math.Abs(movement.Y) >= Math.Abs(movement.X))
            return movement.Y > 0f ? "TackleReadyForward" : "TackleReadyBackward";
        return movement.X < 0f ? "TackleReadyLeft" : "TackleReadyRight";
    }

    private void CommitTackle(Vector3 target, DefenderState action)
    {
        float angle = TargetAngle(target);
        string direction = Math.Abs(angle) <= _config.OpponentTackleForwardAngleDegrees ? "Forward" :
            angle < 0f ? "Left" : "Right";
        _tackleAnimation = action + direction;
        HasEngaged = true;
        Pace = OpponentPace.Sprint;
        State = action;
        if (action == DefenderState.LungeTackle)
        {
            VerticalVelocity = _config.OpponentLungeLaunchVerticalSpeed;
            // Carry approach momentum into the dive; save the original direction for bounded correction.
            CurrentSpeed = Math.Max(CurrentSpeed, Movement.JogSpeed);
            Vector3 launch = target - _position;
            launch.Y = 0f;
            _movementDirection = launch.LengthSquared() > 0.0001f
                ? Vector3.Normalize(launch)
                : Vector3.Transform(-Vector3.UnitZ, Matrix4x4.CreateRotationY(_yawDegrees * MathF.PI / 180f));
        }
        else
        {
            CurrentSpeed = 0f;
            _movementDirection = Vector3.Zero;
        }
        _selectedTackleTarget = target;
        _decisionReason = action == DefenderState.SetWrap ? "Wrap committed" : "Lunge committed";
        InitialContactSolution = CurrentContactSolution;
        InitialLaunchDirection = _movementDirection;
        LaunchSpeed = CurrentSpeed;
        _lungeElapsed = 0;
        SelectAnimation();
        _animation?.SeekTime(0f);
        _tackleRemaining = OneShotDuration;
        _committedDuration = OneShotDuration;
    }

    private float TargetAngle(Vector3 target)
    {
        Vector3 relative = Vector3.Transform(target - _position,
            Matrix4x4.CreateRotationY(-_yawDegrees * MathF.PI / 180f));
        return relative.X * relative.X + relative.Z * relative.Z < 0.0001f ? 0f :
            MathF.Atan2(relative.X, -relative.Z) * 180f / MathF.PI;
    }
}
