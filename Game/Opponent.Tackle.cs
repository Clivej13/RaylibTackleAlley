using System.Numerics;

namespace RaylibTackleAlley.Game;

public enum DefenderState { Locomotion, TackleReady, SetWrap, LungeTackle, LungeLand, Down, GetUp }

public sealed partial class Opponent
{
    // Speed 1 uses the same configured pace as the carrier's lowest speed tier.
    public float TackleReadySpeed => _config.PlayerSlowSpeed;
    // Authored Set/Wrap clips run from frame 1 through 25 at 60 Hz.
    private const float AuthoredSetWrapDuration = 24f / 60f;
    private const float AuthoredLungeDuration = 36f / 60f;
    private bool IsTackleCommitted => State is DefenderState.SetWrap or DefenderState.LungeTackle
        or DefenderState.LungeLand or DefenderState.Down or DefenderState.GetUp;
    public const float DefenderDownDurationSeconds = 2f;
    private const float LungeLaunchVerticalSpeed = 4f;
    private const float FallGravity = 10f;
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
        "LungeLand", "Down", "GetUp"
    ];

    public static readonly string[] AnimationAssetKeys =
    [
        "FootballPlayerAnimations", "FootballPlayerRunAnimations", "FootballPlayerSprintAnimations",
        .. TackleAnimationNames.Select(name => $"FootballPlayer{name}Animations")
    ];

    public const float ReadyEnterDistance = 4f;
    public const float ReadyExitDistance = 5f;
    public const float WrapCommitDistance = 1f;
    public const float WrapContainmentAngleDegrees = 45f;
    public const float WrapSpeedTolerance = 0.25f;
    public const float LungeReachDistance = 2.25f;
    public const float LungeReachAngleDegrees = 45f;
    public const float TackleForwardAngleDegrees = 15f;
    public const float BreakdownReactionSeconds = 0.15f;
    private const float ReadyTurnDegreesPerSecond = 360f;

    // AI supplies intent to the same state update a future defender controller can use.
    public void Update(Vector3 playerPosition, float deltaTime, Vector3? pursuitTarget = null)
    {
        if (UpdatePhysicsAndRecovery(deltaTime)) return;
        Vector3 offset = playerPosition - _position;
        offset.Y = 0f;
        float distance = offset.Length();
        float angle = Math.Abs(TargetAngle(playerPosition));
        // Reserve capture space plus reaction travel and the distance needed to
        // decelerate to the controlled ready pace. Prediction still drives movement.
        float brakingDistance = CurrentSpeed <= TackleReadySpeed ? 0f :
            _config.ForwardDeceleration > 0f
                ? (CurrentSpeed * CurrentSpeed - TackleReadySpeed * TackleReadySpeed) /
                    (2f * _config.ForwardDeceleration)
                : float.PositiveInfinity;
        bool canBreakDown = distance >= WrapCommitDistance +
            CurrentSpeed * BreakdownReactionSeconds + brakingDistance;
        bool ready = distance <= ReadyExitDistance && State != DefenderState.Locomotion ||
            distance <= ReadyEnterDistance && canBreakDown;
        bool contained = distance <= WrapCommitDistance &&
            angle <= WrapContainmentAngleDegrees &&
            CurrentSpeed <= TackleReadySpeed + WrapSpeedTolerance;
        bool reachable = distance <= LungeReachDistance && angle <= LungeReachAngleDegrees;
        if (!IsTackleCommitted)
        {
            if (State == DefenderState.TackleReady && contained)
                CommitTackle(playerPosition, DefenderState.SetWrap);
            else if (reachable && (State == DefenderState.TackleReady
                         ? distance > WrapCommitDistance : !canBreakDown))
                CommitTackle(playerPosition, DefenderState.LungeTackle);
        }
        float yaw = ReadyYaw(playerPosition, Math.Max(0f, deltaTime));
        Vector3 targetOffset = (pursuitTarget ?? playerPosition) - _position;
        targetOffset.Y = 0f;
        // At the interception point, keep closing instead of waiting outside wrap range.
        if (targetOffset.LengthSquared() < 0.01f) targetOffset = offset;
        float targetDistance = targetOffset.Length();
        Vector3 local = Vector3.Transform(targetOffset, Matrix4x4.CreateRotationY(-yaw * MathF.PI / 180f));
        Vector2 movement = targetDistance > 0.1f
            ? new Vector2(local.X, -local.Z) / targetDistance : Vector2.Zero;
        Update(playerPosition, deltaTime, ready, movement, false, pursuitTarget);
    }

    private float ReadyYaw(Vector3 target, float dt)
    {
        Vector3 offset = target - _position;
        if (offset.X * offset.X + offset.Z * offset.Z < 0.0001f) return _yawDegrees;
        float desired = MathF.Atan2(-offset.X, -offset.Z) * 180f / MathF.PI;
        float difference = MathF.IEEERemainder(desired - _yawDegrees, 360f);
        return _yawDegrees + Math.Clamp(difference,
            -ReadyTurnDegreesPerSecond * dt, ReadyTurnDegreesPerSecond * dt);
    }

    // Generic intent: movement is defender-local (X right, Y forward).
    // Only the committed one-shot freezes facing; ready tracks the carrier.
    public void Update(Vector3 playerPosition, float deltaTime, bool readyHeld,
        Vector2 movement, bool tacklePressed, Vector3? pursuitTarget = null)
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
        State = DefenderState.TackleReady;
        if (movement.LengthSquared() > 1f) movement = Vector2.Normalize(movement);
        if (movement.LengthSquared() < 0.01f) movement = Vector2.Zero;
        if (tacklePressed)
        {
            CommitTackle(playerPosition, DefenderState.SetWrap);
            Update(playerPosition, dt, readyHeld, movement, false, pursuitTarget);
            return;
        }

        _yawDegrees = ReadyYaw(playerPosition, dt);
        float amount = movement.Length();
        var step = SpeedRamp.Advance(CurrentSpeed, TackleReadySpeed * amount,
            _config.ForwardAcceleration, _config.ForwardDeceleration, dt);
        CurrentSpeed = Math.Max(0f, step.Speed);
        if (amount > 0f)
        {
            Vector3 local = new(movement.X / amount, 0f, -movement.Y / amount);
            _movementDirection = Vector3.Transform(local,
                Matrix4x4.CreateRotationY(_yawDegrees * MathF.PI / 180f));
        }
        // Continue residual travel during deceleration even when intent becomes neutral.
        _position += _movementDirection * step.Distance;
        Vector3 localVelocity = Vector3.Transform(_movementDirection * CurrentSpeed,
            Matrix4x4.CreateRotationY(-_yawDegrees * MathF.PI / 180f));
        _tackleAnimation = SelectReadyAnimation(new Vector2(localVelocity.X, -localVelocity.Z));
        SelectAnimation();
        _animation?.Update(dt);
    }

    private float OneShotTimeRemaining => _animation is { } clip
        ? Math.Max(0f, (clip.FrameCount - 1) / clip.FramesPerSecond - clip.CurrentTime)
        : _tackleRemaining;

    private float TimeUntilGround()
    {
        if (IsGrounded) return 0f;
        float height = Math.Max(0f, _position.Y - _spawnPosition.Y);
        return (VerticalVelocity + MathF.Sqrt(VerticalVelocity * VerticalVelocity +
            2f * FallGravity * height)) / FallGravity;
    }

    private void AdvanceFall(float dt)
    {
        if (IsGrounded) return;
        float airborneTime = Math.Min(dt, TimeUntilGround());
        _position += _movementDirection * CurrentSpeed * airborneTime;
        _position.Y += VerticalVelocity * airborneTime -
            0.5f * FallGravity * airborneTime * airborneTime;
        VerticalVelocity -= FallGravity * airborneTime;
        if (airborneTime < dt || _position.Y <= _spawnPosition.Y + 0.000001f && VerticalVelocity <= 0f)
        {
            _position.Y = _spawnPosition.Y;
            VerticalVelocity = 0f;
            CurrentSpeed = 0f;
            _movementDirection = Vector3.Zero;
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
            DefenderState.Down => DefenderDownDurationSeconds,
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

    // End-of-run continuation: finish only an already committed lunge or owned physics.
    // This cannot select an action, steer, advance SetWrap, or move the carrier.
    public void UpdateLungeAfterOutcome(float deltaTime)
    {
        float dt = Math.Max(0f, deltaTime);
        if (UpdatePhysicsAndRecovery(dt)) return;
        if (State == DefenderState.LungeTackle) UpdateCommitted(dt, false);
    }

    private static string SelectReadyAnimation(Vector2 movement)
    {
        if (movement.LengthSquared() < 0.01f) return "TackleReady";
        // Forward/back wins exact diagonal ties.
        if (Math.Abs(movement.Y) >= Math.Abs(movement.X))
            return movement.Y > 0f ? "TackleReadyForward" : "TackleReadyBackward";
        return movement.X < 0f ? "TackleReadyLeft" : "TackleReadyRight";
    }

    private void CommitTackle(Vector3 target, DefenderState action)
    {
        float angle = TargetAngle(target);
        string direction = Math.Abs(angle) <= TackleForwardAngleDegrees ? "Forward" :
            angle < 0f ? "Left" : "Right";
        _tackleAnimation = action + direction;
        State = action;
        if (action == DefenderState.LungeTackle)
        {
            VerticalVelocity = LungeLaunchVerticalSpeed;
            // Carry approach momentum into the dive and lock its launch direction.
            CurrentSpeed = Math.Max(CurrentSpeed, _config.OpponentJogSpeed);
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
        SelectAnimation();
        _animation?.SeekTime(0f);
        _tackleRemaining = _animation is { } clip
            ? (clip.FrameCount - 1) / clip.FramesPerSecond
            : action == DefenderState.SetWrap ? AuthoredSetWrapDuration : AuthoredLungeDuration;
    }

    private float TargetAngle(Vector3 target)
    {
        Vector3 relative = Vector3.Transform(target - _position,
            Matrix4x4.CreateRotationY(-_yawDegrees * MathF.PI / 180f));
        return relative.X * relative.X + relative.Z * relative.Z < 0.0001f ? 0f :
            MathF.Atan2(relative.X, -relative.Z) * 180f / MathF.PI;
    }
}
