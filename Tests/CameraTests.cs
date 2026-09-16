using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class CameraTests
{
    [Theory]
    [InlineData(0f, 8.1f)]
    [InlineData(4f, 8.1f)]
    [InlineData(5.25f, 7.425f)]
    [InlineData(6.5f, 6.75f)]
    [InlineData(7.75f, 6.075f)]
    [InlineData(9f, 5.4f)]
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

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void SteeringLooksSlightlyTowardsInputAndReturnsToCentre(float side)
    {
        var camera = new ThirdPersonCamera(new());
        camera.Reset(Vector3.Zero, 6.5f);
        camera.Update(Vector3.Zero, 6.5f, 1f, new(side, 0));
        Vector3 view = camera.Camera.Target - camera.Camera.Position;
        float degrees = MathF.Atan2(view.X, -view.Z) * 180 / MathF.PI;
        Assert.InRange(degrees * side, 11.9f, 12.1f);
        camera.Update(Vector3.Zero, 6.5f, 1f);
        Assert.InRange(Math.Abs(camera.Camera.Position.X), 0, .001f);
        Assert.True(camera.Camera.Target.Z < camera.Camera.Position.Z);
    }

    [Fact]
    public void PullBackLooksBehindWithoutCollapsingOrbitAndResetClearsIt()
    {
        var camera = new ThirdPersonCamera(new());
        camera.Reset(Vector3.Zero, 6.5f);
        for (int i = 0; i < 60; i++)
        {
            camera.Update(Vector3.Zero, 6.5f, 1f / 60, new(0, 1));
            Vector3 p = camera.Camera.Position;
            Assert.Equal(6.75f, new Vector2(p.X, p.Z).Length(), 4);
        }
        Assert.True(camera.Camera.Position.Z < -6.7f);
        Assert.True(camera.Camera.Target.Z > 5.4f);
        camera.Update(Vector3.Zero, 6.5f, 1, new(0, .45f)); // Hold through threshold noise.
        Assert.True(camera.Camera.Position.Z < -6.7f);
        camera.Update(Vector3.Zero, 6.5f, 1, new(0, .3f));
        Assert.True(camera.Camera.Position.Z > 6.7f);
        camera.Update(Vector3.Zero, 6.5f, 1, new(0, 1));
        camera.Reset(Vector3.Zero, 6.5f);
        Assert.Equal(new Vector3(0, 5.5f, 6.75f), camera.Camera.Position);
        Assert.Equal(new Vector3(0, 1.1f, -5.5f), camera.Camera.Target);
    }

    [Fact]
    public void LookSmoothingIsFrameRateIndependent()
    {
        var coarse = new ThirdPersonCamera(new());
        var fine = new ThirdPersonCamera(new());
        coarse.Reset(Vector3.Zero, 6.5f);
        fine.Reset(Vector3.Zero, 6.5f);
        coarse.Update(Vector3.Zero, 6.5f, .2f, new(0, 1));
        for (int i = 0; i < 12; i++) fine.Update(Vector3.Zero, 6.5f, 1f / 60, new(0, 1));
        Assert.True(Vector3.Distance(coarse.Camera.Position, fine.Camera.Position) < .0001f);
        Assert.True(Vector3.Distance(coarse.Camera.Target, fine.Camera.Target) < .0001f);
    }

    [Fact]
    public void ZoomSmoothsInBothDirectionsAndResetIsImmediate()
    {
        var camera = new ThirdPersonCamera(new());
        camera.Reset(Vector3.Zero, 4f);
        camera.Update(Vector3.Zero, 9f, 0.05f);
        Assert.InRange(camera.Camera.Position.Z, 5.41f, 8.09f);
        float closer = camera.Camera.Position.Z;
        camera.Update(Vector3.Zero, 4f, 0.05f);
        Assert.InRange(camera.Camera.Position.Z, closer, 8.09f);
        camera.Reset(Vector3.Zero, 6.5f);
        Assert.Equal(new Vector3(0f, 5.5f, 6.75f), camera.Camera.Position);
    }
}
