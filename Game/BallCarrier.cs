using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.Input;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;

namespace RaylibTackleAlley.Game;

public sealed partial class BallCarrier
{
    private readonly TackleAlleyConfig _config;
    private readonly RightStickInput _rightStick;
    private ModelInstance? _model;
    // Borrowed asset value; only the draw-local copy's transform is changed.
    private Model? _football;
    private Matrix4x4 _footballWorldTransform = Matrix4x4.Identity;
    private const string CarryHandBone = "Hand.R";

    // Metres, row-vector convention. Derived from player_hand_rig.py's
    // fit_carry/attach_football, not a world-space offset. See Tests/FootballAttachment.md.
    private static readonly Matrix4x4 FootballGripLocal = new(
        -0.35795751f, 0.34925157f, -0.86596175f, 0f,
        -0.61571349f, 0.60892960f, 0.50010163f, 0f,
         0.70197102f, 0.71219947f, -0.00293202f, 0f,
        -0.12157734f, 0.02833468f, -0.00747214f, 1f);

    // Vertical motion comes from the authored pose, with only static ground alignment.
    private Vector3 RenderPosition => Position + new Vector3(0f, _groundOffset, 0f);

    private Matrix4x4 PlayerWorldTransform =>
        Matrix4x4.Transpose(_model?.Model.Transform ?? Matrix4x4.Identity) *
        Matrix4x4.CreateScale(_visualScale) *
        Matrix4x4.CreateRotationY(VisualYawDegrees * MathF.PI / 180f) *
        Matrix4x4.CreateTranslation(RenderPosition);

    private void UpdateFootballAttachment()
    {
        if (_model is null) return;
        if (_animation is null || !_animation.TryGetBoneTransform(CarryHandBone, out Matrix4x4 hand))
            throw new InvalidDataException("Carry animation must expose the current animated Hand.R transform.");
        // AnimationPlayer.Update/Seek applies the pose before this query. World state
        // (position, yaw and spin) has also finished updating for this frame.
        _footballWorldTransform = FootballGripLocal * hand * PlayerWorldTransform;
    }
    private readonly Dictionary<string, AnimationPlayer> _animations = new();
    private AnimationPlayer? _animation;
    private float _visualScale;
    private float _groundOffset;
    // Two input units (-1 to +1) in 0.6 seconds; small corrections settle sooner.
    private float _effectiveLateral;
    private int _lastSteeringSide, _reversalChain;
    private float _steeringNeutralTime, _reversalDelay;
    public float SteeringRecoveryRemaining => _reversalDelay;
    private float _currentRunYaw;
    private float _targetRunYaw;

    // Input yaw is left-negative/right-positive. Around world +Y, authored -Z
    // needs the opposite sign to face the corresponding world X direction.
    private float VisualYawDegrees => -_currentRunYaw + (_spinRemaining > 0f &&
        !(_animation is not null && _animations.TryGetValue(EvadeAnimationName ?? "", out var evade) && evade == _animation)
        ? _spinDirection * 360f * (1f - _spinRemaining / _config.PlayerSpinDuration) : 0f);

    public Vector3 Position { get; private set; }
    private float _evadeDistanceScale;
    private float _jukeRemaining;
    private int _jukeDirection;
    private bool _jukeReady = true;
    private float _spinGestureRemaining;
    private bool _spinGestureReady = true;
    private bool _spinGestureConsumed;
    private bool _mouseSpinGesture;
    private float _spinRemaining;
    private int _spinDirection;

    // Authored keys 1-23 at 60 Hz, including the final recovery pose.
    private bool _wasSprinting;
    private int _lastCutSide;
    private float _cutReversalRemaining;
    private string? _cutName;
    private float _cutRemaining;

    public bool IsTaunting { get; private set; }
    private float _tauntElapsed;
    public bool TauntComplete => IsTaunting && _tauntElapsed >= 2.6f;

    public int SpeedTier { get; private set; } = 2;
    public float CurrentForwardSpeed { get; private set; }
    public float TargetForwardSpeed => Speed;
    public float Speed => SpeedTier switch
    {
        1 => _config.PlayerSlowSpeed,
        3 => _config.PlayerSprintSpeed,
        _ => _config.PlayerForwardSpeed
    };

    // Juke assets use rig labels: Left moves +X, Right moves -X (opposite rear-screen input).
    private string? EvadeAnimationName => _spinRemaining > 0f ? (_spinDirection < 0 ? "SpinLeft" : "SpinRight")
        : _jukeRemaining > 0f ? (_jukeDirection < 0 ? "JukeRight" : "JukeLeft") : null;

    // Tier 0 remains reserved; no stationary clip is selected.
    public string AnimationName => _recovery is not null ? (_recovery.Phase == RecoveryPhase.Down ? "Down" : "GetUp") : IsTaunting ? "TauntBicepFlex" : _cutName ?? EvadeAnimationName ?? (SpeedTier switch
    {
        1 => "CarryJog",
        2 => "CarryRun",
        3 => "CarrySprint",
        _ => throw new InvalidOperationException("No locomotion clip for this speed tier.")
    });

    /// <summary>Apply a loaded texture asset to this player's Uniform material.</summary>
    public void ApplyUniform(AssetManager assets, string textureKey) =>
        PlayerUniform.ApplyUniform(_model ??
            throw new InvalidOperationException("Initialize player visuals before selecting a uniform."),
            assets, textureKey);

    // AssetManager owns instances and borrowed clips; instances are released first.
    public unsafe void InitializeVisual(AssetManager assets)
    {
        if (_model is not null)
            return;

        var clips = new Dictionary<string, ModelAnimation>();
        foreach (string key in new[] { "FootballPlayerCarryJogAnimations", "FootballPlayerCarryRunAnimations", "FootballPlayerCarrySprintAnimations",
            "FootballPlayerCutLeftAnimations", "FootballPlayerCutRightAnimations",
            "FootballPlayerJukeLeftAnimations", "FootballPlayerJukeRightAnimations",
            "FootballPlayerSpinLeftAnimations", "FootballPlayerSpinRightAnimations", "FootballPlayerTauntBicepFlexAnimations" })
            foreach (ModelAnimation clip in assets.GetModelAnimations(key))
                clips[new string(clip.Name)] = clip;

        ModelInstance model = assets.CreateModelInstance("FootballPlayer");
        try
        {
            // Construct the initial CarryRun player last so its starting pose is applied last.
            foreach (string name in new[] { "TauntBicepFlex", "CutLeft", "CutRight", "JukeLeft", "JukeRight", "SpinLeft", "SpinRight", "CarryJog", "CarrySprint", "CarryRun" })
            {
                if (!clips.TryGetValue(name, out ModelAnimation clip) || clip.KeyFrameCount <= 0 ||
                    !Raylib.IsModelAnimationValid(model.Model, clip))
                    throw new InvalidDataException($"{name} must be nonempty and compatible with FootballPlayer.");
                // BallCarrier owns its playback clocks and deformable model instance.
                // Raylib resamples glTF animation at 60 Hz independently of Blender FPS.
                _animations.Add(name, new AnimationPlayer(model, clip, loop: name.StartsWith("Carry", StringComparison.Ordinal)));
            }
            BoundingBox bounds = Raylib.GetModelBoundingBox(model.Model);
            float height = bounds.Max.Y - bounds.Min.Y;
            if (!float.IsFinite(height) || height <= 0f)
                throw new InvalidDataException("FootballPlayer must have a finite positive height.");
            _visualScale = _config.PlayerVisualHeight / height;
            _groundOffset = -bounds.Min.Y * _visualScale;
            _football = assets.GetModel("Football");
            _model = model;
            _visualAssets = assets;
            SelectAnimation();
            UpdateFootballAttachment();
        }
        catch
        {
            _model = null;
            _football = null;
            _animation = null;
            _animations.Clear();
            assets.ReleaseModelInstance(model);
            throw;
        }
    }

    private void SelectAnimation()
    {
        if (!_animations.TryGetValue(AnimationName, out AnimationPlayer? next) ||
            ReferenceEquals(next, _animation))
            return;
        float phase = 0f;
        if (!IsTaunting && _cutName is null && EvadeAnimationName is null && _animation is { } previous &&
            (_animations["CarryJog"] == previous || _animations["CarryRun"] == previous || _animations["CarrySprint"] == previous))
        {
            float duration = previous.FrameCount / previous.FramesPerSecond;
            if (float.IsFinite(duration) && duration > 0f &&
                float.IsFinite(previous.CurrentTime))
                phase = previous.CurrentTime / duration;
        }
        // Preserve the gait cycle across clips with different durations.
        _animation = next;
        // Another player has deformed this shared instance since this clock last ran.
        // Visit a different frame so a cached destination still reapplies its pose.
        phase = float.IsFinite(phase) ? phase : 0f;
        _animation.SeekPhase(phase < 0.5f ? 0.75f : 0f);
        _animation.SeekPhase(phase);
    }

    public BallCarrier(TackleAlleyConfig config)
    {
        config.ValidatePlayer();
        config.ValidateRagdollRecovery();
        _config = config;
        Ragdoll = new(config);
        _contactPose = new(config);
        _rightStick = new RightStickInput(config);
        Reset();
    }

    public void Reset()
    {
        Ragdoll.Deactivate(); _ragdollSkeleton = null; _recovery = null;
        IsTaunting = false; _tauntElapsed = 0f;
        HasTackleGroundImpact = false; Velocity = Vector3.Zero;
        _rightStick.IgnoreNextMouseDelta();
        _mouseSpinGesture = false;
        Position = _config.PlayerSpawn.Position;
        _effectiveLateral = 0f;
        ResetSteeringPenalty();
        _currentRunYaw = 0f;
        _targetRunYaw = 0f;
        _cutName = null;
        _cutRemaining = 0f;
        _wasSprinting = false;
        _lastCutSide = 0;
        _cutReversalRemaining = 0f;
        SpeedTier = 2;
        CurrentForwardSpeed = TargetForwardSpeed;
        _evadeDistanceScale = 0f;
        _jukeRemaining = 0f;
        _jukeDirection = 0;
        _jukeReady = true;
        _spinGestureRemaining = 0f;
        _spinGestureReady = true;
        _spinGestureConsumed = false;
        _spinRemaining = 0f;
        _spinDirection = 0;
        SelectAnimation();
        _animation?.SeekTime(0f);
        if (_model is not null && _animation is not null)
            Raylib.UpdateModelAnimation(_model.Model, _animation.Animation, _animation.CurrentFrame);
        UpdateFootballAttachment();
    }

    public void IgnoreNextMouseDelta() => _rightStick.IgnoreNextMouseDelta();

    public void Update(InputController input, float deltaTime, FootballField field)
    {
        if (UpdatePhysicsAndRecovery(deltaTime)) return;
        Vector3 previousPosition = Position;
        float lateral = GetDirectionalValue(input.GetValue("MoveLeft"), input.GetValue("MoveRight"));
        deltaTime = Math.Max(0f, deltaTime);
        // Backward input selects the slow running tier.
        float backwardInput = input.GetValue("MoveBackward");
        bool sprinting = input.GetValue("Sprint") > _config.SprintInputThreshold;
        SpeedTier = sprinting ? 3 : Math.Abs(backwardInput) > _config.SlowInputThreshold ? 1 : 2;

        ObserveSteeringReversal(lateral, deltaTime);
        _effectiveLateral = SpeedTier == 3
            ? _effectiveLateral + Math.Clamp(lateral - _effectiveLateral,
                -_config.PlayerSprintSteeringRate * deltaTime, _config.PlayerSprintSteeringRate * deltaTime)
            : lateral;

        UpdateCutGesture(lateral, deltaTime);
        // Relative cut clips own the turn; retain the incoming external yaw.
        if (_cutName is null)
        {
            // Sprint steering already limits the turn; facing shares that movement state.
            if (sprinting)
                _currentRunYaw = _targetRunYaw = _effectiveLateral * _config.PlayerMaxRunYawDegrees;
            else
                UpdateRunYaw(lateral, deltaTime);
        }

        // Keep Cut advancement and heading handoff at their existing point in the frame.
        bool cutAdvanced = _cutName is not null;
        if (cutAdvanced) AdvanceAnimation(deltaTime);

        var rightStick = _rightStick.Read(input);
        float juke = rightStick.Direction.X;
        float spinX = rightStick.Direction.X;
        float spinBack = Math.Max(0f, rightStick.Direction.Y);
        float headZ = rightStick.Direction.Y;
        bool headFake = sprinting;
        if (headFake)
        {
            // Cancel moves and consume the gesture until the stick returns to neutral.
            _jukeRemaining = 0f;
            _spinRemaining = 0f;
            _spinGestureRemaining = 0f;
            _spinGestureReady = false;
            _spinGestureConsumed = true;
            _jukeReady = false;
        }
        else
        {
            UpdateSpinGesture(spinX, spinBack, headZ, deltaTime, rightStick.IsMouse);
        }
        if (!headFake && Math.Abs(juke) < _config.EvadeReleaseThreshold && !_spinGestureConsumed)
            _jukeReady = true;
        else if (!headFake && Math.Abs(juke) >= _config.JukeInputThreshold && _jukeReady)
        {
            if (_jukeRemaining <= 0f && _spinRemaining <= 0f
                && _spinGestureRemaining <= 0f && !_spinGestureConsumed)
            {
                _jukeDirection = Math.Sign(juke);
                _jukeRemaining = _config.PlayerJukeDuration;
                CaptureEvadeDistance();
                ApplyEvadeSpeedRetention(_config.JukeSpeedRetention);
            }
            // Holding the stick must not repeat or queue a juke.
            _jukeReady = false;
        }

        string? evadeName = EvadeAnimationName;
        float evadeRemaining = _spinRemaining > 0f ? _spinRemaining : _jukeRemaining;
        float jukeTime = Math.Min(deltaTime, Math.Max(0f, _jukeRemaining));
        _jukeRemaining = Math.Max(0f, _jukeRemaining - deltaTime);
        float spinTime = Math.Min(deltaTime, _spinRemaining);
        _spinRemaining = Math.Max(0f, _spinRemaining - deltaTime);
        Vector3 position = Position;
        bool steeringPenaltyActive = _reversalChain > 0;
        // Forward travel is automatic, independent of forward/back input.
        float lateralAmount = Math.Abs(_effectiveLateral);
        float forwardRetention = 1f + (_config.FullSteeringForwardRetention - 1f) * lateralAmount;
        // Recover momentum during either evade, but pause forward travel until it ends.
        // Split at the move boundary so long frames resume only after the animation.
        float evadeTime = jukeTime + spinTime;
        if (evadeTime > 0f) AdvanceForwardSpeed(evadeTime);
        float normalTime = Math.Max(0f, deltaTime - evadeTime);
        float forwardDistance = AdvanceForwardSpeed(normalTime);
        // Integrate sideways motion using the same lost/recovered momentum, so a
        // stopped runner cannot keep zigzagging at full lateral speed.
        float lateralTime = steeringPenaltyActive
            ? (TargetForwardSpeed > 0 ? forwardDistance / TargetForwardSpeed : 0) : normalTime;
        position.X += _jukeDirection * _config.PlayerJukeSpeed * _evadeDistanceScale * jukeTime
            + _spinDirection * _config.PlayerSpinSpeed * _evadeDistanceScale * spinTime
            + _effectiveLateral * _config.PlayerLateralSpeed * lateralTime;
        position.Z -= forwardDistance * forwardRetention;
        Position = field.ClampToOuterBoundary(position, _config.PlayerBoundaryRadius);
        if (deltaTime > 0f) Velocity = (Position - previousPosition) / deltaTime;
        if (!cutAdvanced) AdvanceEvadeAnimation(deltaTime, evadeName, evadeRemaining);
        UpdateFootballAttachment();
    }

    public void RunIntoEndZone(float deltaTime, float stopZ)
    {
        if (Position.Z <= stopZ || IsTaunting)
        {
            IsTaunting = true;
            _cutName = null;
            _jukeRemaining = _spinRemaining = 0f;
            CurrentForwardSpeed = 0f;
            Velocity = Vector3.Zero;
            SelectAnimation();
            float dt = Math.Max(0f, deltaTime);
            _tauntElapsed += dt;
            _animation?.Update(dt);
            UpdateFootballAttachment();
            return;
        }
        if (UpdatePhysicsAndRecovery(deltaTime)) return;
        deltaTime = Math.Max(0f, deltaTime);
        string? evadeName = EvadeAnimationName;
        float evadeRemaining = _spinRemaining > 0f ? _spinRemaining : _jukeRemaining;
        _jukeRemaining = Math.Max(0f, _jukeRemaining - deltaTime);
        _spinRemaining = Math.Max(0f, _spinRemaining - deltaTime);
        if (_cutName is null)
            UpdateRunYaw(0f, deltaTime);
        if (_cutName is not null) AdvanceAnimation(deltaTime);
        else AdvanceEvadeAnimation(deltaTime, evadeName, evadeRemaining);
        Position = new Vector3(Position.X, Position.Y,
            Math.Max(stopZ, Position.Z - AdvanceForwardSpeed(deltaTime)));
        UpdateFootballAttachment();
    }

    private void UpdateCutGesture(float lateral, float deltaTime)
    {
        bool sprinting = SpeedTier == 3;
        bool releasedSprint = _wasSprinting && !sprinting;
        bool pressedSprint = !_wasSprinting && sprinting;
        _wasSprinting = sprinting;
        _cutReversalRemaining = Math.Max(0f, _cutReversalRemaining - deltaTime);
        if (_cutName is not null)
        {
            _lastCutSide = 0;
            _cutReversalRemaining = 0f;
            return;
        }

        // Freeze the previous strong raw direction for this release opportunity.
        // Neutral and Sprint re-presses neither replace it nor refresh its timer.
        if (releasedSprint && _cutReversalRemaining <= 0f && _lastCutSide != 0)
            _cutReversalRemaining = _config.PlayerCutReversalWindow;

        int side = Math.Abs(lateral) >= _config.PlayerCutThreshold ? Math.Sign(lateral) : 0;
        if (_cutReversalRemaining > 0f)
        {
            if (side != 0 && _lastCutSide == -side)
            {
                _cutName = side > 0 ? "CutRight" : "CutLeft";
                _cutRemaining = _config.PlayerCutDuration;
                _targetRunYaw = _currentRunYaw;
                _lastCutSide = 0;
                _cutReversalRemaining = 0f;
            }
            return;
        }

        // Only a Sprint session supplies a direction for the next release.
        if (!sprinting || pressedSprint) _lastCutSide = 0;
        if (sprinting && side != 0) _lastCutSide = side;
    }

    // Playback follows the existing movement timer; it never extends or owns the action.
    private void AdvanceEvadeAnimation(float deltaTime, string? name, float remaining)
    {
        if (name is null)
        {
            AdvanceAnimation(deltaTime);
            return;
        }

        bool spin = name.StartsWith("Spin", StringComparison.Ordinal);
        float duration = spin ? _config.PlayerSpinDuration : _config.PlayerJukeDuration;
        if (_animations.TryGetValue(name, out var clip))
        {
            bool changed = !ReferenceEquals(_animation, clip);
            _animation = clip;
            float progress = Math.Clamp(1f - Math.Max(0f, remaining - deltaTime) / duration, 0f, 1f);
            // Include the last authored pose without looping, regardless of export duration.
            if (changed) clip.SeekTime((progress < 0.5f ? clip.FrameCount - 1 : 0) / clip.FramesPerSecond);
            clip.SeekTime(progress * (clip.FrameCount - 1) / clip.FramesPerSecond);
        }
        if (remaining > deltaTime) return;

        SelectAnimation();
        // Authored exits: JukeLeft/SpinRight phase 1; JukeRight/SpinLeft phase 13.
        _animation?.SeekPhase(name is "JukeRight" or "SpinLeft" ? 0.5f : 0f);
        _animation?.Update(deltaTime - remaining);
    }

    private void AdvanceAnimation(float deltaTime)
    {
        SelectAnimation();
        if (_cutName is null)
        {
            _animation?.Update(deltaTime);
            return;
        }

        float cutTime = Math.Min(deltaTime, _cutRemaining);
        _animation?.Update(cutTime);
        _cutRemaining = Math.Max(0f, _cutRemaining - cutTime);
        if (_cutRemaining > 0f) return;

        // Match the authored exit: CarryRun frame 22 (right) or 10 (left),
        // on its 24-interval gait. Sprint/Jog use the equivalent gait phase.
        float exitPhase = _cutName == "CutRight" ? 21f / 24f : 9f / 24f;
        // Neutral locomotion takes over the absolute heading from the local 90-degree turn.
        _currentRunYaw = _targetRunYaw = _cutName == "CutLeft" ? -_config.PlayerMaxRunYawDegrees : _config.PlayerMaxRunYawDegrees;
        _cutName = null;
        SelectAnimation();
        _animation?.SeekPhase(exitPhase);
        _animation?.Update(deltaTime - cutTime);
    }

    private void ResetSteeringPenalty()
    {
        _lastSteeringSide = _reversalChain = 0;
        _steeringNeutralTime = _reversalDelay = 0;
    }

    private void ObserveSteeringReversal(float lateral, float dt)
    {
        int side = Math.Abs(lateral) >= _config.PlayerReversalInputThreshold ? Math.Sign(lateral) : 0;
        if (side == 0)
        {
            _steeringNeutralTime += dt;
            if (_steeringNeutralTime > _config.PlayerReversalWindow) _lastSteeringSide = 0;
            return;
        }
        if (_lastSteeringSide != 0 && side != _lastSteeringSide &&
            _steeringNeutralTime <= _config.PlayerReversalWindow && _config.PlayerReversalSpeedLoss > 0)
        {
            CurrentForwardSpeed = Math.Max(0, CurrentForwardSpeed - TargetForwardSpeed * _config.PlayerReversalSpeedLoss);
            // Bound the chain counter once further reversals cannot lengthen the delay.
            float delay = _config.PlayerReversalAccelerationDelay +
                _reversalChain * _config.PlayerReversalAdditionalDelay;
            if (_reversalChain == 0 || (_config.PlayerReversalAdditionalDelay > 0 &&
                delay < _config.PlayerReversalMaximumDelay)) _reversalChain++;
            _reversalDelay = Math.Min(delay, _config.PlayerReversalMaximumDelay);
        }
        _lastSteeringSide = side;
        _steeringNeutralTime = 0;
    }

    private void CaptureEvadeDistance()
    {
        // Snapshot physical pace before the move's own speed penalty. Recovery during
        // the animation must not lengthen an evade started slowly or at a standstill.
        _evadeDistanceScale = _config.PlayerForwardSpeed > 0
            ? Math.Clamp(CurrentForwardSpeed / _config.PlayerForwardSpeed, 0,
                _config.PlayerEvadeMaximumDistanceScale) : 0;
    }

    private void ApplyEvadeSpeedRetention(float retention)
    {
        float speed = TargetForwardSpeed * retention;
        CurrentForwardSpeed = _reversalChain > 0 ? Math.Min(CurrentForwardSpeed, speed) : speed;
    }

    private float AdvanceForwardSpeed(float deltaTime)
    {
        // Split at the lockout boundary so catch-up frames cannot accelerate early.
        float heldTime = Math.Min(deltaTime, _reversalDelay);
        var held = SpeedRamp.Advance(CurrentForwardSpeed, Math.Min(CurrentForwardSpeed, TargetForwardSpeed),
            _config.ForwardAcceleration, _config.ForwardDeceleration, heldTime);
        _reversalDelay = Math.Max(0, _reversalDelay - heldTime);
        var step = SpeedRamp.Advance(held.Speed, TargetForwardSpeed,
            _config.ForwardAcceleration, _config.ForwardDeceleration, deltaTime - heldTime);
        CurrentForwardSpeed = step.Speed;
        if (_reversalDelay <= 0 && CurrentForwardSpeed >= TargetForwardSpeed) _reversalChain = 0;
        return held.Distance + step.Distance;
    }

    private void UpdateSpinGesture(float side, float back, float headZ, float deltaTime, bool isMouse)
    {
        _spinGestureRemaining = Math.Max(0f, _spinGestureRemaining - deltaTime);
        if (Math.Abs(side) < _config.EvadeReleaseThreshold && back < _config.EvadeReleaseThreshold && Math.Abs(headZ) < _config.EvadeReleaseThreshold)
        {
            // No mouse delta means no movement, not an explicit stick release.
            // Retain a mouse back gesture only for the existing 0.4-second window.
            if (_mouseSpinGesture && _spinGestureRemaining > 0f)
                return;
            _spinGestureReady = true;
            _spinGestureConsumed = false;
            _spinGestureRemaining = 0f;
            return;
        }

        // Start near straight back, then roll to either side. Controller neutral cancels.
        if (_spinGestureReady && back >= _config.SpinBackThreshold && Math.Abs(side) < _config.SpinBackLateralTolerance)
        {
            _mouseSpinGesture = isMouse;
            _spinGestureReady = false;
            _spinGestureConsumed = true;
            if (_jukeRemaining <= 0f && _spinRemaining <= 0f)
                _spinGestureRemaining = _config.PlayerSpinGestureWindow;
        }

        if (_spinGestureRemaining > 0f && Math.Abs(side) >= _config.SpinSideThreshold && back < _config.SpinSideBackLimit)
        {
            _spinDirection = Math.Sign(side);
            _spinRemaining = _config.PlayerSpinDuration;
            CaptureEvadeDistance();
            ApplyEvadeSpeedRetention(_config.SpinSpeedRetention);
            _spinGestureRemaining = 0f;
            _jukeReady = false;
        }
    }

    private static float GetDirectionalValue(float negativeDirection, float positiveDirection)
    {
        float value = negativeDirection < 0 || positiveDirection < 0
            ? positiveDirection + negativeDirection
            : positiveDirection - negativeDirection;
        return Math.Clamp(value, -1f, 1f);
    }

    private void UpdateRunYaw(float lateral, float deltaTime)
    {
        _targetRunYaw = lateral * _config.PlayerMaxRunYawDegrees;
        float blend = 1f - MathF.Exp(-_config.PlayerRunYawResponse * deltaTime);
        _currentRunYaw += (_targetRunYaw - _currentRunYaw) * blend;
    }

    public void Draw()
    {
        if (_model is null)
            throw new InvalidOperationException("Initialize player visuals after loading assets.");
        if (Ragdoll.IsActive && _ragdollSkeleton is not null)
        {
            _ragdollSkeleton.Apply(_model.Model, Ragdoll);
            _ragdollSkeleton.Draw(_model.Model);
        }
        else if (_recovery is not null) _recovery.Draw();
        else Raylib.DrawModelEx(_model.Model, RenderPosition,
            Vector3.UnitY, VisualYawDegrees, new Vector3(_visualScale), Color.White);
        if (_football is not { } football)
            throw new InvalidOperationException("Initialize the standalone football asset.");
        // Model is a value copy; transpose only at the System.Numerics -> native Raylib boundary.
        football.Transform = Matrix4x4.Transpose(_footballWorldTransform);
        Raylib.DrawModelEx(football, Vector3.Zero, Vector3.UnitY, 0f, Vector3.One, Color.White);
    }
}
