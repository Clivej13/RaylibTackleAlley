using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class MovementAttributesTests : IDisposable
{
    private readonly TackleAlleyConfig _config = new();
    private readonly InputController _input;
    private readonly FootballField _field;
    public MovementAttributesTests()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(64, 64, "Movement attributes");
        _input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        _input.ApplyRebind(new InputRebindResult("RightStickLeft", "Keyboard", "R"));
        _input.ApplyRebind(new InputRebindResult("RightStickRight", "Keyboard", "E"));
        _input.ApplyRebind(new InputRebindResult("RightStickBack", "Keyboard", "Q"));
        _field = new FootballField(_config, new AssetManager(new AssetConfig()));
    }
    public void Dispose() => Raylib.CloseWindow();
    private static unsafe void Key(KeyboardKey key, bool down)
    {
        var e = new AutomationEvent { Type = down ? 2u : 1u };
        e.Params[0] = (int)key; Raylib.PlayAutomationEvent(e);
    }
    private BallCarrier Carrier(PlayerProfile profile) => new(new TackleAlleyConfig { BallCarrierProfile = profile });
    private void Tick(BallCarrier player, float dt) { _input.Update(); player.Update(_input, dt, _field); }
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    private static void Speed(BallCarrier p, float speed) => typeof(BallCarrier).GetProperty(nameof(BallCarrier.CurrentForwardSpeed))!.SetValue(p, speed);
    private static void Speed(Opponent p, float speed) => typeof(Opponent).GetProperty(nameof(Opponent.CurrentSpeed))!.SetValue(p, speed);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiftyPreservesAllBaselineValues(bool defender)
    {
        var a = new PlayerMovementAttributes(new(), _config, defender);
        Assert.Equal(defender ? _config.OpponentJogSpeed : _config.PlayerSlowSpeed, a.JogSpeed);
        Assert.Equal(defender ? _config.OpponentRunSpeed : _config.PlayerForwardSpeed, a.RunningSpeed);
        Assert.Equal(defender ? _config.OpponentSprintSpeed : _config.PlayerSprintSpeed, a.SprintSpeed);
        Assert.Equal(_config.PlayerSlowSpeed, a.ReadySpeed);
        Assert.Equal(_config.ForwardAcceleration, a.AccelerationRate);
        Assert.Equal(_config.PlayerLateralSpeed, a.LateralSpeed);
        Assert.Equal(_config.PlayerSprintSteeringRate, a.SteeringResponse);
        Assert.Equal(_config.PlayerRunYawResponse, a.FacingResponse);
        Assert.Equal(_config.PlayerReversalSpeedLoss, a.ReversalPenalty);
        Assert.Equal(_config.PlayerReversalAccelerationDelay, a.ReversalDelay);
        Assert.Equal(_config.PlayerReversalAdditionalDelay, a.ReversalAdditionalDelay);
        Assert.Equal(_config.PlayerReversalMaximumDelay, a.ReversalMaximumDelay);
        Assert.Equal(_config.PlayerJukeSpeed, a.JukeSpeed);
        Assert.Equal(_config.PlayerSpinSpeed, a.SpinSpeed);
        Assert.Equal(1, a.DirectionBlend(1f / 60));
    }

    [Theory]
    [InlineData(1, .8f, .75f, .85f, 1.15f, .9f)]
    [InlineData(50, 1f, 1f, 1f, 1f, 1f)]
    [InlineData(100, 1.2f, 1.25f, 1.15f, .85f, 1.1f)]
    public void MappingsHaveExactAnchors(int rating, float speed, float acceleration, float agility, float reversal, float evade)
    {
        var a = new PlayerMovementAttributes(new() { Speed = rating, Acceleration = rating, Agility = rating }, _config);
        var defender = new PlayerMovementAttributes(new() { Speed = rating }, _config, defender: true);
        Assert.Equal(speed, defender.JogSpeedMultiplier, 5);
        Assert.Equal(speed, defender.RunningSpeedMultiplier, 5);
        Assert.Equal(speed, defender.SprintSpeedMultiplier, 5);
        Assert.Equal(acceleration, a.AccelerationMultiplier, 5);
        Assert.Equal(agility, a.SteeringMultiplier, 5);
        Assert.Equal(reversal, a.ReversalMultiplier, 5);
        Assert.Equal(evade, a.EvadeMultiplier, 5);
        for (int r = 1; r <= 100; r++)
            Assert.InRange(PlayerMovementAttributes.RatingMultiplier(r, .8f, 1.2f), .8f, 1.2f);
    }

    [Fact]
    public void SpeedChangesTargetsWithoutChangingAcceleration()
    {
        using var normal = Carrier(new());
        using var fast = Carrier(new() { Speed = 100 });
        Assert.Equal(normal.Movement.AccelerationRate, fast.Movement.AccelerationRate);
        Assert.Equal(normal.Speed * (1 + _config.ReturnerRunSpeedInfluence), fast.Speed);
        Speed(normal, 0); Speed(fast, 0);
        Tick(normal, .1f); Tick(fast, .1f);
        Assert.Equal(normal.CurrentForwardSpeed, fast.CurrentForwardSpeed);
        Tick(normal, 2); Tick(fast, 2);
        Assert.Equal(normal.TargetForwardSpeed, normal.CurrentForwardSpeed);
        Assert.Equal(fast.TargetForwardSpeed, fast.CurrentForwardSpeed, 5);
        Assert.True(fast.CurrentForwardSpeed > normal.CurrentForwardSpeed);
    }

    [Fact]
    public void AccelerationReachesTheSameTargetSooner()
    {
        using var slow = Carrier(new() { Acceleration = 1 });
        using var fast = Carrier(new() { Acceleration = 100 });
        Speed(slow, 0); Speed(fast, 0);
        Tick(slow, .7f); Tick(fast, .7f);
        Assert.Equal(slow.TargetForwardSpeed, fast.TargetForwardSpeed);
        Assert.Equal(fast.TargetForwardSpeed, fast.CurrentForwardSpeed, 5);
        Assert.True(slow.CurrentForwardSpeed < fast.CurrentForwardSpeed);
    }

    [Fact]
    public void AgilityImprovesLateralSprintSteeringAndFacing()
    {
        using var low = Carrier(new() { Agility = 1 });
        using var high = Carrier(new() { Agility = 100 });
        Key(KeyboardKey.D, true);
        Tick(low, .05f); Tick(high, .05f);
        Assert.True(high.Position.X > low.Position.X);
        Assert.True(Math.Abs(Field<float>(high, "_currentRunYaw")) > Math.Abs(Field<float>(low, "_currentRunYaw")));
        low.Reset(); high.Reset();
        Key(KeyboardKey.LeftShift, true);
        Tick(low, .05f); Tick(high, .05f);
        Assert.True(Field<float>(high, "_effectiveLateral") > Field<float>(low, "_effectiveLateral"));
        Key(KeyboardKey.LeftShift, false); Key(KeyboardKey.D, false);
    }

    [Fact]
    public void ReversalPenaltyAndDelayAreLowerButStillBlockAcceleration()
    {
        using var low = Carrier(new() { Agility = 1 });
        using var high = Carrier(new() { Agility = 100 });
        Key(KeyboardKey.A, true); Tick(low, .01f); Tick(high, .01f);
        Key(KeyboardKey.A, false); Key(KeyboardKey.D, true);
        Tick(low, 0); Tick(high, 0);
        Assert.True(high.CurrentForwardSpeed > low.CurrentForwardSpeed);
        Assert.Equal(_config.PlayerReversalAccelerationDelay * .85f, high.SteeringRecoveryRemaining, 5);
        Assert.Equal(_config.PlayerReversalAccelerationDelay * 1.15f, low.SteeringRecoveryRemaining, 5);
        float before = high.CurrentForwardSpeed;
        Tick(high, .1f);
        Assert.Equal(before, high.CurrentForwardSpeed);
        Assert.InRange(high.Movement.ReversalPenalty, 0, 1);
        Key(KeyboardKey.D, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvadesUseAgilityWithoutChangingActionDuration(bool spin)
    {
        float Travel(int agility)
        {
            using var p = Carrier(new() { Agility = agility });
            Key(KeyboardKey.E, false); Key(KeyboardKey.Q, false); Tick(p, 0);
            if (spin) { Key(KeyboardKey.Q, true); Tick(p, 0); Key(KeyboardKey.Q, false); }
            Key(KeyboardKey.E, true); Tick(p, 0);
            float duration = spin ? _config.PlayerSpinDuration : _config.PlayerJukeDuration;
            Assert.Equal(duration, Field<float>(p, spin ? "_spinRemaining" : "_jukeRemaining"));
            Tick(p, duration);
            Key(KeyboardKey.E, false);
            return p.Position.X;
        }
        float low = Travel(1), normal = Travel(50), high = Travel(100);
        Assert.Equal(normal * .9f, low, 4);
        Assert.Equal(normal * 1.1f, high, 4);
    }

    [Fact]
    public void EachDefenderUsesOwnPursuitReadyAndLaunchSpeeds()
    {
        var config = new TackleAlleyConfig { OpponentInitialYawDegrees = 0 };
        using var low = new Opponent(Vector3.Zero, config, new() { Speed = 1, Acceleration = 1, Agility = 1 });
        using var high = new Opponent(Vector3.Zero, config, new() { Speed = 100, Acceleration = 100, Agility = 100 });
        Assert.True(high.CurrentSpeed > low.CurrentSpeed);
        Assert.True(high.TackleReadySpeed > low.TackleReadySpeed);
        low.Update(new(10, 0, -20), .1f, false, Vector2.Zero, false);
        high.Update(new(10, 0, -20), .1f, false, Vector2.Zero, false);
        Assert.True(high.CurrentSpeed > low.CurrentSpeed);
        Assert.True(high.Movement.ReadyFacingResponse > low.Movement.ReadyFacingResponse);
        Assert.True(high.Movement.TrackingResponse > low.Movement.TrackingResponse);
        foreach (var d in new[] { low, high })
        {
            d.Reset();
            d.Update(new(0, 0, -10), 0, true, Vector2.Zero, false);
            Speed(d, 0);
            d.Update(new(0, 0, -1.8f), 0);
            Assert.Equal(DefenderState.LungeTackle, d.State);
            Assert.Equal(d.Movement.JogSpeed, d.CurrentSpeed);
            var attributes = d.Movement;
            d.Reset();
            Assert.Same(attributes, d.Movement);
        }
    }

    [Fact]
    public void PredictionUsesActualVelocityAndIndividualMaximumTierSpeed()
    {
        var prediction = new CarrierPursuitPrediction();
        Vector3 position = new(0, 0, -.5f); // observed 5 m/s
        prediction.Reset(Vector3.Zero);
        var slow = prediction.Observe(position, 2, .1f, 5f);
        prediction.Reset(Vector3.Zero);
        var fast = prediction.Observe(position, 2, .1f, 10f);
        Assert.Equal(2 * Vector3.Distance(position, fast), Vector3.Distance(position, slow), 5);
        prediction.Reset(position);
        Assert.Equal(position, prediction.Observe(position, 3, .1f, 12f));
        var a = LungeInterception.Target(new(3, 0, 0), Vector3.Zero, new(0, 0, -3), 4);
        var b = LungeInterception.Target(new(3, 0, 0), Vector3.Zero, new(0, 0, -3), 12);
        Assert.True(Math.Abs(a.Z) > Math.Abs(b.Z));
    }

    [Fact]
    public void ResetRetainsCachedAttributesAndProfile()
    {
        var config = new TackleAlleyConfig { BallCarrierProfile = new() { Speed = 80, Acceleration = 25, Agility = 95 } };
        using var p = new BallCarrier(config);
        var attributes = p.Movement;
        config.ForwardAcceleration *= 2;
        config.BallCarrierProfile = new();
        p.Reset();
        Assert.Same(attributes, p.Movement);
        Assert.Equal(80, p.Profile.Speed);
        Assert.Equal(attributes.RunningSpeed, p.CurrentForwardSpeed);
    }

    [Theory]
    [InlineData(-10, .35f)]
    [InlineData(0, .35f)]
    [InlineData(1, .35f)]
    [InlineData(10, 1f)]
    [InlineData(30, 1.5f)]
    public void PlaybackIsBounded(float speed, float expected)
    {
        if (speed < 0) speed = 0;
        Assert.Equal(expected, PlayerMovementAttributes.PlaybackRate(speed, 10), 5);
        Assert.Equal(.35f, PlayerMovementAttributes.PlaybackRate(speed, 0));
    }

    [Theory]
    [InlineData("Speed", 0)]
    [InlineData("Speed", 101)]
    [InlineData("Acceleration", 0)]
    [InlineData("Acceleration", 101)]
    [InlineData("Agility", 0)]
    [InlineData("Agility", 101)]
    public void RatingsOutsideRangeAreRejected(string field, int value)
    {
        var profile = new PlayerProfile();
        typeof(PlayerProfile).GetProperty(field)!.SetValue(profile, value);
        Assert.Contains(field, Assert.Throws<ArgumentException>(() => profile.Validate()).Message);
    }

    [Theory]
    [InlineData("Speed")]
    [InlineData("Acceleration")]
    [InlineData("Agility")]
    public void JsonRequiresIntegersAndMissingRatingsDefaultToFifty(string field)
    {
        var profile = JsonSerializer.Deserialize<PlayerProfile>("{}")!;
        Assert.Equal(50, profile.Speed);
        Assert.Equal(50, profile.Acceleration);
        Assert.Equal(50, profile.Agility);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PlayerProfile>("{\"" + field + "\":50.5}"));
    }
}
