using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class ReturnerArchetypeTests : IDisposable
{
    private readonly TackleAlleyConfig _config = JsonSerializer.Deserialize<TackleAlleyConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json")))!;
    private readonly ReturnerCatalog _catalog = ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json"));
    private readonly InputController _input;
    private readonly FootballField _field;

    public ReturnerArchetypeTests()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(64, 64, "Returner archetypes");
        _input = new(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        _input.ApplyRebind(new InputRebindResult("RightStickRight", "Keyboard", "E"));
        _field = new(_config, new AssetManager(new AssetConfig()));
    }

    public void Dispose() => Raylib.CloseWindow();
    private static unsafe void Key(KeyboardKey key, bool down)
    {
        var e = new AutomationEvent { Type = down ? 2u : 1u };
        e.Params[0] = (int)key;
        Raylib.PlayAutomationEvent(e);
    }
    private void Tick(BallCarrier player, float dt)
    {
        _input.Update();
        player.Update(_input, dt, _field);
    }
    private static void Stop(BallCarrier player) =>
        typeof(BallCarrier).GetProperty(nameof(BallCarrier.CurrentForwardSpeed))!.SetValue(player, 0f);

    [Fact]
    public void ConfiguredArchetypesProduceDifferentActualMovementAndRetainItAcrossResets()
    {
        string before = JsonSerializer.Serialize(_config);
        var measures = new Dictionary<string, (float Run, float Acceleration, float Steering, float Juke)>();
        foreach (var entry in _catalog.Returners)
        {
            using var player = new BallCarrier(_config, entry.Profile);
            var movement = player.Movement;
            var physical = player.Physical;
            var profile = player.Profile;
            float z = player.Position.Z;
            Tick(player, .5f);
            float run = z - player.Position.Z;
            Assert.Equal(movement.RunningSpeed * .5f, run, 4);

            player.Reset();
            Stop(player); // Same zero-speed condition as finishing get-up recovery.
            z = player.Position.Z;
            Tick(player, .2f);
            Assert.Equal(movement.AccelerationRate * .2f, player.CurrentForwardSpeed, 4);
            Assert.Equal(.5f * movement.AccelerationRate * .2f * .2f, z - player.Position.Z, 4);
            float acceleration = player.CurrentForwardSpeed;

            player.Reset();
            Key(KeyboardKey.LeftShift, true);
            Key(KeyboardKey.D, true);
            float x = player.Position.X;
            Tick(player, .1f);
            float steering = player.Position.X - x;
            Key(KeyboardKey.D, false); Key(KeyboardKey.LeftShift, false);
            Tick(player, 0);

            player.Reset();
            x = player.Position.X;
            Key(KeyboardKey.E, true); Tick(player, 0);
            Assert.True(player.IsEvading);
            Tick(player, _config.PlayerJukeDuration);
            float juke = player.Position.X - x;
            Assert.Equal(movement.JukeSpeed * _config.PlayerJukeDuration, juke, 4);
            Assert.False(player.IsEvading);
            Key(KeyboardKey.E, false); Tick(player, 0);

            player.Reset();
            Assert.Same(profile, player.Profile);
            Assert.Same(movement, player.Movement);
            Assert.Same(physical, player.Physical);
            Assert.Same(physical, player.Ragdoll.Physical);
            Assert.Equal(movement.RunningSpeed, player.CurrentForwardSpeed);
            measures.Add(entry.Id, (run, acceleration, steering, juke));
        }
        Assert.Equal("marcus-reed", measures.MaxBy(p => p.Value.Run).Key);
        Assert.Equal("eli-brooks", measures.MaxBy(p => p.Value.Acceleration).Key);
        Assert.Equal("jalen-price", measures.MaxBy(p => p.Value.Steering).Key);
        Assert.Equal("jalen-price", measures.MaxBy(p => p.Value.Juke).Key);
        Assert.Equal("darius-stone", measures.MinBy(p => p.Value.Run).Key);
        // Run-tier differences are deliberately milder than the sprint tier now.
        Assert.True(measures["marcus-reed"].Run > measures["darius-stone"].Run * 1.15f);
        Assert.True(measures["eli-brooks"].Acceleration > measures["darius-stone"].Acceleration * 1.3f);
        Assert.True(measures["jalen-price"].Juke > measures["darius-stone"].Juke * 1.25f);
        Assert.Equal(before, JsonSerializer.Serialize(_config));
    }

    [Fact]
    public void BodyArchetypesChangeVisualProportionsMassAndContactSizeTogether()
    {
        var visual = _catalog.Returners.ToDictionary(r => r.Id, r => new PlayerVisualProfile(r.Profile));
        var physical = _catalog.Returners.ToDictionary(r => r.Id, r => new PlayerPhysicalAttributes(r.Profile, _config));
        var power = physical["darius-stone"];
        var light = physical["marcus-reed"];
        Assert.True(power.TotalMass > light.TotalMass * 1.6f);
        Assert.True(power.TorsoRadius > light.TorsoRadius);
        Assert.True(power.BoundaryRadius > light.BoundaryRadius);
        Assert.True(power.CollisionHeight > light.CollisionHeight);
        Assert.True(visual["darius-stone"].WidthRatio > visual["marcus-reed"].WidthRatio * 1.1f);
        Assert.True(visual["darius-stone"].DepthRatio > visual["marcus-reed"].DepthRatio * 1.1f);
        Assert.Equal(5, visual.Values.Select(v => (v.HeightRatio, v.WidthRatio, v.DepthRatio)).Distinct().Count());

        foreach (var entry in _catalog.Returners)
        {
            var v = visual[entry.Id];
            var p = physical[entry.Id];
            Assert.Equal(entry.Profile.Weight, p.BodyPartMasses.Sum(), 3);
            Assert.Equal(entry.Profile.Height, p.CollisionHeight);
            // Rounded capsules intentionally stay bounded. For the shipped builds,
            // their width remains within 10% of the visible horizontal scale.
            float horizontalScale = v.HeightRatio * MathF.Sqrt(v.WidthRatio * v.DepthRatio);
            Assert.InRange(p.RadiusScale / horizontalScale, .9f, 1.1f);
        }
    }

    [Theory]
    [InlineData(1, .85f)]
    [InlineData(50, 1f)]
    [InlineData(100, 1.15f)]
    public void JukeRatingIndependentlyChangesTravelNotDurationOrOtherMovement(int rating, float multiplier)
    {
        using var player = new BallCarrier(_config, new() { Juke = rating });
        using var neutral = new BallCarrier(_config, new());
        Assert.Equal(multiplier, player.Movement.JukeMultiplier, 5);
        Assert.Equal(neutral.Movement.RunningSpeed, player.Movement.RunningSpeed);
        Assert.Equal(neutral.Movement.AccelerationRate, player.Movement.AccelerationRate);
        Assert.Equal(neutral.Movement.SpinSpeed, player.Movement.SpinSpeed);
        Assert.Equal(neutral.Movement.ReversalDelay, player.Movement.ReversalDelay);
        Key(KeyboardKey.E, true); Tick(player, 0);
        float x = player.Position.X;
        Tick(player, _config.PlayerJukeDuration - .01f);
        Assert.True(player.IsEvading);
        Tick(player, .01f);
        Assert.False(player.IsEvading);
        Assert.Equal(_config.PlayerJukeSpeed * multiplier * _config.PlayerJukeDuration, player.Position.X - x, 4);
        Key(KeyboardKey.E, false);
    }
}
