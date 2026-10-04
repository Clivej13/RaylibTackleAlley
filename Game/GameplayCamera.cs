using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

// Presentation only: entry facing seeds orbit; follow/look never write simulation state.
public sealed class GameplayCamera
{
    private readonly TackleAlleyConfig _config;
    private Vector3 _anchor;
    private float _yaw, _pitch, _desiredYaw, _desiredPitch;
    private float _entryYaw, _distance;
    private Vector2 _heldDirection;
    private float _heldMovementYaw, _heldLookYaw;
    public CameraMode Mode { get; private set; }
    public Camera3D Camera { get; private set; }
    public float MovementYawDegrees => _entryYaw + _yaw;

    public GameplayCamera(TackleAlleyConfig config)
    {
        config.ValidateCamera();
        _config = config;
        Mode = config.DefaultCameraMode;
        Reset(Vector3.Zero);
    }

    public void SetMode(CameraMode mode, Vector3 playerPosition, float facingYaw = 0)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (Mode == mode) return;
        Mode = mode;
        Reset(playerPosition, facingYaw); // Apply framing immediately, including while paused.
    }

    public void Reset(Vector3 playerPosition, float facingYaw = 0)
    {
        _heldDirection = Vector2.Zero;
        _anchor = playerPosition;
        _entryYaw = facingYaw;
        _distance = _config.ThirdPersonCamera.Distance;
        _yaw = _desiredYaw = _config.ThirdPersonCamera.InitialYawDegrees;
        _pitch = _desiredPitch = _config.ThirdPersonCamera.InitialPitchDegrees;
        Rebuild();
    }

    public void Update(Vector3 playerPosition, float deltaTime, CameraLookInput look = default, float facingYaw = 0, bool sprinting = false)
    {
        float dt = Math.Max(0, deltaTime);
        float follow = Mode == CameraMode.ThirdPerson ? _config.ThirdPersonCamera.FollowSmoothing : Preset.FollowSmoothing;
        _anchor = Vector3.Lerp(_anchor, playerPosition, 1 - MathF.Exp(-follow * dt));
        if (Mode == CameraMode.ThirdPerson)
        {
            var tuning = _config.ThirdPersonCamera;
            // Follow the runner through turns, taking the shortest path across +/-180.
            _entryYaw += DirectionalMovement.Delta(_entryYaw, facingYaw)
                * (1 - MathF.Exp(-6 * dt));
            float distance = sprinting ? tuning.SprintDistance : tuning.Distance;
            _distance += (distance - _distance) * (1 - MathF.Exp(-tuning.ZoomSmoothing * dt));
            float yawRate = Math.Clamp(look.Rate.X, -1, 1) * tuning.YawSpeedDegrees;
            float pitchRate = Math.Clamp(look.Rate.Y, -1, 1) * tuning.PitchSpeedDegrees;
            SmoothLook(ref _yaw, ref _desiredYaw, yawRate, dt, tuning.LookSmoothing);
            float desiredPitch = Math.Clamp(_desiredPitch + pitchRate * dt, tuning.MinPitchDegrees, tuning.MaxPitchDegrees);
            float movingTime = pitchRate == 0 ? 0 : Math.Clamp((desiredPitch - _desiredPitch) / pitchRate, 0, dt);
            SmoothLook(ref _pitch, ref _desiredPitch, pitchRate, movingTime, tuning.LookSmoothing);
            _desiredPitch = desiredPitch;
            SmoothLook(ref _pitch, ref _desiredPitch, 0, dt - movingTime, tuning.LookSmoothing);
            // Shift both together to avoid unbounded angle growth without a wrap jump.
            float turns = MathF.Floor((_desiredYaw + 180) / 360) * 360;
            _desiredYaw -= turns;
            _yaw -= turns;
        }
        Rebuild();
    }

    public PlayerInputSnapshot ResolveMovement(PlayerInputSnapshot input)
    {
        if (Mode != CameraMode.ThirdPerson) return input;
        Vector2 movement = input.Movement;
        if (movement.LengthSquared() < .0001f)
        {
            _heldDirection = Vector2.Zero;
            return input;
        }
        Vector2 direction = Vector2.Normalize(movement);
        // Keep a held command travelling straight while the camera catches up.
        // A new stick/key direction is interpreted against the current view.
        if (_heldDirection == Vector2.Zero || Vector2.Dot(direction, _heldDirection) < .999f)
        {
            _heldDirection = direction;
            _heldMovementYaw = MovementYawDegrees;
            _heldLookYaw = _yaw;
        }
        return input.RelativeToYaw(_heldMovementYaw + DirectionalMovement.Delta(_heldLookYaw, _yaw));
    }

    private static void SmoothLook(ref float current, ref float desired, float rate, float dt, float response)
    {
        // Exact exponential response to a linearly moving target angle.
        float blend = 1 - MathF.Exp(-response * dt);
        current += (desired - current) * blend + rate * (dt - blend / response);
        desired += rate * dt;
    }

    private FixedCameraPreset Preset => Mode switch
    {
        CameraMode.Close => _config.CloseCamera,
        CameraMode.Medium => _config.MediumCamera,
        CameraMode.Far => _config.FarCamera,
        _ => throw new InvalidOperationException("ThirdPerson has separate tuning.")
    };

    public void ApplyPitchSetting()
    {
        _config.ValidateCamera();
        _pitch = _desiredPitch = _config.ThirdPersonCamera.InitialPitchDegrees;
        Rebuild();
    }

    public void RefreshSettings()
    {
        _config.ValidateCamera();
        Rebuild();
    }

    private void Rebuild()
    {
        if (Mode == CameraMode.ThirdPerson)
        {
            var tuning = _config.ThirdPersonCamera;
            Vector3 direction = Direction(_entryYaw + _yaw, _pitch);
            // Move framing through a semicircle from right shoulder over the head
            // to left shoulder, preserving look direction and the movement basis.
            Vector3 right = DirectionalMovement.Right(MovementYawDegrees);
            float angle = tuning.ShoulderAngleDegrees * MathF.PI / 180;
            Vector3 shoulder = (right * MathF.Cos(angle) + Vector3.UnitY * MathF.Sin(angle)) * tuning.ShoulderOffset;
            Vector3 target = _anchor + Vector3.UnitY * tuning.TargetHeight + shoulder;
            Camera = Create(target - direction * _distance, target, tuning.FovY);
        }
        else
        {
            // One fixed-follow implementation for all three presets. Position and
            // target use the SAME smoothed anchor, so follow lag cannot turn the view.
            var preset = Preset;
            Vector3 position = _anchor + new Vector3(0, preset.Height, preset.Distance - preset.LookAhead);
            Camera = Create(position, position + Direction(0, preset.PitchDegrees) * preset.Distance, preset.FovY);
        }
    }

    private static Vector3 Direction(float yaw, float pitch)
    {
        float y = yaw * MathF.PI / 180, p = pitch * MathF.PI / 180;
        return new(MathF.Sin(y) * MathF.Cos(p), -MathF.Sin(p), -MathF.Cos(y) * MathF.Cos(p));
    }

    private static Camera3D Create(Vector3 position, Vector3 target, float fov) =>
        new() { Position = position, Target = target, Up = Vector3.UnitY, FovY = fov, Projection = CameraProjection.Perspective };
}
