using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibTackleAlley.Game;
using Xunit;
using CameraMode = RaylibTackleAlley.Game.CameraMode;

public sealed class DirectionalMovementTests
{
    private readonly TackleAlleyConfig _config = new() { PlayerSpawn = new() { Z = -30 } };
    private FootballField Field => new(_config, new AssetManager(new AssetConfig()));
    private static PlayerInputSnapshot Input(Vector3 direction, bool sprint = false, Vector2 gesture = default, bool slow = false) =>
        new(direction.X, Math.Max(0, direction.Z), sprint ? 1 : 0, gesture, false,
            Math.Max(0, -direction.Z), slow ? 1 : 0);

    [Theory]
    [InlineData(0, false)] [InlineData(90, false)] [InlineData(180, false)] [InlineData(-90, false)]
    [InlineData(45, false)] [InlineData(135, false)] [InlineData(-45, false)] [InlineData(-135, false)]
    [InlineData(0, true)] [InlineData(90, true)] [InlineData(180, true)] [InlineData(-90, true)]
    [InlineData(45, true)] [InlineData(135, true)] [InlineData(-45, true)] [InlineData(-135, true)]
    public void EveryDirectionHasTheSameAccelerationAndSpeed(float yaw, bool sprint)
    {
        using var player = new BallCarrier(_config);
        Vector3 direction = DirectionalMovement.Forward(yaw);
        var input = Input(direction, sprint);
        player.Update(input, .5f, Field);
        float acceleration = player.Movement.TierAcceleration(sprint ? 3 : 2);
        Vector3 expected = _config.PlayerSpawn.Position + direction * (.5f * acceleration * .25f);
        Assert.InRange(Vector3.Distance(expected, player.Position), 0, 1e-5f);
        Assert.Equal(acceleration * .5f, player.CurrentForwardSpeed, 5);
        Assert.Equal(sprint ? player.Movement.SprintSpeed : player.Movement.RunningSpeed, player.TargetForwardSpeed, 4);
    }

    [Fact]
    public void SmallReversalThresholdDoesNotPenalizeHeldOrNeutralInput()
    {
        _config.PlayerReversalInputThreshold = .00001f;
        using var player = new BallCarrier(_config);
        player.Update(default(PlayerInputSnapshot), .1f, Field);
        for (int i = 0; i < 60; i++) player.Update(Input(-Vector3.UnitZ), 1f / 60, Field);
        Assert.Equal(player.Speed, player.CurrentForwardSpeed);
        Assert.Equal(0, player.SteeringRecoveryRemaining);
        Assert.True(float.IsFinite(player.FacingYawDegrees));
    }

    [Fact]
    public void NoInputStartsAtRestAndReleaseDeceleratesToRest()
    {
        using var player = new BallCarrier(_config);
        player.Update(default(PlayerInputSnapshot), 1, Field);
        Assert.Equal(_config.PlayerSpawn.Position, player.Position);
        Assert.Equal(0, player.LocomotionPlaybackRate);
        player.Update(Input(-Vector3.UnitZ), .5f, Field);
        float speed = player.CurrentForwardSpeed;
        Vector3 start = player.Position;
        player.Update(default(PlayerInputSnapshot), 1, Field);
        Assert.Equal(0, player.CurrentForwardSpeed);
        Assert.Equal(speed * speed / (2 * _config.ForwardDeceleration), start.Z - player.Position.Z, 4);
        start = player.Position;
        player.Update(Input(Vector3.Zero, true), 1, Field);
        Assert.Equal(start, player.Position); // Sprint alone never supplies a direction.
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FullReversalCanRunTowardOwnEnd(bool sprint)
    {
        using var player = new BallCarrier(_config);
        player.Update(Input(-Vector3.UnitZ, sprint), .8f, Field);
        for (int i = 0; i < 120; i++) player.Update(Input(Vector3.UnitZ, sprint), 1f / 60, Field);
        Assert.True(player.Velocity.Z > 0);
        Assert.InRange(Math.Abs(DirectionalMovement.Delta(180, player.FacingYawDegrees)), 0, .1f);
        Assert.True(player.CurrentForwardSpeed > 0);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(90, false)] [InlineData(180, false)] [InlineData(-90, false)]
    [InlineData(0, true)] [InlineData(90, true)] [InlineData(180, true)] [InlineData(-90, true)]
    public void JukesAndSpinsTravelAlongCapturedLocalRightAfterTurning(float yaw, bool spin)
    {
        using var player = new BallCarrier(_config);
        var direction = DirectionalMovement.Forward(yaw);
        var running = Input(direction);
        for (int i = 0; i < 120; i++) player.Update(running, 1f / 120, Field);
        var localRight = DirectionalMovement.Right(player.FacingYawDegrees);
        if (spin) player.Update(Input(direction, gesture: Vector2.UnitY), 0, Field);
        player.Update(Input(direction, gesture: Vector2.UnitX), 0, Field);
        float duration = spin ? _config.PlayerSpinDuration : _config.PlayerJukeDuration;
        float speed = spin ? player.Movement.SpinSpeed : player.Movement.JukeSpeed;
        Vector3 start = player.Position;
        // A new movement command during the move must not redirect committed evade travel.
        player.Update(Input(-direction, gesture: Vector2.UnitX), duration, Field);
        Assert.InRange(Vector3.Distance(start + localRight * speed * duration, player.Position), 0, 1e-4f);
        Assert.False(player.IsEvading);
    }

    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(-90)]
    public void ReleaseSprintCutsUseTheIncomingHeadingInsteadOfWorldX(float yaw)
    {
        using var player = new BallCarrier(_config);
        Vector3 forward = DirectionalMovement.Forward(yaw), right = DirectionalMovement.Right(yaw);
        player.Update(Input(forward), 1, Field);
        var left = Vector3.Normalize(forward - right);
        var destination = Vector3.Normalize(forward + right);
        player.Update(Input(left, true), .2f, Field);
        player.Update(Input(destination), .01f, Field);
        Assert.Equal("CutRight", player.AnimationName);
        player.Update(Input(destination), _config.PlayerCutDuration, Field);
        Assert.Equal("CarryRun", player.AnimationName);
        Assert.InRange(Math.Abs(DirectionalMovement.Delta(DirectionalMovement.Yaw(new(destination.X, destination.Z)),
            player.FacingYawDegrees)), 0, .001f);
        Assert.True(Vector3.Dot(player.Velocity, destination) > 0);
    }

    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(-90)]
    public void UprightContactUsesTheMovementHeading(float yaw)
    {
        using var player = new BallCarrier(_config);
        Vector3 forward = DirectionalMovement.Forward(yaw), right = DirectionalMovement.Right(yaw);
        player.Update(Input(forward), .5f, Field);
        float speed = player.CurrentForwardSpeed;
        typeof(BallCarrier).GetMethod("ApplyUprightContact", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(player, [(right * 2 - forward) * player.Physical.TotalMass]);
        Assert.Equal(speed - 1, player.CurrentForwardSpeed, 4);
        Vector3 before = player.Position;
        player.Update(Input(forward), .01f, Field);
        Assert.True(Vector3.Dot(player.Position - before, right) > 0);
        Assert.True(Vector3.Dot(player.Position - before, forward) > 0);
    }

    [Theory]
    [InlineData(CameraMode.Close)] [InlineData(CameraMode.Medium)] [InlineData(CameraMode.Far)]
    public void FixedModesIgnoreFacingOrbitAndSprint(CameraMode mode)
    {
        var camera = new GameplayCamera(_config);
        camera.SetMode(mode, Vector3.Zero);
        Vector3 direction = camera.Camera.Target - camera.Camera.Position;
        for (int yaw = -360; yaw <= 360; yaw += 30)
        {
            camera.Update(new(yaw * .01f, 0, yaw * -.01f), .1f, new(Vector2.One), yaw, true);
            Assert.InRange(Vector3.Distance(direction, camera.Camera.Target - camera.Camera.Position), 0, 1e-5f);
        }
    }

    [Fact]
    public void ThirdPersonFollowsFacingAndZoomsOnlyWhileSprinting()
    {
        var camera = new GameplayCamera(_config);
        camera.SetMode(CameraMode.ThirdPerson, Vector3.Zero, 180);
        Assert.True(camera.Camera.Position.Z < camera.Camera.Target.Z);
        camera.Update(new(2, 0, -5), 2, default, 90, true);
        Vector3 direction = Vector3.Normalize(camera.Camera.Target - camera.Camera.Position);
        Assert.True(direction.X > .9f); // The camera has swung behind the new heading.
        Assert.Equal(_config.ThirdPersonCamera.SprintDistance,
            Vector3.Distance(camera.Camera.Position, camera.Camera.Target), 4);
        camera.Update(new(2, 0, -5), 1, new(new(1, -.2f)), 90, false);
        Assert.True(Vector3.Distance(direction, Vector3.Normalize(camera.Camera.Target - camera.Camera.Position)) > .5f);
        camera.Update(new(2, 0, -5), 2, default, 90, false);
        Assert.Equal(_config.ThirdPersonCamera.Distance, Vector3.Distance(camera.Camera.Position, camera.Camera.Target), 4);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(0)] [InlineData(8)]
    public void InvalidSprintDistanceFailsAtStartup(float distance)
    {
        var config = new TackleAlleyConfig();
        config.ThirdPersonCamera.SprintDistance = distance;
        Assert.ThrowsAny<ArgumentException>(() => new GameplayCamera(config));
    }
}
