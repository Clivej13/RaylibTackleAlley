using System.Numerics;
using System.Reflection;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class AutoControlTests
{
    [Fact]
    public void AutoAcceleratesForwardAtTierTwoThenHandsOffUntilRestart()
    {
        var config = new TackleAlleyConfig();
        var assets = new AssetManager(new AssetConfig());
        var field = new FootballField(config, assets);
        using var player = new BallCarrier(config);
        player.Reset(AutoTendency.Returner);
        for (int i = 0; i < 120; i++) player.Update(default(PlayerInputSnapshot), 1f / 120, field);
        Assert.Equal(PlayerControlState.Auto, player.ControlState);
        Assert.Equal(AutoTendency.Returner, player.ActiveAutoTendency);
        Assert.Equal(2, player.SpeedTier);
        Assert.Equal(player.Movement.RunningSpeed, player.CurrentForwardSpeed, 4);
        Assert.Equal(config.PlayerSpawn.X, player.Position.X);
        Assert.True(player.Position.Z < config.PlayerSpawn.Z);
        Assert.Equal(0, player.FacingYawDegrees);
        var sideways = new PlayerInputSnapshot(1, 0, 0, Vector2.Zero, false);
        player.Update(sideways, .1f, field);
        Assert.Equal(PlayerControlState.Player, player.ControlState);
        Assert.Null(player.ActiveAutoTendency);
        Assert.True(player.Position.X > config.PlayerSpawn.X);
        for (int i = 0; i < 120; i++) player.Update(default(PlayerInputSnapshot), 1f / 60, field);
        Assert.Equal(0, player.CurrentForwardSpeed);
        Assert.Equal("TackleReady", player.AnimationName);
        Assert.Equal(PlayerControlState.Player, player.ControlState);
        Assert.Null(player.ActiveAutoTendency);
        player.Reset(AutoTendency.Returner);
        Assert.Equal(PlayerControlState.Auto, player.ControlState);
        Assert.Equal(AutoTendency.Returner, player.ActiveAutoTendency);
        Assert.Equal(0, player.CurrentForwardSpeed);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void GameplayActionsAlsoTakeControl(int action)
    {
        var config = new TackleAlleyConfig();
        using var player = new BallCarrier(config);
        player.Reset(AutoTendency.Returner);
        player.Update(new PlayerInputSnapshot(0, 0, action == 1 ? 1 : 0,
            action == 3 ? Vector2.UnitX : Vector2.Zero, false, Slow: action == 2 ? 1 : 0),
            0, new FootballField(config, new AssetManager(new AssetConfig())));
        Assert.Equal(PlayerControlState.Player, player.ControlState);
        Assert.Null(player.ActiveAutoTendency);
    }

    [Fact]
    public void GameStartsAndRestartsInAuto()
    {
        var config = new TackleAlleyConfig();
        using var game = new TackleAlleyGame(config, new InputController(new InputConfig()), new AssetManager(new AssetConfig()));
        var player = (BallCarrier)typeof(TackleAlleyGame).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        Assert.Equal(PlayerControlState.Auto, player.ControlState);
        Assert.Equal(AutoTendency.Returner, player.ActiveAutoTendency);
        player.Reset();
        game.ResetRun();
        Assert.Equal(PlayerControlState.Auto, player.ControlState);
        Assert.Equal(AutoTendency.Returner, player.ActiveAutoTendency);
    }
}
