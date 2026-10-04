using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class CameraTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(-90)]
    public void ShoulderArcCoversEveryFiveDegreeStepWithoutChangingViewDirection(float yaw)
    {
        var config = new TackleAlleyConfig { DefaultCameraMode = CameraMode.ThirdPerson };
        var camera = new GameplayCamera(config);
        var player = new Vector3(4, 0, -12);
        camera.Reset(player, yaw);
        var direction = Direction(camera);
        var right = DirectionalMovement.Right(yaw);
        for (int angle = 0; angle <= 180; angle += 5)
        {
            config.ThirdPersonCamera.ShoulderAngleDegrees = angle;
            camera.RefreshSettings();
            var offset = camera.Camera.Target - player - Vector3.UnitY * config.ThirdPersonCamera.TargetHeight;
            Assert.True(MathF.Abs(.65f * MathF.Cos(angle * MathF.PI / 180) - Vector3.Dot(offset, right)) < 1e-5f);
            Assert.True(MathF.Abs(.65f * MathF.Sin(angle * MathF.PI / 180) - offset.Y) < 1e-5f);
            Assert.True(Vector3.Distance(direction, Direction(camera)) < 1e-5);
            Assert.Equal(yaw, camera.MovementYawDegrees);
        }
        foreach (var mode in new[] { CameraMode.Close, CameraMode.Medium, CameraMode.Far })
        {
            camera.SetMode(mode, player, yaw);
            var before = camera.Camera;
            config.ThirdPersonCamera.ShoulderAngleDegrees = 90;
            camera.RefreshSettings();
            Assert.Equal(before.Position, camera.Camera.Position);
            Assert.Equal(before.Target, camera.Camera.Target);
            config.ThirdPersonCamera.ShoulderAngleDegrees = 180;
        }
    }

    [Theory]
    [InlineData(CameraMode.ThirdPerson)]
    [InlineData(CameraMode.Close)]
    [InlineData(CameraMode.Medium)]
    [InlineData(CameraMode.Far)]
    public void SavedTiltAppliesImmediatelyAndSurvivesReset(CameraMode mode)
    {
        var config = new TackleAlleyConfig { DefaultCameraMode = mode };
        var camera = new GameplayCamera(config);
        var original = camera.Camera;
        config.ThirdPersonCamera.InitialPitchDegrees = 35;
        camera.ApplyPitchSetting();
        if (mode == CameraMode.ThirdPerson)
            Assert.Equal(-MathF.Sin(35 * MathF.PI / 180), Direction(camera).Y, 5);
        else
        {
            Assert.Equal(original.Position, camera.Camera.Position);
            Assert.Equal(original.Target, camera.Camera.Target);
        }
        camera.SetMode(CameraMode.ThirdPerson, Vector3.Zero);
        camera.Reset(Vector3.Zero);
        Assert.Equal(-MathF.Sin(35 * MathF.PI / 180), Direction(camera).Y, 5);
    }

    private static Vector3 Direction(GameplayCamera camera) => Vector3.Normalize(camera.Camera.Target - camera.Camera.Position);

    [Theory]
    [InlineData(CameraMode.Close)]
    [InlineData(CameraMode.Medium)]
    [InlineData(CameraMode.Far)]
    public void FixedFollowTranslatesWithoutRotatingEvenDuringLagAndLookInput(CameraMode mode)
    {
        var camera = new GameplayCamera(new() { DefaultCameraMode = mode });
        camera.Reset(Vector3.Zero);
        Vector3 direction = Direction(camera);
        foreach (Vector3 position in new[] { new Vector3(4, 0, -3), new(-4, 0, -5), new(8, 2, 2), new(0, 0, -40) })
        {
            camera.Update(position, 1f / 30, new(new(1, 1)));
            Assert.True(Vector3.Distance(direction, Direction(camera)) < 1e-6);
        }
        Assert.NotEqual(Vector3.Zero, camera.Camera.Position);
    }

    [Theory]
    [InlineData(CameraMode.Close)]
    [InlineData(CameraMode.Medium)]
    [InlineData(CameraMode.Far)]
    public void FixedPresetUsesItsOwnFraming(CameraMode mode)
    {
        var config = new TackleAlleyConfig { DefaultCameraMode = mode };
        var preset = mode switch { CameraMode.Close => config.CloseCamera, CameraMode.Medium => config.MediumCamera, _ => config.FarCamera };
        preset.Distance = 12; preset.Height = 9; preset.PitchDegrees = 35; preset.LookAhead = 4; preset.FovY = 65;
        var camera = new GameplayCamera(config);
        camera.Reset(new(2, 0, -3));
        Assert.Equal(new Vector3(2, 9, 5), camera.Camera.Position);
        Assert.Equal(65, camera.Camera.FovY);
        Assert.Equal(-MathF.Sin(35 * MathF.PI / 180), Direction(camera).Y, 5);
    }

    [Fact]
    public void ThirdPersonPositionFollowDoesNotChangeItsOrbitOffset()
    {
        var config = new TackleAlleyConfig { DefaultCameraMode = CameraMode.ThirdPerson };
        var camera = new GameplayCamera(config);
        Vector3 direction = Direction(camera);
        camera.Update(new(8, 0, -20), .1f);
        Assert.True(Vector3.Distance(direction, Direction(camera)) < 1e-6);
        Assert.True(camera.Camera.Target.X > 0 && camera.Camera.Target.X < 8);
        camera.Update(new(8, 0, -20), .2f, new(new(1, -1)));
        Assert.True(Direction(camera).X > direction.X);
        Assert.True(Direction(camera).Y > direction.Y);
        Assert.Equal(config.ThirdPersonCamera.Distance, Vector3.Distance(camera.Camera.Position, camera.Camera.Target), 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(-90)]
    public void ShoulderFramingStaysBesideThePlayerAtEveryHeading(float yaw)
    {
        var config = new TackleAlleyConfig { DefaultCameraMode = CameraMode.ThirdPerson };
        var camera = new GameplayCamera(config);
        Vector3 player = new(4, 0, -12);
        camera.Reset(player, yaw);
        Vector3 right = DirectionalMovement.Right(yaw);
        Vector3 offset = camera.Camera.Position - player;
        Assert.Equal(.65f, Vector3.Dot(offset, right), 4);
        float pitch = config.ThirdPersonCamera.InitialPitchDegrees * MathF.PI / 180;
        Assert.Equal(config.ThirdPersonCamera.TargetHeight + MathF.Sin(pitch) * config.ThirdPersonCamera.Distance, camera.Camera.Position.Y, 4);
        Assert.Equal(-MathF.Cos(pitch) * config.ThirdPersonCamera.Distance, Vector3.Dot(offset, DirectionalMovement.Forward(yaw)), 4);
        Assert.True(Vector3.Dot(player - camera.Camera.Position, right) < 0);
        Vector3 originalPosition = camera.Camera.Position;
        Vector3 originalTarget = camera.Camera.Target;
        Vector3 travel = new(1, 0, -2);
        camera.Update(player + travel, .1f, facingYaw: yaw);
        Vector3 followed = camera.Camera.Position - originalPosition;
        Assert.True(Vector3.Distance(followed, travel) < .21f);
        Assert.True(Vector3.Distance(followed, camera.Camera.Target - originalTarget) < 1e-5f);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void ThirdPersonPitchIsBounded(int sign)
    {
        var config = new TackleAlleyConfig { DefaultCameraMode = CameraMode.ThirdPerson };
        var camera = new GameplayCamera(config);
        for (int i = 0; i < 600; i++) camera.Update(Vector3.Zero, 1f / 60, new(new(0, sign)));
        float pitch = -MathF.Asin(Direction(camera).Y) * 180 / MathF.PI;
        Assert.Equal(sign < 0 ? config.ThirdPersonCamera.MinPitchDegrees : config.ThirdPersonCamera.MaxPitchDegrees, pitch, 3);
    }

    [Fact]
    public void ThirdPersonLookSmoothingIsFramePartitionIndependent()
    {
        var config = new TackleAlleyConfig { DefaultCameraMode = CameraMode.ThirdPerson };
        var coarse = new GameplayCamera(config);
        var fine = new GameplayCamera(config);
        coarse.Update(Vector3.Zero, .2f, new(new(1, 1)));
        for (int i = 0; i < 12; i++) fine.Update(Vector3.Zero, 1f / 60, new(new(1, 1)));
        Assert.True(Vector3.Distance(coarse.Camera.Position, fine.Camera.Position) < .0001f);
    }

    [Fact]
    public void ModeChangesApplyImmediatelyAndDiscardOrbitForFixedModes()
    {
        var camera = new GameplayCamera(new() { DefaultCameraMode = CameraMode.ThirdPerson });
        camera.Update(Vector3.Zero, 1, new(new(1, 1)));
        camera.SetMode(CameraMode.Far, new(2, 0, -6));
        Assert.Equal(CameraMode.Far, camera.Mode);
        Assert.Equal(new Vector3(2, 8, 1), camera.Camera.Position);
        Assert.Equal(0, Direction(camera).X);
        Assert.True(Direction(camera).Z < 0);
        camera.Reset(Vector3.Zero);
        Assert.Equal(CameraMode.Far, camera.Mode);
    }

    [Fact]
    public void NestedCameraTuningRejectsNonFiniteValues()
    {
        foreach (string name in new[] { nameof(TackleAlleyConfig.ThirdPersonCamera), nameof(TackleAlleyConfig.CloseCamera),
            nameof(TackleAlleyConfig.MediumCamera), nameof(TackleAlleyConfig.FarCamera) })
        foreach (var property in typeof(TackleAlleyConfig).GetProperty(name)!.PropertyType.GetProperties())
        {
            var config = new TackleAlleyConfig();
            var tuning = typeof(TackleAlleyConfig).GetProperty(name)!.GetValue(config)!;
            property.SetValue(tuning, float.NaN);
            Assert.ThrowsAny<ArgumentException>(config.ValidateCamera);
        }
    }

    [Fact]
    public void InvalidCameraBoundsAndModesFailValidation()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { CloseCamera = null! }.ValidateCamera());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { DefaultCameraMode = (CameraMode)99 }.ValidateCamera());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { ThirdPersonCamera = new() { MinPitchDegrees = 30 } }.ValidateCamera());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { FarCamera = new() { Distance = 0 } }.ValidateCamera());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { MediumCamera = new() { FovY = 180 } }.ValidateCamera());
    }
}
