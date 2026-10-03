using System.Numerics;
using System.Text.Json;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class TackleAlleyCommitmentTests
{
    [Fact]
    public void CurrentLevelDoesNotUseReservedLinebackerBehavior()
    {
        var profiles = DefenderProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
        var level = LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), new(), profiles).Resolve("level-1");
        foreach (var defender in level.Defenders)
        {
            Assert.Equal("balanced", defender.BehaviorProfile.Id);
            Assert.Null(defender.BehaviorProfile.ApproachTuning);
        }
        Assert.NotNull(profiles.Resolve("offball-linebacker").ApproachTuning);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void Number52CommitsInsteadOfWaitingForWrapAndLateDodgeEscapesHisPath(int hz)
    {
        var config = JsonSerializer.Deserialize<TackleAlleyConfig>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json")))!;
        var profiles = DefenderProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
        var level = LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), config, profiles).Resolve("level-1");
        var body = level.Defenders.Single(d => d.Profile.JerseyNumber == 52).Profile;
        using var alley = new Opponent(Vector3.Zero, config, body, profiles.Resolve("balanced"));
        using var linebacker = new Opponent(Vector3.Zero, config, body, profiles.Resolve("offball-linebacker"));
        Vector3 carrier = new(0, 0, 2);
        foreach (var defender in new[] { alley, linebacker })
        {
            defender.Update(carrier, 0, true, Vector2.Zero, false);
            defender.Update(carrier, 0, carrierVelocity: Vector3.Zero, carrierPredictedDirection: -Vector3.UnitZ);
        }
        Assert.Equal(DefenderState.LungeTackle, alley.State);
        Assert.Equal(DefenderState.TackleReady, linebacker.State);
        Assert.True(alley.CurrentContactSolution.Reachable);
        Assert.True(Vector3.Distance(alley.Position, carrier) > alley.Physical.WrapReach);
        float dt = 1f / hz;
        for (int i = 0; i < hz / 5; i++)
            alley.Update(carrier, dt, carrierVelocity: Vector3.Zero);
        Assert.True(alley.DirectionLocked);
        Vector3 locked = alley.CorrectedDirection;
        Vector3 origin = alley.Position;
        // A late lateral dodge crosses out of the committed path; no retargeting.
        for (int i = 0; i < hz / 5; i++)
        {
            carrier += Vector3.UnitX * (4 * dt);
            alley.Update(carrier, dt, carrierVelocity: Vector3.UnitX * 4, carrierEvading: true);
            Assert.Equal(locked, alley.CorrectedDirection);
        }
        Vector3 travel = alley.Position - origin;
        Assert.InRange(Math.Abs(Vector3.Dot(travel, Vector3.UnitX)), 0, .0001f);
        Assert.True(Math.Abs(carrier.X - alley.Position.X) > .7f);
    }
}
