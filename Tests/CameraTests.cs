using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class CameraTests
{
    [Theory]
    [InlineData(0f, 8.1f)]
    [InlineData(6.5f, 8.1f)]
    [InlineData(7.75f, 7.425f)]
    [InlineData(9f, 6.75f)]
    [InlineData(10.25f, 6.075f)]
    [InlineData(11.5f, 5.4f)]
    [InlineData(20f, 5.4f)]
    public void ActualSpeedInterpolatesBetweenStrongerDistanceAnchors(float speed, float distance)
    {
        var camera = new ThirdPersonCamera(new());
        Assert.Equal(distance, camera.DistanceForSpeed(speed), 5);
        camera.Reset(new Vector3(1, 0, -2), speed);
        Assert.Equal(distance - 2f, camera.Camera.Position.Z, 5);
        Assert.Equal(5.5f, camera.Camera.Position.Y);
        Assert.Equal(new Vector3(1, 1.1f, -7.5f), camera.Camera.Target);
        Assert.Equal(55f, camera.Camera.FovY);
    }

    [Fact]
    public void ZoomSmoothsInBothDirectionsAndResetIsImmediate()
    {
        var camera = new ThirdPersonCamera(new());
        camera.Reset(Vector3.Zero, 6.5f);
        camera.Update(Vector3.Zero, 11.5f, 0.05f);
        Assert.InRange(camera.Camera.Position.Z, 5.41f, 8.09f);
        float closer = camera.Camera.Position.Z;
        camera.Update(Vector3.Zero, 6.5f, 0.05f);
        Assert.InRange(camera.Camera.Position.Z, closer, 8.09f);
        camera.Reset(Vector3.Zero, 9f);
        Assert.Equal(new Vector3(0f, 5.5f, 6.75f), camera.Camera.Position);
    }
}
