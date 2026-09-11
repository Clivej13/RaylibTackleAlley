using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

public sealed class ThirdPersonCamera
{
    private readonly TackleAlleyConfig _config;
    private Vector3 _position;
    public Camera3D Camera { get; private set; }

    public ThirdPersonCamera(TackleAlleyConfig config)
    {
        _config = config;
        Camera = new Camera3D { Up = Vector3.UnitY, FovY = 55, Projection = CameraProjection.Perspective };
    }

    public void Reset(Vector3 playerPosition, float currentForwardSpeed)
    {
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

    public void Update(Vector3 playerPosition, float currentForwardSpeed, float deltaTime)
    {
        Vector3 desired = playerPosition + new Vector3(0, _config.CameraHeight, DistanceForSpeed(currentForwardSpeed));
        float blend = 1f - MathF.Exp(-_config.CameraSmoothing * Math.Max(deltaTime, 0f));
        _position = Vector3.Lerp(_position, desired, blend);
        Camera = new Camera3D { Position = _position, Target = playerPosition + new Vector3(0, 1.1f, -5.5f), Up = Vector3.UnitY, FovY = 55, Projection = CameraProjection.Perspective };
    }
}
