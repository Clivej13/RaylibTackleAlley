using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

public sealed class ThirdPersonCamera
{
    private readonly TackleAlleyConfig _config;
    private Vector3 _position;
    private float _lookYaw;
    private bool _lookingBack;
    public Camera3D Camera { get; private set; }

    public ThirdPersonCamera(TackleAlleyConfig config)
    {
        config.ValidateCamera();
        _config = config;
        Camera = new Camera3D { Up = Vector3.UnitY, FovY = _config.CameraFovY, Projection = CameraProjection.Perspective };
    }

    public void Reset(Vector3 playerPosition, float currentForwardSpeed)
    {
        _lookYaw = 0;
        _lookingBack = false;
        _position = playerPosition + new Vector3(0, _config.CameraHeight, DistanceForSpeed(currentForwardSpeed));
        Update(playerPosition, currentForwardSpeed, 0f);
    }

    public float DistanceForSpeed(float currentForwardSpeed)
    {
        bool lowerBand = currentForwardSpeed <= _config.PlayerForwardSpeed;
        float lowSpeed = lowerBand ? _config.PlayerSlowSpeed : _config.PlayerForwardSpeed;
        float highSpeed = lowerBand ? _config.PlayerForwardSpeed : _config.PlayerSprintSpeed;
        float lowDistance = lowerBand ? _config.CameraSpeed1Distance : _config.CameraSpeed2Distance;
        float highDistance = lowerBand ? _config.CameraSpeed2Distance : _config.CameraSpeed3Distance;
        float amount = Math.Clamp((currentForwardSpeed - lowSpeed) / (highSpeed - lowSpeed), 0f, 1f);
        return lowDistance + (highDistance - lowDistance) * amount;
    }

    public void Update(Vector3 playerPosition, float currentForwardSpeed, float deltaTime,
        Vector2 movementInput = default)
    {
        float backward = Math.Clamp(movementInput.Y, 0, 1);
        // Forward is 0 degrees, straight back is 180: require the narrow rear sector.
        float rearAngle = MathF.Atan2(Math.Abs(movementInput.X), backward) * 180f / MathF.PI;
        _lookingBack = rearAngle <= _config.CameraLookBackHalfAngleDegrees + .0001f &&
            backward > (_lookingBack ? _config.CameraLookBackReleaseThreshold : _config.CameraLookBackThreshold);
        float desiredYaw = _lookingBack ? MathF.PI
            : -Math.Clamp(movementInput.X, -1, 1) * _config.CameraSteeringYawDegrees * MathF.PI / 180f;
        float lookBlend = 1f - MathF.Exp(-_config.CameraLookSmoothing * Math.Max(deltaTime, 0f));
        _lookYaw += (desiredYaw - _lookYaw) * lookBlend;
        Vector3 desired = playerPosition + new Vector3(0, _config.CameraHeight, DistanceForSpeed(currentForwardSpeed));
        float blend = 1f - MathF.Exp(-_config.CameraSmoothing * Math.Max(deltaTime, 0f));
        _position = Vector3.Lerp(_position, desired, blend);
        // Orbit the smoothed follow position at full radius, never interpolate through the runner.
        Matrix4x4 rotation = Matrix4x4.CreateRotationY(_lookYaw);
        Vector3 position = playerPosition + Vector3.Transform(_position - playerPosition, rotation);
        Vector3 target = playerPosition + Vector3.Transform(new Vector3(0, _config.CameraTargetHeight, -_config.CameraLookAheadDistance), rotation);
        Camera = new Camera3D { Position = position, Target = target, Up = Vector3.UnitY, FovY = _config.CameraFovY, Projection = CameraProjection.Perspective };
    }
}
