using System.Numerics;
using System.Text.Json;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class GameplayTuningTests
{
    [Fact]
    public void DefaultsAndShippedConfigurationValidateAndRoundTrip()
    {
        new TackleAlleyConfig().Validate();
        JsonSerializer.Deserialize<TackleAlleyConfig>("{}")!.Validate();
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json"));
        var shipped = JsonSerializer.Deserialize<TackleAlleyConfig>(json)!;
        shipped.Validate();
        // Preserve the shipped balance, which intentionally differs from class speed defaults.
        Assert.Equal(9, shipped.PlayerForwardSpeed);
        Assert.Equal(6.5f, shipped.PlayerSlowSpeed);
        Assert.Equal(11.5f, shipped.PlayerSprintSpeed);
        using var document = JsonDocument.Parse(json);
        foreach (var property in typeof(TackleAlleyConfig).GetProperties())
            Assert.True(document.RootElement.TryGetProperty(property.Name, out _), property.Name);
        var custom = new TackleAlleyConfig
        {
            PlayerSpinSpeed = 13, ContactMaximumTackleImpulse = 450,
            PlayerSpawn = new() { X = 2, Z = -3 },
            OpponentSpawns = [new() { X = -2, Z = -12 }],
            RagdollHipLimits = new() { MinimumDegrees = [-60, -20, -30], MaximumDegrees = [20, 20, 30] }
        };
        var loaded = JsonSerializer.Deserialize<TackleAlleyConfig>(JsonSerializer.Serialize(custom))!;
        loaded.Validate();
        Assert.Equal(13, loaded.PlayerSpinSpeed);
        Assert.Equal(450, loaded.ContactMaximumTackleImpulse);
        Assert.Equal(new Vector3(2, 0, -3), loaded.PlayerSpawn.Position);
        Assert.Single(loaded.OpponentSpawns);
        Assert.Equal(custom.RagdollHipLimits.MinimumDegrees, loaded.RagdollHipLimits.MinimumDegrees);
        Assert.Equal(35, new TackleAlleyConfig().RagdollHipLimits.MaximumDegrees[0]);
    }

    [Fact]
    public void ShippedConfigurationKeepsLungeLowAndDrivingForward()
    {
        var config = JsonSerializer.Deserialize<TackleAlleyConfig>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json")))!;
        var defender = new Opponent(Vector3.Zero, config);
        Vector3 target = new(0, 0, 1.8f);
        defender.Update(target, 0, true, Vector2.Zero, false);
        defender.Update(target, 0);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
        float speed = defender.CurrentSpeed;
        for (int frame = 0; frame < 36; frame++)
        {
            defender.Update(target, 1f / 60f);
            Assert.InRange(defender.Position.Y, 0f, .051f);
        }
        Assert.True(defender.IsGrounded);
        Assert.Equal(speed * .6f, defender.Position.Z, 4);
        Assert.Equal(speed, defender.CurrentSpeed);
    }

    [Fact]
    public void EveryScalarTuningRejectsNonFiniteValues()
    {
        foreach (var property in typeof(TackleAlleyConfig).GetProperties().Where(p => p.PropertyType == typeof(float)))
        {
            var config = new TackleAlleyConfig();
            property.SetValue(config, float.NaN);
            Assert.True(Record.Exception(config.Validate) is ArgumentException, property.Name);
        }
    }

    [Theory]
    [InlineData(nameof(TackleAlleyConfig.PlayerReversalInputThreshold), 0)]
    [InlineData(nameof(TackleAlleyConfig.PlayerReversalSpeedLoss), 1.1f)]
    [InlineData(nameof(TackleAlleyConfig.PlayerReversalWindow), 0)]
    [InlineData(nameof(TackleAlleyConfig.PlayerReversalAccelerationDelay), -1)]
    [InlineData(nameof(TackleAlleyConfig.CameraLookBackHalfAngleDegrees), 90)]
    [InlineData(nameof(TackleAlleyConfig.PlayerEvadeMaximumDistanceScale), -1)]
    [InlineData(nameof(TackleAlleyConfig.PlayerSpinDuration), 0)]
    [InlineData(nameof(TackleAlleyConfig.RagdollChestMass), 0)]
    [InlineData(nameof(TackleAlleyConfig.RagdollChestRadius), -1)]
    [InlineData(nameof(TackleAlleyConfig.ContactRestitution), 1.1f)]
    [InlineData(nameof(TackleAlleyConfig.ContactFriction), -1)]
    [InlineData(nameof(TackleAlleyConfig.RagdollStruggleBlendIn), 0)]
    [InlineData(nameof(TackleAlleyConfig.OpponentFallGravity), 0)]
    [InlineData(nameof(TackleAlleyConfig.LungeFullPredictionDistance), 0)]
    [InlineData(nameof(TackleAlleyConfig.CameraFovY), 180)]
    [InlineData(nameof(TackleAlleyConfig.FullSteeringForwardRetention), 1.1f)]
    public void UnsafeScalarValuesAreRejected(string property, float value)
    {
        var config = new TackleAlleyConfig();
        typeof(TackleAlleyConfig).GetProperty(property)!.SetValue(config, value);
        Assert.ThrowsAny<ArgumentException>(config.Validate);
    }

    [Fact]
    public void InvalidRelationshipsAndNestedValuesAreRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { PlayerReversalMaximumDelay = .1f }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { OpponentReadyExitDistance = 2 }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { OpponentMaximumLungeReachDistance = 1 }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { CameraLookBackReleaseThreshold = .8f }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { EvadeReleaseThreshold = .9f }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { ContactHitLimbPelvisShare = .5f }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { PlayerForwardSpeed = 4 }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { OpponentSpawns = null! }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig { PlayerSpawn = new() { X = float.PositiveInfinity } }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig {
            RagdollKneeLimits = new() { MinimumDegrees = [0, 0], MaximumDegrees = [1, 1, 1] }
        }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyConfig {
            RagdollKneeLimits = new() { MinimumDegrees = [10, 0, 0], MaximumDegrees = [1, 1, 1] }
        }.Validate());
    }

    [Fact]
    public void CustomPredictionChangesPursuitAndLaunchAim()
    {
        var config = new TackleAlleyConfig {
            PursuitRunPredictionDistance = 5, PursuitDirectionResponse = 0,
            LungeMaximumPredictionSeconds = .1f
        };
        var prediction = new CarrierPursuitPrediction(config);
        prediction.Reset(Vector3.Zero);
        Assert.Equal(new Vector3(0, 0, -6), prediction.Observe(new(0, 0, -1), 2, .1f));
        Assert.Equal(new Vector3(1, 0, -1), prediction.Observe(new(1, 0, -1), 2, .1f));
        Vector3 target = LungeInterception.Target(new(3, 0, 0), Vector3.Zero, new(0, 0, -6), 9, config);
        Assert.Equal(-.6f, target.Z, 4);
        config.LungeMaximumPredictionSeconds = 0;
        Assert.Equal(Vector3.Zero, LungeInterception.Target(new(3, 0, 0), Vector3.Zero, new(0, 0, -6), 9, config));
    }

    [Fact]
    public void CustomTackleReachLaunchSpeedAndGravityAffectTheDive()
    {
        var config = new TackleAlleyConfig {
            OpponentInitialYawDegrees = 0, OpponentLungeReachDistance = 5,
            OpponentMaximumLungeReachDistance = 5, OpponentLungeLaunchVerticalSpeed = 7,
            OpponentFallGravity = 4, OpponentJogSpeed = 8
        };
        var defender = new Opponent(Vector3.Zero, config);
        defender.Update(new(0, 0, -10), 0, true, Vector2.Zero, false);
        defender.Update(new(0, 0, -4.8f), 0);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
        Assert.Equal(7, defender.VerticalVelocity);
        defender.Update(new(0, 0, -4.8f), .1f);
        Assert.Equal(6.6f, defender.VerticalVelocity, 4);
        Assert.Equal(.68f, defender.Position.Y, 4);
    }

    [Fact]
    public void CustomCameraFramingAndLookThresholdsAreUsed()
    {
        var config = new TackleAlleyConfig {
            CameraFovY = 70, CameraTargetHeight = 2, CameraLookAheadDistance = 3,
            CameraSteeringYawDegrees = 20, CameraLookBackThreshold = .8f,
            CameraLookBackReleaseThreshold = .7f
        };
        var camera = new ThirdPersonCamera(config);
        camera.Reset(Vector3.Zero, 6.5f);
        Assert.Equal(70, camera.Camera.FovY);
        Assert.Equal(new Vector3(0, 2, -3), camera.Camera.Target);
        camera.Update(Vector3.Zero, 6.5f, 1, new(1, .6f));
        Vector3 view = camera.Camera.Target - camera.Camera.Position;
        Assert.InRange(MathF.Atan2(view.X, -view.Z) * 180 / MathF.PI, 19.9f, 20.1f);
        camera.Update(Vector3.Zero, 6.5f, 1, new(0, .9f));
        Assert.True(camera.Camera.Target.Z > camera.Camera.Position.Z);
    }

    [Fact]
    public void CustomBodyMassRadiiAndJointLimitsReachTheSolver()
    {
        var config = new TackleAlleyConfig {
            RagdollChestMass = 50, RagdollUpperArmRadius = .08f,
            ContactTorsoRadius = .3f, RagdollMaximumAngularSpeed = 2,
            RagdollTorsoLimits = new() { MinimumDegrees = [-10, -20, -15], MaximumDegrees = [10, 20, 15] }
        };
        var doll = RagdollContactTests.Create(new(0, 10, 0), Vector3.Zero, config);
        Assert.Equal(50, doll.Bodies[1].Mass);
        Assert.Equal(.08f, doll.Bodies[3].Radius);
        Assert.Equal(.3f, RagdollContact.Radius(doll.Bodies[1]));
        Assert.Equal(10 * MathF.PI / 180, doll.Joints[0].MaximumAngles.X, 5);
        doll.ApplyImpulse(new(1, new(50, 0, 0), doll.Bodies[1].Position + Vector3.UnitY));
        Assert.Equal(1, doll.Bodies[1].LinearVelocity.X);
        Assert.InRange(doll.Bodies[1].AngularVelocity.Length(), 1.99f, 2.001f);
    }

    [Fact]
    public void GravityDampingAndActiveDriveUsePerInstanceConfiguration()
    {
        var config = new TackleAlleyConfig {
            RagdollGravity = 0, RagdollLinearDamping = 0, RagdollStruggleDuration = .05f,
            RagdollHitLimbMotorStrength = .3f, RagdollContactYieldSpeed = 2
        };
        var tuned = RagdollContactTests.Create(new(0, 10, 0), Vector3.UnitX, config);
        var normal = RagdollContactTests.Create(new(0, 10, 0), Vector3.UnitX);
        tuned.Update(Ragdoll.FixedStep);
        normal.Update(Ragdoll.FixedStep);
        Assert.Equal(0, tuned.Bodies[0].LinearVelocity.Y, 3);
        Assert.True(normal.Bodies[0].LinearVelocity.Y < -.01f);
        Assert.Equal(1, tuned.Bodies[0].LinearVelocity.X, 3);
        var rotations = tuned.Joints.Select(j => j.ReferenceRotation).ToArray();
        tuned.StartActiveDrive(_ => rotations);
        tuned.ReactToContact(3, 1);
        Assert.Equal(1, tuned.MotorStrength(3));
        tuned.ReactToContact(3, 3);
        Assert.Equal(.3f, tuned.MotorStrength(3));
        for (int i = 0; i < 8; i++) tuned.Update(Ragdoll.FixedStep);
        Assert.False(tuned.IsActivelyDriven);
    }

    [Fact]
    public void ConfiguredContactRadiiAndImpulseClampsChangeCollision()
    {
        var config = new TackleAlleyConfig {
            ContactTorsoRadius = .34f, ContactMaximumTackleImpulse = 20,
            ContactTackleAngularEnergyShare = 0
        };
        var a = RagdollContactTests.Create(new(0, 3, -.65f), new(0, 0, 9), config);
        var b = RagdollContactTests.Create(new(0, 3, 0), Vector3.Zero, config);
        Assert.True(RagdollContact.Overlaps(a, b));
        var normalA = RagdollContactTests.Create(new(0, 3, -.65f), Vector3.Zero);
        var normalB = RagdollContactTests.Create(new(0, 3, 0), Vector3.Zero);
        Assert.False(RagdollContact.Overlaps(normalA, normalB));
        Vector3 initial = Momentum(a);
        RagdollContact.Resolve(a, b, true);
        Assert.Equal(20, Momentum(b).Z, 3);
        Assert.True(Vector3.Distance(initial - Momentum(a), Momentum(b)) < .001f);
        Assert.All(b.Bodies, body => Assert.Equal(Vector3.Zero, body.AngularVelocity));
    }

    [Fact]
    public void ContactRestitutionFrictionAndImpulseScaleAffectResponse()
    {
        float Response(float restitution, float scale)
        {
            var config = new TackleAlleyConfig {
                ContactRestitution = restitution, ContactImpulseScale = scale, ContactFriction = 0
            };
            var a = RagdollContactTests.Create(new(0, 3, -.46f), new(0, 0, 4), config);
            var b = RagdollContactTests.Create(new(0, 3, 0), Vector3.Zero, config);
            RagdollContact.Resolve(a, b);
            return Momentum(b).Z;
        }
        Assert.Equal(0, Response(0, 0));
        Assert.True(Response(.5f, 1) > Response(0, 1));
        Assert.True(Response(0, 1) > Response(0, .2f));

        float SidewaysImpulse(float friction)
        {
            var config = new TackleAlleyConfig { ContactFriction = friction };
            var a = RagdollContactTests.Create(new(0, 3, -.46f), new(3, 0, 4), config);
            var b = RagdollContactTests.Create(new(0, 3, 0), Vector3.Zero, config);
            RagdollContact.Resolve(a, b);
            return Momentum(b).X;
        }
        Assert.True(SidewaysImpulse(.8f) > SidewaysImpulse(0) + .01f);
    }

    private static Vector3 Momentum(Ragdoll doll) =>
        doll.Bodies.Aggregate(Vector3.Zero, (sum, body) => sum + body.LinearVelocity * body.Mass);
}
