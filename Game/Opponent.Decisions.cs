using System.Numerics;

namespace RaylibTackleAlley.Game;

public readonly record struct DefenderDecisionDebug(
    DefenderAiState State, string Profile, string Reason, Vector3 PursuitTarget, Vector3 PredictedTarget,
    Vector3 ApproachDirection, float BrakingDistance, float RequiredStoppingDistance,
    bool WrapReachable, bool LungeReachable, Vector3? SelectedTackleTarget, Vector3 LockedDirection);

public sealed partial class Opponent
{
    public DefenderProfile BehaviorProfile { get; }
    private readonly TackleAlleyConfig _aimingConfig;
    private DefenderApproach _approach;
    private bool _wrapReachable, _lungeReachable;
    private Vector3? _selectedTackleTarget;
    private string _decisionReason = "Reset";

    public DefenderAiState AiState => Ragdoll.IsActive || IsRecovering ||
        State is DefenderState.Down or DefenderState.GetUp or DefenderState.LungeLand ? DefenderAiState.Recovery :
        State == DefenderState.SetWrap ? DefenderAiState.WrapCommitment :
        State == DefenderState.LungeTackle ? DefenderAiState.LungeCommitment :
        State == DefenderState.Taunt ? DefenderAiState.Taunt :
        State == DefenderState.TackleReady ? CurrentSpeed > TackleReadySpeed + _config.OpponentWrapSpeedTolerance
            ? DefenderAiState.Breakdown : DefenderAiState.Approach : _approach.State;

    public DefenderDecisionDebug DecisionDebug => new(AiState, BehaviorProfile.Name,
        AiState == DefenderAiState.Recovery ? "Physics/recovery owns motion" : _decisionReason,
        _approach.PursuitTarget, _approach.PredictedTarget, _approach.Direction,
        _approach.BrakingDistance, _approach.RequiredStoppingDistance, _wrapReachable, _lungeReachable,
        _selectedTackleTarget, DirectionLocked ? _movementDirection : Vector3.Zero);

    private void ResetDecisions()
    {
        _approach = new(DefenderAiState.Pursuit, _spawnPosition, _spawnPosition, Vector3.Zero, 0, 0, 0, false, false, false, "Reset");
        _wrapReachable = _lungeReachable = false;
        _selectedTackleTarget = null;
        _decisionReason = "Reset";
    }

    // AI senses and chooses intent; Opponent.Tackle executes that intent. Reachable
    // body aiming and swept contact remain owned by the existing aiming/contact path.
    public void Update(Vector3 playerPosition, float deltaTime, Vector3? pursuitTarget = null,
        Vector3? carrierVelocity = null, Vector3? carrierPredictedDirection = null,
        PlayerPhysicalAttributes? carrierPhysical = null, Ragdoll? carrierPose = null, bool carrierEvading = false)
    {
        float dt = Math.Max(0, deltaTime);
        ContactCooldown = Math.Max(0, ContactCooldown - dt);
        if (UpdatePhysicsAndRecovery(dt)) return;
        ObserveCarrier(playerPosition, carrierVelocity ?? Vector3.Zero, carrierPhysical ?? _defaultCarrierPhysical,
            carrierPose, carrierEvading, dt);
        _approach = DefenderDecision.PlanApproach(BehaviorProfile, _config, _position, playerPosition,
            pursuitTarget, carrierPredictedDirection, CurrentSpeed, TackleReadySpeed, Physical.WrapReach,
            State != DefenderState.Locomotion);

        if (IsTackleCommitted)
        {
            _decisionReason = "Committed execution";
            // Fresh observations support only bounded early correction. Decisions cannot
            // replace a commitment, turn a locked dive, or bypass its recovery.
            Update(playerPosition, dt, _approach.Ready, Vector2.Zero, false,
                _approach.PursuitTarget, _aimTarget.Point);
            return;
        }

        _selectedTackleTarget = null;
        Vector3 facing = Vector3.Transform(-Vector3.UnitZ, Matrix4x4.CreateRotationY(_yawDegrees * MathF.PI / 180));
        var waist = TackleAiming.Target(playerPosition, carrierPhysical ?? _defaultCarrierPhysical,
            TackleBodyRegion.Waist, carrierPose);
        _wrapReachable = CurrentSpeed <= TackleReadySpeed + _config.OpponentWrapSpeedTolerance &&
            TackleAiming.TryWrap(_position, Velocity, facing, _aimCarrierVelocity, Physical, waist, _config, out _);
        CurrentContactSolution = SolveContact(SupportedLungeDuration, 0, facing);
        _lungeReachable = CurrentContactSolution.Reachable;
        Vector3 offset = playerPosition - _position;
        offset.Y = 0;
        float closing = Math.Max(0, Vector3.Dot(Velocity - _aimCarrierVelocity,
            _approach.Distance > .0001f ? offset / _approach.Distance : Vector3.Zero));
        float urgentDistance = _config.OpponentLungeContactDistance * Physical.HeightRatio +
            closing * _config.OpponentLungeAnimationLeadSeconds;
        var choice = DefenderDecision.SelectTackle(BehaviorProfile, _approach,
            State == DefenderState.TackleReady, _wrapReachable, _lungeReachable,
            Physical.WrapReach, Physical.HeightRatio, urgentDistance, out string tackleReason);
        _decisionReason = _approach.Reason;
        if (choice == DefenderTackleChoice.Wrap)
        {
            TackleAiming.TryWrap(_position, Velocity, facing, _aimCarrierVelocity, Physical, waist, _config, out var point);
            _aimTarget = waist;
            CurrentContactSolution = new(true, point, 0, facing, TackleBodyRegion.Waist, PredictionConfidence);
            _decisionReason = "Reachable wrap selected";
            CommitTackle(point, DefenderState.SetWrap);
        }
        else if (choice == DefenderTackleChoice.Lunge)
        {
            _decisionReason = "Reachable lunge selected";
            CommitTackle(CurrentContactSolution.ContactPoint, DefenderState.LungeTackle);
        }
        else if (_approach.State != DefenderAiState.Pursuit)
            _decisionReason += "; " + (PredictionConfidence < _config.TackleMinimumPredictionConfidence
                ? "Low prediction confidence" : tackleReason);

        bool ready = _approach.Ready || _approach.InReadyCone && PredictionConfidence < .5f &&
            _approach.Distance <= BehaviorProfile.BreakdownExitDistance;
        float yaw = ReadyYaw(_aimTarget.Point, dt);
        Vector3 targetOffset = _approach.PursuitTarget - _position;
        targetOffset.Y = 0;
        if (targetOffset.LengthSquared() < _config.OpponentPredictionArrivalDistance * _config.OpponentPredictionArrivalDistance)
            targetOffset = offset;
        float targetDistance = targetOffset.Length();
        Vector3 local = Vector3.Transform(targetOffset, Matrix4x4.CreateRotationY(-yaw * MathF.PI / 180));
        Vector2 movement = targetDistance > _config.OpponentPredictionArrivalDistance
            ? new Vector2(local.X, -local.Z) / targetDistance : Vector2.Zero;
        Update(playerPosition, dt, ready, movement, false, _approach.PursuitTarget, _aimTarget.Point);
    }

    private void ObserveCarrier(Vector3 position, Vector3 velocity, PlayerPhysicalAttributes physical,
        Ragdoll? pose, bool evading, float dt)
    {
        _tackleObservation.Observe(velocity, dt, evading, _config);
        _aimCarrierVelocity = velocity;
        _aimCarrierPosition = position;
        _aimTarget = TackleAiming.Target(position, physical,
            State == DefenderState.SetWrap ? TackleBodyRegion.Waist : TackleBodyRegion.PelvisLowerChest, pose);
    }
}
