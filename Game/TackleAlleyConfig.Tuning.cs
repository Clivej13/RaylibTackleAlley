using System.Numerics;
using System.Text.Json.Serialization;

namespace RaylibTackleAlley.Game;

// Gameplay-facing values use the former code constants as defaults. Distances are metres,
// times seconds, masses kilograms, angles degrees unless the property says Radians.
public sealed partial class TackleAlleyConfig
{
    public float CameraLookBackHalfAngleDegrees { get; set; } = 10f;
    public float PlayerReversalInputThreshold { get; set; } = .55f;
    public float PlayerReversalWindow { get; set; } = .25f;
    public float PlayerReversalSpeedLoss { get; set; } = .35f;
    public float PlayerReversalAccelerationDelay { get; set; } = .45f;
    public float PlayerReversalAdditionalDelay { get; set; } = .2f;
    public float PlayerReversalMaximumDelay { get; set; } = 1f;

    public float PlayerEvadeMaximumDistanceScale { get; set; } = 1f;

    // Player movement and gestures
    public float PlayerVisualHeight { get; set; } = PlayerVisualProfile.ReferenceHeight;
    public float PlayerSprintSteeringRate { get; set; } = 2f / 0.6f;
    public float PlayerMaxRunYawDegrees { get; set; } = 45f;
    public float PlayerRunYawResponse { get; set; } = 12f;
    public float PlayerSpinDuration { get; set; } = 0.45f;
    public float PlayerSpinSpeed { get; set; } = 4f;
    public float PlayerSpinGestureWindow { get; set; } = 0.4f;
    public float PlayerCutThreshold { get; set; } = 0.65f;
    public float PlayerCutReversalWindow { get; set; } = 0.40f;
    public float PlayerCutDuration { get; set; } = 22f / 60f;
    public float SprintInputThreshold { get; set; } = .5f;
    public float SlowInputThreshold { get; set; } = .35f;
    public float EvadeReleaseThreshold { get; set; } = .25f;
    public float JukeInputThreshold { get; set; } = .65f;
    public float JukeSpeedRetention { get; set; } = .50f;
    public float SpinSpeedRetention { get; set; } = .30f;
    public float PlayerBoundaryRadius { get; set; } = .7f;
    public float SpinBackThreshold { get; set; } = .65f;
    public float SpinBackLateralTolerance { get; set; } = .35f;
    public float SpinSideThreshold { get; set; } = .65f;
    public float SpinSideBackLimit { get; set; } = .75f;
    public float FullSteeringForwardRetention { get; set; } = .75f;
    public float MouseGestureNoisePixels { get; set; } = 2f;

    // Defender decisions and interception
    public float OpponentDownDuration { get; set; } = 2f;
    public float OpponentRecoveryGroundDelay { get; set; } = .15f;
    public float OpponentRecoveryBlendDuration { get; set; } = .2f;
    public float OpponentGetUpPlaybackSpeed { get; set; } = 2f;
    // A shallow 5 cm hop keeps the authored forward lean at midsection height.
    public float OpponentLungeLaunchVerticalSpeed { get; set; } = 1f;
    public float OpponentFallGravity { get; set; } = 10f;
    public float OpponentReadyHalfAngleDegrees { get; set; } = 30f;
    public float OpponentReadyEnterDistance { get; set; } = 4f;
    public float OpponentReadyExitDistance { get; set; } = 5f;
    public float OpponentWrapCommitDistance { get; set; } = 1f;
    public float OpponentWrapContainmentAngleDegrees { get; set; } = 45f;
    public float OpponentWrapSpeedTolerance { get; set; } = 0.25f;
    public float OpponentLungeReachDistance { get; set; } = 2.75f;
    public float OpponentLungeAnimationLeadSeconds { get; set; } = .28f;
    public float OpponentMaximumLungeReachDistance { get; set; } = 4.5f;
    public float OpponentLungeVelocityChangeTolerance { get; set; } = 2f;
    public float OpponentLungeReachAngleDegrees { get; set; } = 45f;
    public float OpponentTackleForwardAngleDegrees { get; set; } = 15f;
    public float OpponentBreakdownReactionSeconds { get; set; } = 0.15f;
    public float OpponentReadyTurnDegreesPerSecond { get; set; } = 360f;
    public float PursuitJogPredictionDistance { get; set; } = 1f;
    public float PursuitRunPredictionDistance { get; set; } = 2f;
    public float PursuitSprintPredictionDistance { get; set; } = 3f;
    public float PursuitStationarySpeed { get; set; } = 0.05f;
    public float PursuitDirectionResponse { get; set; } = 10f;
    public float LungeMaximumPredictionSeconds { get; set; } = .55f;
    public float LungeFullPredictionDistance { get; set; } = 3f;
    public float LungeMaximumLeadDistanceRatio { get; set; } = 1f;
    public float OpponentVisualHeight { get; set; } = 2f;
    public float OpponentInitialYawDegrees { get; set; } = 180f;
    public float OpponentPursuitStopDistance { get; set; } = MathF.Sqrt(.001f);
    public float OpponentPredictionArrivalDistance { get; set; } = .1f;
    public float OpponentReadyInputDeadzone { get; set; } = .1f;
    public float OpponentReadyAnimationSpeedThreshold { get; set; } = .1f;
    public float OpponentLungeContactDistance { get; set; } = .5f;

    // Camera and run flow
    public float CameraFovY { get; set; } = 55f;
    public float CameraTargetHeight { get; set; } = 1.1f;
    public float CameraLookAheadDistance { get; set; } = 5.5f;
    public float AutoRestartDelay { get; set; } = 2.5f;
    public float EndZoneStopFraction { get; set; } = .5f;

    // Ragdoll simulation and active drive
    public float RagdollGravity { get; set; } = 9.81f;
    public float RagdollLinearDamping { get; set; } = 0.35f;
    public float RagdollAngularDamping { get; set; } = 2.5f;
    public float RagdollGroundFriction { get; set; } = 8f;
    public float RagdollMaximumAngularSpeed { get; set; } = 12f;
    public float RagdollSettleLinearSpeed { get; set; } = 0.12f;
    public float RagdollSettleAngularSpeed { get; set; } = 0.25f;
    public float RagdollSettleDuration { get; set; } = 0.8f;
    public float RagdollGroundContactTolerance { get; set; } = .005f;
    public float RagdollDownTorsoRadiusMultiplier { get; set; } = 3.5f;
    public float RagdollGroundImpactMinimumSpeed { get; set; } = .5f;
    public float RagdollGroundContactHoldSeconds { get; set; } = .08f;
    public float RagdollStruggleDuration { get; set; } = 1.25f;
    public float RagdollStruggleBlendIn { get; set; } = .12f;
    public float RagdollStruggleFadeOut { get; set; } = .5f;
    public float RagdollMotorStiffness { get; set; } = 100f;
    public float RagdollMotorDamping { get; set; } = 12f;
    public float RagdollMaximumMotorAcceleration { get; set; } = 90f;
    public float RagdollMotorPoseResponse { get; set; } = 35f;
    public float RagdollMaximumMotorCorrectionSpeed { get; set; } = 6f;
    public float RagdollFootSupportDistance { get; set; } = .12f;
    public float RagdollSupportStiffness { get; set; } = 32f;
    public float RagdollSupportDamping { get; set; } = 6f;
    public float RagdollMaximumSupportAcceleration { get; set; } = 14f;
    public float RagdollBalanceStiffness { get; set; } = 18f;
    public float RagdollMaximumBalanceAcceleration { get; set; } = 12f;
    public float RagdollHitLimbMotorStrength { get; set; } = .08f;
    public float RagdollHitTorsoMotorStrength { get; set; } = .15f;
    public float RagdollContactYieldSpeed { get; set; } = .75f;
    public float RagdollPelvisRadius { get; set; } = .15f;
    public float RagdollPelvisMass { get; set; } = 18f;
    public float RagdollChestRadius { get; set; } = .19f;
    public float RagdollChestMass { get; set; } = 25f;
    public float RagdollHeadRadius { get; set; } = .13f;
    public float RagdollHeadMass { get; set; } = 6f;
    public float RagdollUpperArmRadius { get; set; } = .065f;
    public float RagdollUpperArmMass { get; set; } = 3f;
    public float RagdollLowerArmRadius { get; set; } = .055f;
    public float RagdollLowerArmMass { get; set; } = 2f;
    public float RagdollUpperLegRadius { get; set; } = .095f;
    public float RagdollUpperLegMass { get; set; } = 9f;
    public float RagdollLowerLegRadius { get; set; } = .07f;
    public float RagdollLowerLegMass { get; set; } = 5f;
    public float RagdollHeadSegmentLength { get; set; } = .12f;
    public float RagdollBalanceTiltLimitRadians { get; set; } = MathF.PI * .45f;
    public float RagdollMinimumSupportStrength { get; set; } = .5f;
    public float RagdollBalanceDamping { get; set; } = 3f;
    public float RagdollGaitPlaybackRate { get; set; } = .8f;

    // Character contact
    public float ContactRestitution { get; set; } = .05f;
    public float ContactFriction { get; set; } = .2f;
    public float ContactImpulseScale { get; set; } = 1f;
    public float ContactMaximumImpulse { get; set; } = 160f;
    public float ContactMaximumTackleImpulse { get; set; } = 1200f;
    public float ContactTackleAngularEnergyShare { get; set; } = .2f;
    public float ContactTorsoRadius { get; set; } = .24f;
    public float ContactPelvisRadius { get; set; } = .19f;
    public float ContactChestShare { get; set; } = .6f;
    public float ContactPenetrationSlop { get; set; } = .005f;
    public float ContactLimbRadiusScale { get; set; } = 1f;
    public float ContactHitLimbAngularShare { get; set; } = .75f;
    public float ContactHitLimbPelvisShare { get; set; } = .1f;
    public float ContactBroadPhaseDistance { get; set; } = 5f;
    public float ContactSubstepDistance { get; set; } = 6f;

    // Legacy/component compatibility only; authored run setup now lives in levels.json.
    public SpawnLocation PlayerSpawn { get; set; } = new();
    public DefenderSpawn[] OpponentSpawns { get; set; } =
    [
        new() { X = -5.5f, Z = -18f }, new() { X = 5.5f, Z = -31f },
        new() { X = -4.5f, Z = -46f }, new() { X = 4.5f, Z = -61f }
    ];

    public JointAngleLimits RagdollTorsoLimits { get; set; } = new()
    { MinimumDegrees = [-35, -30, -25], MaximumDegrees = [35, 30, 25] };
    public JointAngleLimits RagdollNeckLimits { get; set; } = new()
    { MinimumDegrees = [-45, -65, -35], MaximumDegrees = [45, 65, 35] };
    public JointAngleLimits RagdollElbowLimits { get; set; } = new()
    { MinimumDegrees = [-145, -8, -8], MaximumDegrees = [5, 8, 8] };
    public JointAngleLimits RagdollKneeLimits { get; set; } = new()
    { MinimumDegrees = [-5, -6, -6], MaximumDegrees = [145, 6, 6] };
    public JointAngleLimits RagdollHipLimits { get; set; } = new()
    { MinimumDegrees = [-110, -35, -45], MaximumDegrees = [35, 35, 45] };
    public JointAngleLimits RagdollShoulderLimits { get; set; } = new()
    { MinimumDegrees = [-120, -70, -95], MaximumDegrees = [120, 70, 95] };

    public void ValidatePlayer()
    {
        if (BallCarrierProfile is null) throw new ArgumentException("BallCarrierProfile is required.");
        BallCarrierProfile.Validate(nameof(BallCarrierProfile));
        Nonnegative(PlayerEvadeMaximumDistanceScale, nameof(PlayerEvadeMaximumDistanceScale));
        Positive(PlayerReversalInputThreshold, nameof(PlayerReversalInputThreshold));
        Unit(PlayerReversalInputThreshold, nameof(PlayerReversalInputThreshold));
        Positive(PlayerReversalWindow, nameof(PlayerReversalWindow));
        Unit(PlayerReversalSpeedLoss, nameof(PlayerReversalSpeedLoss));
        Nonnegative(PlayerReversalAccelerationDelay, nameof(PlayerReversalAccelerationDelay));
        Nonnegative(PlayerReversalAdditionalDelay, nameof(PlayerReversalAdditionalDelay));
        Nonnegative(PlayerReversalMaximumDelay, nameof(PlayerReversalMaximumDelay));
        if (PlayerReversalMaximumDelay < PlayerReversalAccelerationDelay)
            throw new ArgumentException("Reversal maximum delay must cover the initial delay.");
        Nonnegative(PlayerForwardSpeed, nameof(PlayerForwardSpeed));
        Nonnegative(PlayerLateralSpeed, nameof(PlayerLateralSpeed));
        Nonnegative(PlayerSlowSpeed, nameof(PlayerSlowSpeed));
        Nonnegative(PlayerSprintSpeed, nameof(PlayerSprintSpeed));
        ValidateReturnerSpeedScaling();
        Nonnegative(PlayerJukeSpeed, nameof(PlayerJukeSpeed));
        Positive(PlayerJukeDuration, nameof(PlayerJukeDuration));
        Nonnegative(MouseGestureSensitivity, nameof(MouseGestureSensitivity));
        Nonnegative(ForwardAcceleration, nameof(ForwardAcceleration));
        Nonnegative(ForwardDeceleration, nameof(ForwardDeceleration));
        Positive(PlayerVisualHeight, nameof(PlayerVisualHeight));
        Positive(PlayerSprintSteeringRate, nameof(PlayerSprintSteeringRate));
        Nonnegative(PlayerMaxRunYawDegrees, nameof(PlayerMaxRunYawDegrees));
        Nonnegative(PlayerRunYawResponse, nameof(PlayerRunYawResponse));
        Positive(PlayerSpinDuration, nameof(PlayerSpinDuration));
        Nonnegative(PlayerSpinSpeed, nameof(PlayerSpinSpeed));
        Positive(PlayerSpinGestureWindow, nameof(PlayerSpinGestureWindow));
        Unit(PlayerCutThreshold, nameof(PlayerCutThreshold));
        Positive(PlayerCutReversalWindow, nameof(PlayerCutReversalWindow));
        Positive(PlayerCutDuration, nameof(PlayerCutDuration));
        Unit(SprintInputThreshold, nameof(SprintInputThreshold));
        Unit(SlowInputThreshold, nameof(SlowInputThreshold));
        Unit(EvadeReleaseThreshold, nameof(EvadeReleaseThreshold));
        Unit(JukeInputThreshold, nameof(JukeInputThreshold));
        Unit(JukeSpeedRetention, nameof(JukeSpeedRetention));
        Unit(SpinSpeedRetention, nameof(SpinSpeedRetention));
        Nonnegative(PlayerBoundaryRadius, nameof(PlayerBoundaryRadius));
        Unit(SpinBackThreshold, nameof(SpinBackThreshold));
        Unit(SpinBackLateralTolerance, nameof(SpinBackLateralTolerance));
        Unit(SpinSideThreshold, nameof(SpinSideThreshold));
        Unit(SpinSideBackLimit, nameof(SpinSideBackLimit));
        Unit(FullSteeringForwardRetention, nameof(FullSteeringForwardRetention));
        Nonnegative(MouseGestureNoisePixels, nameof(MouseGestureNoisePixels));
        if (EvadeReleaseThreshold >= JukeInputThreshold || EvadeReleaseThreshold >= SpinBackThreshold ||
            EvadeReleaseThreshold >= SpinSideThreshold)
            throw new ArgumentException("Evade release threshold must be below gesture activation thresholds.");
        if (PlayerSpawn is null) throw new ArgumentException("PlayerSpawn is required.");
        PlayerSpawn.Validate();
    }

    public void ValidateCamera()
    {
        Positive(CameraLookBackHalfAngleDegrees, nameof(CameraLookBackHalfAngleDegrees));
        if (CameraLookBackHalfAngleDegrees >= 90)
            throw new ArgumentException("Rear-view half angle must be smaller than 90 degrees.");
        Positive(CameraSpeed1Distance, nameof(CameraSpeed1Distance));
        Positive(CameraSpeed2Distance, nameof(CameraSpeed2Distance));
        Positive(CameraSpeed3Distance, nameof(CameraSpeed3Distance));
        Positive(CameraHeight, nameof(CameraHeight));
        Nonnegative(CameraSmoothing, nameof(CameraSmoothing));
        Nonnegative(CameraSteeringYawDegrees, nameof(CameraSteeringYawDegrees));
        Nonnegative(CameraLookSmoothing, nameof(CameraLookSmoothing));
        Unit(CameraLookBackThreshold, nameof(CameraLookBackThreshold));
        Unit(CameraLookBackReleaseThreshold, nameof(CameraLookBackReleaseThreshold));
        Positive(CameraFovY, nameof(CameraFovY));
        Nonnegative(CameraTargetHeight, nameof(CameraTargetHeight));
        Nonnegative(CameraLookAheadDistance, nameof(CameraLookAheadDistance));
        if (!(PlayerSlowSpeed < PlayerForwardSpeed && PlayerForwardSpeed < PlayerSprintSpeed))
            throw new ArgumentException("Camera speed anchors require PlayerSlowSpeed < PlayerForwardSpeed < PlayerSprintSpeed.");
        if (CameraLookBackReleaseThreshold >= CameraLookBackThreshold || CameraFovY >= 180)
            throw new ArgumentException("Camera release threshold must be below activation, and FOV below 180 degrees.");
    }

    public void ValidateTackle()
    {
        ValidateTackleAiming();
        ValidateOpponentLocomotion();
        Nonnegative(OpponentJogSpeed, nameof(OpponentJogSpeed));
        Nonnegative(OpponentRunSpeed, nameof(OpponentRunSpeed));
        Nonnegative(OpponentSprintSpeed, nameof(OpponentSprintSpeed));
        Positive(OpponentRunDistance, nameof(OpponentRunDistance));
        Nonnegative(OpponentSprintDistance, nameof(OpponentSprintDistance));
        Nonnegative(OpponentPaceHysteresis, nameof(OpponentPaceHysteresis));
        Nonnegative(OpponentDownDuration, nameof(OpponentDownDuration));
        Nonnegative(OpponentRecoveryGroundDelay, nameof(OpponentRecoveryGroundDelay));
        Positive(OpponentRecoveryBlendDuration, nameof(OpponentRecoveryBlendDuration));
        Positive(OpponentGetUpPlaybackSpeed, nameof(OpponentGetUpPlaybackSpeed));
        Nonnegative(OpponentLungeLaunchVerticalSpeed, nameof(OpponentLungeLaunchVerticalSpeed));
        Positive(OpponentFallGravity, nameof(OpponentFallGravity));
        Nonnegative(OpponentReadyHalfAngleDegrees, nameof(OpponentReadyHalfAngleDegrees));
        if (OpponentReadyHalfAngleDegrees > 90f)
            throw new ArgumentException("OpponentReadyHalfAngleDegrees must be at most 90 degrees.");
        Nonnegative(OpponentReadyEnterDistance, nameof(OpponentReadyEnterDistance));
        Nonnegative(OpponentReadyExitDistance, nameof(OpponentReadyExitDistance));
        Nonnegative(OpponentWrapCommitDistance, nameof(OpponentWrapCommitDistance));
        Nonnegative(OpponentWrapContainmentAngleDegrees, nameof(OpponentWrapContainmentAngleDegrees));
        Nonnegative(OpponentWrapSpeedTolerance, nameof(OpponentWrapSpeedTolerance));
        Nonnegative(OpponentLungeReachDistance, nameof(OpponentLungeReachDistance));
        Positive(OpponentLungeAnimationLeadSeconds, nameof(OpponentLungeAnimationLeadSeconds));
        Nonnegative(OpponentMaximumLungeReachDistance, nameof(OpponentMaximumLungeReachDistance));
        Nonnegative(OpponentLungeVelocityChangeTolerance, nameof(OpponentLungeVelocityChangeTolerance));
        Nonnegative(OpponentLungeReachAngleDegrees, nameof(OpponentLungeReachAngleDegrees));
        Nonnegative(OpponentTackleForwardAngleDegrees, nameof(OpponentTackleForwardAngleDegrees));
        Nonnegative(OpponentBreakdownReactionSeconds, nameof(OpponentBreakdownReactionSeconds));
        Nonnegative(OpponentReadyTurnDegreesPerSecond, nameof(OpponentReadyTurnDegreesPerSecond));
        Positive(OpponentVisualHeight, nameof(OpponentVisualHeight));
        Finite(OpponentInitialYawDegrees, nameof(OpponentInitialYawDegrees));
        Nonnegative(OpponentPursuitStopDistance, nameof(OpponentPursuitStopDistance));
        Nonnegative(OpponentPredictionArrivalDistance, nameof(OpponentPredictionArrivalDistance));
        Unit(OpponentReadyInputDeadzone, nameof(OpponentReadyInputDeadzone));
        Unit(OpponentReadyAnimationSpeedThreshold, nameof(OpponentReadyAnimationSpeedThreshold));
        Nonnegative(OpponentLungeContactDistance, nameof(OpponentLungeContactDistance));
        if (OpponentReadyEnterDistance > OpponentReadyExitDistance ||
            OpponentLungeReachDistance > OpponentMaximumLungeReachDistance)
            throw new ArgumentException("Defender distance bounds are reversed.");
        if (OpponentWrapContainmentAngleDegrees > 180 || OpponentLungeReachAngleDegrees > 180 ||
            OpponentTackleForwardAngleDegrees > 180)
            throw new ArgumentException("Tackle angles must not exceed 180 degrees.");
    }

    public void ValidatePrediction()
    {
        Nonnegative(PursuitJogPredictionDistance, nameof(PursuitJogPredictionDistance));
        Nonnegative(PursuitRunPredictionDistance, nameof(PursuitRunPredictionDistance));
        Nonnegative(PursuitSprintPredictionDistance, nameof(PursuitSprintPredictionDistance));
        Nonnegative(PursuitStationarySpeed, nameof(PursuitStationarySpeed));
        Nonnegative(PursuitDirectionResponse, nameof(PursuitDirectionResponse));
        Nonnegative(LungeMaximumPredictionSeconds, nameof(LungeMaximumPredictionSeconds));
        Positive(LungeFullPredictionDistance, nameof(LungeFullPredictionDistance));
        Nonnegative(LungeMaximumLeadDistanceRatio, nameof(LungeMaximumLeadDistanceRatio));
    }

    public void ValidateRagdollPhysics()
    {
        Positive(RagdollDownDuration, nameof(RagdollDownDuration));
        Positive(RagdollDownBlendDuration, nameof(RagdollDownBlendDuration));
        Nonnegative(RagdollGravity, nameof(RagdollGravity));
        Nonnegative(RagdollLinearDamping, nameof(RagdollLinearDamping));
        Nonnegative(RagdollAngularDamping, nameof(RagdollAngularDamping));
        Nonnegative(RagdollGroundFriction, nameof(RagdollGroundFriction));
        Positive(RagdollMaximumAngularSpeed, nameof(RagdollMaximumAngularSpeed));
        Nonnegative(RagdollSettleLinearSpeed, nameof(RagdollSettleLinearSpeed));
        Nonnegative(RagdollSettleAngularSpeed, nameof(RagdollSettleAngularSpeed));
        Positive(RagdollSettleDuration, nameof(RagdollSettleDuration));
        Nonnegative(RagdollGroundContactTolerance, nameof(RagdollGroundContactTolerance));
        Nonnegative(RagdollDownTorsoRadiusMultiplier, nameof(RagdollDownTorsoRadiusMultiplier));
        Nonnegative(RagdollGroundImpactMinimumSpeed, nameof(RagdollGroundImpactMinimumSpeed));
        Nonnegative(RagdollGroundContactHoldSeconds, nameof(RagdollGroundContactHoldSeconds));
        Nonnegative(RagdollStruggleDuration, nameof(RagdollStruggleDuration));
        Positive(RagdollStruggleBlendIn, nameof(RagdollStruggleBlendIn));
        Positive(RagdollStruggleFadeOut, nameof(RagdollStruggleFadeOut));
        Nonnegative(RagdollMotorStiffness, nameof(RagdollMotorStiffness));
        Nonnegative(RagdollMotorDamping, nameof(RagdollMotorDamping));
        Nonnegative(RagdollMaximumMotorAcceleration, nameof(RagdollMaximumMotorAcceleration));
        Nonnegative(RagdollMotorPoseResponse, nameof(RagdollMotorPoseResponse));
        Nonnegative(RagdollMaximumMotorCorrectionSpeed, nameof(RagdollMaximumMotorCorrectionSpeed));
        Nonnegative(RagdollFootSupportDistance, nameof(RagdollFootSupportDistance));
        Nonnegative(RagdollSupportStiffness, nameof(RagdollSupportStiffness));
        Nonnegative(RagdollSupportDamping, nameof(RagdollSupportDamping));
        Nonnegative(RagdollMaximumSupportAcceleration, nameof(RagdollMaximumSupportAcceleration));
        Nonnegative(RagdollBalanceStiffness, nameof(RagdollBalanceStiffness));
        Nonnegative(RagdollMaximumBalanceAcceleration, nameof(RagdollMaximumBalanceAcceleration));
        Unit(RagdollHitLimbMotorStrength, nameof(RagdollHitLimbMotorStrength));
        Unit(RagdollHitTorsoMotorStrength, nameof(RagdollHitTorsoMotorStrength));
        Nonnegative(RagdollContactYieldSpeed, nameof(RagdollContactYieldSpeed));
        Unit(ContactRestitution, nameof(ContactRestitution));
        Nonnegative(ContactFriction, nameof(ContactFriction));
        Nonnegative(ContactImpulseScale, nameof(ContactImpulseScale));
        Nonnegative(ContactMaximumImpulse, nameof(ContactMaximumImpulse));
        Nonnegative(ContactMaximumTackleImpulse, nameof(ContactMaximumTackleImpulse));
        Unit(ContactTackleAngularEnergyShare, nameof(ContactTackleAngularEnergyShare));
        Positive(ContactTorsoRadius, nameof(ContactTorsoRadius));
        Positive(ContactPelvisRadius, nameof(ContactPelvisRadius));
        Unit(ContactChestShare, nameof(ContactChestShare));
        Nonnegative(ContactPenetrationSlop, nameof(ContactPenetrationSlop));
        Nonnegative(ContactLimbRadiusScale, nameof(ContactLimbRadiusScale));
        Positive(RagdollPelvisRadius, nameof(RagdollPelvisRadius));
        Positive(RagdollPelvisMass, nameof(RagdollPelvisMass));
        Positive(RagdollChestRadius, nameof(RagdollChestRadius));
        Positive(RagdollChestMass, nameof(RagdollChestMass));
        Positive(RagdollHeadRadius, nameof(RagdollHeadRadius));
        Positive(RagdollHeadMass, nameof(RagdollHeadMass));
        Positive(RagdollUpperArmRadius, nameof(RagdollUpperArmRadius));
        Positive(RagdollUpperArmMass, nameof(RagdollUpperArmMass));
        Positive(RagdollLowerArmRadius, nameof(RagdollLowerArmRadius));
        Positive(RagdollLowerArmMass, nameof(RagdollLowerArmMass));
        Positive(RagdollUpperLegRadius, nameof(RagdollUpperLegRadius));
        Positive(RagdollUpperLegMass, nameof(RagdollUpperLegMass));
        Positive(RagdollLowerLegRadius, nameof(RagdollLowerLegRadius));
        Positive(RagdollLowerLegMass, nameof(RagdollLowerLegMass));
        Positive(RagdollHeadSegmentLength, nameof(RagdollHeadSegmentLength));
        Positive(RagdollBalanceTiltLimitRadians, nameof(RagdollBalanceTiltLimitRadians));
        Unit(RagdollMinimumSupportStrength, nameof(RagdollMinimumSupportStrength));
        Nonnegative(RagdollBalanceDamping, nameof(RagdollBalanceDamping));
        Nonnegative(RagdollGaitPlaybackRate, nameof(RagdollGaitPlaybackRate));
        Unit(ContactHitLimbAngularShare, nameof(ContactHitLimbAngularShare));
        Unit(ContactHitLimbPelvisShare, nameof(ContactHitLimbPelvisShare));
        Positive(ContactBroadPhaseDistance, nameof(ContactBroadPhaseDistance));
        Positive(ContactSubstepDistance, nameof(ContactSubstepDistance));
        ValidateRagdollRecovery();
        if (ContactHitLimbAngularShare + ContactHitLimbPelvisShare > 1)
            throw new ArgumentException("Limb and pelvis angular shares must sum to at most one.");
        if (ContactSubstepDistance < ContactBroadPhaseDistance)
            throw new ArgumentException("ContactSubstepDistance must cover ContactBroadPhaseDistance.");
        if (RagdollTorsoLimits is null) throw new ArgumentException("RagdollTorsoLimits is required.");
        RagdollTorsoLimits.Validate(nameof(RagdollTorsoLimits));
        if (RagdollNeckLimits is null) throw new ArgumentException("RagdollNeckLimits is required.");
        RagdollNeckLimits.Validate(nameof(RagdollNeckLimits));
        if (RagdollElbowLimits is null) throw new ArgumentException("RagdollElbowLimits is required.");
        RagdollElbowLimits.Validate(nameof(RagdollElbowLimits));
        if (RagdollKneeLimits is null) throw new ArgumentException("RagdollKneeLimits is required.");
        RagdollKneeLimits.Validate(nameof(RagdollKneeLimits));
        if (RagdollHipLimits is null) throw new ArgumentException("RagdollHipLimits is required.");
        RagdollHipLimits.Validate(nameof(RagdollHipLimits));
        if (RagdollShoulderLimits is null) throw new ArgumentException("RagdollShoulderLimits is required.");
        RagdollShoulderLimits.Validate(nameof(RagdollShoulderLimits));
    }

    public void ValidateField()
    {
        Positive(FieldWidth, nameof(FieldWidth));
        Positive(FieldLength, nameof(FieldLength));
        Positive(EndZoneLength, nameof(EndZoneLength));
        Positive(FieldAssetWidth, nameof(FieldAssetWidth));
        Positive(FieldAssetLength, nameof(FieldAssetLength));
        Positive(StadiumAssetWidth, nameof(StadiumAssetWidth));
        Positive(StadiumAssetLength, nameof(StadiumAssetLength));
        Positive(TackleDistance, nameof(TackleDistance));
        Positive(AutoRestartDelay, nameof(AutoRestartDelay));
        Unit(EndZoneStopFraction, nameof(EndZoneStopFraction));
        if (EndZoneLength >= FieldLength)
            throw new ArgumentException("EndZoneLength must be smaller than FieldLength.");
        if (PlayerBoundaryRadius * 2 >= Math.Min(FieldAssetWidth, StadiumAssetWidth) ||
            PlayerBoundaryRadius * 2 >= Math.Min(FieldAssetLength, StadiumAssetLength))
            throw new ArgumentException("Player boundary radius must fit inside the field.");
        if (OpponentSpawns is null || OpponentSpawns.Any(spawn => spawn is null))
            throw new ArgumentException("OpponentSpawns must be a non-null array of spawn locations.");
        for (int i = 0; i < OpponentSpawns.Length; i++)
        {
            var spawn = OpponentSpawns[i];
            spawn.Validate();
            if (spawn.Profile is null) throw new ArgumentException($"OpponentSpawns[{i}].Profile is required.");
            spawn.Profile.Validate($"OpponentSpawns[{i}].Profile");
        }
    }

    public void Validate()
    {
        ValidatePlayer();
        ValidateCamera();
        ValidateOpponentLocomotion();
        ValidateTackle();
        ValidatePrediction();
        ValidateRagdollPhysics();
        ValidateField();
        _ = new PlayerPhysicalAttributes(BallCarrierProfile, this);
        foreach (var spawn in OpponentSpawns) _ = new PlayerPhysicalAttributes(spawn.Profile, this);
        if (BallCarrierProfileExamples is null || BallCarrierProfileExamples.Any(p => p is null))
            throw new ArgumentException("Carrier examples must be non-null profiles.");
        foreach (var profile in BallCarrierProfileExamples) _ = new PlayerPhysicalAttributes(profile, this);
    }

    private static void Finite(float value, string name)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(name, "Must be finite.");
    }
    private static void Nonnegative(float value, string name)
    {
        Finite(value, name);
        if (value < 0) throw new ArgumentOutOfRangeException(name, "Must be nonnegative.");
    }
    private static void Positive(float value, string name)
    {
        Finite(value, name);
        if (value <= 0) throw new ArgumentOutOfRangeException(name, "Must be positive.");
    }
    private static void Unit(float value, string name)
    {
        Nonnegative(value, name);
        if (value > 1) throw new ArgumentOutOfRangeException(name, "Must be between zero and one.");
    }
}

public sealed class DefenderSpawn : SpawnLocation
{
    public PlayerProfile Profile { get; set; } = new();
}

public class SpawnLocation
{
    public float X { get; set; }
    public float Z { get; set; }
    [JsonIgnore] public Vector3 Position => new(X, 0, Z);
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Z))
            throw new ArgumentException("Spawn coordinates must be finite.");
    }
}

public sealed class JointAngleLimits
{
    public float[] MinimumDegrees { get; set; } = [0, 0, 0];
    public float[] MaximumDegrees { get; set; } = [0, 0, 0];
    [JsonIgnore] public Vector3 Minimum => new(MinimumDegrees[0], MinimumDegrees[1], MinimumDegrees[2]);
    [JsonIgnore] public Vector3 Maximum => new(MaximumDegrees[0], MaximumDegrees[1], MaximumDegrees[2]);
    internal void Validate(string name)
    {
        if (MinimumDegrees is not { Length: 3 } || MaximumDegrees is not { Length: 3 })
            throw new ArgumentException($"{name} requires three minimum and maximum angles.");
        for (int i = 0; i < 3; i++)
            if (!float.IsFinite(MinimumDegrees[i]) || !float.IsFinite(MaximumDegrees[i]) ||
                MinimumDegrees[i] < -180 || MaximumDegrees[i] > 180 || MinimumDegrees[i] > MaximumDegrees[i])
                throw new ArgumentException($"{name} must have ordered finite angles within -180 to 180 degrees.");
    }
}
