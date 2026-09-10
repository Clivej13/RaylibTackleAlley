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

    public void Reset(Vector3 playerPosition)
    {
        _position = playerPosition + new Vector3(0, _config.CameraHeight, _config.CameraFollowDistance);
        Update(playerPosition, 1f / 60f);
    }

    public void Update(Vector3 playerPosition, float deltaTime)
    {
        Vector3 desired = playerPosition + new Vector3(0, _config.CameraHeight, _config.CameraFollowDistance);
        float blend = 1f - MathF.Exp(-_config.CameraSmoothing * Math.Clamp(deltaTime, 0, 0.1f));
        _position = Vector3.Lerp(_position, desired, blend);
        Camera = new Camera3D { Position = _position, Target = playerPosition + new Vector3(0, 1.1f, -5.5f), Up = Vector3.UnitY, FovY = 55, Projection = CameraProjection.Perspective };
    }
}
