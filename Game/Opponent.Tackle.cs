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
    public float TackleReadySpeed => _config.PlayerSlowSpeed;
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
    private Vector3? _observedCarrierVelocity;
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


    // AI supplies intent to the same state update a future defender controller can use.
    public void Update(Vector3 playerPosition, float deltaTime, Vector3? pursuitTarget = null,
        Vector3? carrierVelocity = null, Vector3? carrierPredictedDirection = null)
    {
        if (UpdatePhysicsAndRecovery(deltaTime)) return;
        // Near the carrier, contain the body rather than chase a distant lead point.
        if (pursuitTarget is { } predicted)
        {
            Vector3 lead = predicted - playerPosition;
            lead.Y = 0;
            float separation = Vector3.Distance(_position, playerPosition);
            float cap = separation * Math.Clamp(separation / _config.LungeFullPredictionDistance, 0, 1) * .5f;
            if (lead.Length() > cap) lead = Vector3.Normalize(lead) * cap;
            pursuitTarget = playerPosition + lead;
        }
        Vector3 offset = playerPosition - _position;
        offset.Y = 0f;
        float distance = offset.Length();
        float angle = Math.Abs(TargetAngle(playerPosition));
        // A defender behind the runner must keep chasing even after turning to face them.
        // The carrier-to-prediction direction defines forward, independently of visual facing.
        // Callers without prediction direction retain the distance-based AI contract.
        bool inReadyCone = carrierPredictedDirection is not { } forward || IsInCarrierReadyCone(offset, forward);
        // Reserve capture space plus reaction travel and the distance needed to
        // decelerate to the controlled ready pace. Prediction still drives movement.
        float brakingDistance = CurrentSpeed <= TackleReadySpeed ? 0f :
            _config.ForwardDeceleration > 0f
                ? (CurrentSpeed * CurrentSpeed - TackleReadySpeed * TackleReadySpeed) /
                    (2f * _config.ForwardDeceleration)
                : float.PositiveInfinity;
        bool canBreakDown = distance >= _config.OpponentWrapCommitDistance +
            CurrentSpeed * _config.OpponentBreakdownReactionSeconds + brakingDistance;
        bool ready = inReadyCone && (distance <= _config.OpponentReadyExitDistance && State != DefenderState.Locomotion ||
            distance <= _config.OpponentReadyEnterDistance && canBreakDown);
        bool contained = distance <= _config.OpponentWrapCommitDistance &&
            angle <= _config.OpponentWrapContainmentAngleDegrees &&
            CurrentSpeed <= TackleReadySpeed + _config.OpponentWrapSpeedTolerance;
        Vector3 relativeVelocity = Velocity - (carrierVelocity ?? Vector3.Zero);
        relativeVelocity.Y = 0;
        float closingSpeed = distance > .0001f ? Math.Max(0, Vector3.Dot(relativeVelocity, offset / distance)) : 0;
        float launchDistance = Math.Clamp(_config.OpponentLungeContactDistance + closingSpeed * _config.OpponentLungeAnimationLeadSeconds,
            _config.OpponentLungeReachDistance, _config.OpponentMaximumLungeReachDistance);
        float predictionConfidence = 1f;
        bool stableMotion = true;
        if (carrierVelocity is { } observed)
        {
            observed.Y = 0;
            Vector3 previous = _observedCarrierVelocity ?? observed;
            float change = (observed - previous).Length();
            stableMotion = change <= _config.OpponentLungeVelocityChangeTolerance;
            predictionConfidence = 1f / (1f + change);
            _observedCarrierVelocity = Vector3.Lerp(previous, observed,
                1f - MathF.Exp(-_config.PursuitDirectionResponse * Math.Max(0, deltaTime)));
        }
        // Fresh changes in observed motion only justify a near-term interception.
        // Keep tracking until the new path stabilizes; never steer a committed dive.
        float interceptionWindow = Math.Min(AuthoredLungeDuration,
            _config.OpponentLungeAnimationLeadSeconds +
            (AuthoredLungeDuration - _config.OpponentLungeAnimationLeadSeconds) * predictionConfidence);
        bool reachable = distance <= launchDistance && angle <= _config.OpponentLungeReachAngleDegrees;
        bool imminentContact = carrierVelocity.HasValue && distance <= _config.OpponentLungeContactDistance + closingSpeed * _config.OpponentLungeAnimationLeadSeconds;
        // The cone only controls the slow ready stance. Reachable tackles must still
        // commit from pursuit, including beside/behind the carrier or without a prediction.
        if (!IsTackleCommitted)
        {
            if (State == DefenderState.TackleReady && contained)
                CommitTackle(playerPosition, DefenderState.SetWrap);
            else if (stableMotion && reachable && (State == DefenderState.TackleReady
                         ? distance > _config.OpponentWrapCommitDistance : !canBreakDown || imminentContact) &&
                     LungeInterception.TryTarget(_position, playerPosition, carrierVelocity ?? Vector3.Zero,
                         Math.Max(CurrentSpeed, _config.OpponentJogSpeed), interceptionWindow,
                         out var launchTarget, _config) &&
                     Math.Abs(TargetAngle(launchTarget)) <= _config.OpponentLungeReachAngleDegrees)
                CommitTackle(launchTarget, DefenderState.LungeTackle);
        }
        // Brake and contain a close, changing path instead of converting a sprint
        // into an immediate airborne commitment.
        if (inReadyCone && !stableMotion && distance <= _config.OpponentReadyExitDistance) ready = true;
        if (!inReadyCone) pursuitTarget = playerPosition;
        float yaw = ReadyYaw(playerPosition, Math.Max(0f, deltaTime));
        Vector3 targetOffset = (pursuitTarget ?? playerPosition) - _position;
        targetOffset.Y = 0f;
        // At the interception point, keep closing instead of waiting outside wrap range.
        if (targetOffset.LengthSquared() < _config.OpponentPredictionArrivalDistance * _config.OpponentPredictionArrivalDistance) targetOffset = offset;
        float targetDistance = targetOffset.Length();
        Vector3 local = Vector3.Transform(targetOffset, Matrix4x4.CreateRotationY(-yaw * MathF.PI / 180f));
        Vector2 movement = targetDistance > _config.OpponentPredictionArrivalDistance
            ? new Vector2(local.X, -local.Z) / targetDistance : Vector2.Zero;
        Update(playerPosition, deltaTime, ready, movement, false, pursuitTarget);
    }

    private bool IsInCarrierReadyCone(Vector3 toCarrier, Vector3 carrierPredictedDirection)
    {
        carrierPredictedDirection.Y = 0f;
        if (carrierPredictedDirection.LengthSquared() < .0001f || toCarrier.LengthSquared() < .0001f)
            return false;
        float cosine = Vector3.Dot(Vector3.Normalize(-toCarrier), Vector3.Normalize(carrierPredictedDirection));
        return cosine + .000001f >= MathF.Cos(_config.OpponentReadyHalfAngleDegrees * MathF.PI / 180f);
    }

    private float ReadyYaw(Vector3 target, float dt)
    {
        Vector3 offset = target - _position;
        if (offset.X * offset.X + offset.Z * offset.Z < 0.0001f) return _yawDegrees;
        float desired = MathF.Atan2(-offset.X, -offset.Z) * 180f / MathF.PI;
        float difference = MathF.IEEERemainder(desired - _yawDegrees, 360f);
        return _yawDegrees + Math.Clamp(difference,
            -_config.OpponentReadyTurnDegreesPerSecond * dt, _config.OpponentReadyTurnDegreesPerSecond * dt);
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
        if (lunging) _position += _movementDirection * CurrentSpeed * dt;
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
        _tackleRemaining = OneShotDuration;
    }

    private float TargetAngle(Vector3 target)
    {
        Vector3 relative = Vector3.Transform(target - _position,
            Matrix4x4.CreateRotationY(-_yawDegrees * MathF.PI / 180f));
        return relative.X * relative.X + relative.Z * relative.Z < 0.0001f ? 0f :
            MathF.Atan2(relative.X, -relative.Z) * 180f / MathF.PI;
    }
}
