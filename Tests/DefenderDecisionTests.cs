using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class DefenderDecisionTests
{
    private static Opponent Running(DefenderProfile? profile = null, float speed = 6.5f)
    {
        var defender = new Opponent(Vector3.Zero, new() { OpponentJogSpeed = speed }, behaviorProfile: profile);
        defender.Update(new(0, 0, -30), .01f, false, Vector2.Zero, false);
        return defender;
    }

    [Fact]
    public void PursuitApproachBreakdownControlledApproachAndWrapAreDistinctStages()
    {
        using var defender = Running();
        defender.Update(defender.Position + new Vector3(0, 0, -30), 0);
        Assert.Equal(DefenderAiState.Pursuit, defender.AiState);
        defender.Update(defender.Position + new Vector3(0, 0, -8), 0);
        Assert.Equal(DefenderAiState.Approach, defender.AiState);
        defender.Update(defender.Position + new Vector3(0, 0, -4), 0);
        Assert.Equal(DefenderAiState.Breakdown, defender.AiState);
        Assert.True(defender.DecisionDebug.RequiredStoppingDistance > 1);
        for (int i = 0; i < 5; i++)
            defender.Update(defender.Position + new Vector3(0, 0, -4), .05f);
        Assert.Equal(DefenderAiState.Approach, defender.AiState);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        defender.Update(defender.Position + new Vector3(0, 0, -.9f), 0, carrierVelocity: defender.Velocity);
        Assert.Equal(DefenderAiState.WrapCommitment, defender.AiState);
        Assert.Equal(DefenderState.SetWrap, defender.State);
        var selected = defender.DecisionDebug.SelectedTackleTarget;
        Assert.NotNull(selected);
        defender.Update(new(30, 0, 30), .1f);
        Assert.Equal(selected, defender.DecisionDebug.SelectedTackleTarget);
        Assert.Equal(DefenderAiState.WrapCommitment, defender.AiState);
        defender.Update(new(30, 0, 30), .4f);
        Assert.Equal(DefenderAiState.Pursuit, defender.AiState);
        defender.Reset();
        Assert.Equal(DefenderAiState.Pursuit, defender.AiState);
        Assert.Null(defender.DecisionDebug.SelectedTackleTarget);
    }

    [Theory]
    [InlineData("balanced")]
    [InlineData("aggressive")]
    [InlineData("contain")]
    public void EveryProfileUsesReachableLungeThenTheSameDirectionLock(string id)
    {
        var profiles = DefenderProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
        var profile = profiles.Resolve(id);
        using var defender = Running(profile, 4);
        defender.Update(defender.Position + new Vector3(0, 0, -4), 0, true, Vector2.Zero, false);
        defender.Update(defender.Position + new Vector3(0, 0, -1.8f), 0);
        Assert.Equal(DefenderAiState.LungeCommitment, defender.AiState);
        Assert.True(defender.InitialContactSolution.Reachable);
        Assert.Equal(defender.InitialContactSolution.ContactPoint, defender.DecisionDebug.SelectedTackleTarget);
        defender.Update(new(-.1f, 0, -1.8f), .25f);
        Assert.True(defender.DirectionLocked);
        Vector3 locked = defender.CorrectedDirection;
        defender.Update(new(30, 0, 30), .1f);
        Assert.Equal(locked, defender.CorrectedDirection);
        Assert.Equal(locked, defender.DecisionDebug.LockedDirection);
        Assert.Equal(DefenderAiState.LungeCommitment, defender.AiState);
        Assert.Equal("Committed execution", defender.DecisionDebug.Reason);
    }

    [Fact]
    public void CorrectionSettingsArePerDefenderAndDoNotMutateSharedTuning()
    {
        var config = new TackleAlleyConfig();
        var noCorrection = new DefenderProfile { CorrectionWindowFraction = 0, MaximumCorrectionDegrees = 0 };
        using var defender = Running(noCorrection, 4);
        defender.Update(defender.Position + new Vector3(0, 0, -4), 0, true, Vector2.Zero, false);
        defender.Update(defender.Position + new Vector3(0, 0, -1.8f), 0);
        Assert.True(defender.DirectionLocked);
        var initial = defender.InitialLaunchDirection;
        defender.Update(new(-.4f, 0, -1.8f), .1f);
        Assert.Equal(initial, defender.CorrectedDirection);
        using var second = new Opponent(Vector3.Zero, config, behaviorProfile: noCorrection);
        Assert.Equal(.4f, config.LungeCorrectionWindowFraction);
        Assert.Equal(90, config.LungeCorrectionRateDegrees);
        Assert.Equal(18, config.LungeMaximumCorrectionDegrees);
        Assert.Equal(0, second.BehaviorProfile.CorrectionWindowFraction);
    }

    [Fact]
    public void DebugSeparatesRawPredictionFromCappedPursuitAndShowsApproachDirection()
    {
        using var defender = Running(new DefenderProfile { PursuitPredictionStrength = 1.5f }, 4);
        Vector3 target = defender.Position + new Vector3(0, 0, -8);
        defender.Update(target, 0, target + Vector3.UnitX * 10);
        var debug = defender.DecisionDebug;
        Assert.Equal(target + Vector3.UnitX * 15, debug.PredictedTarget);
        Assert.True(Vector3.Distance(debug.PursuitTarget, target) < Vector3.Distance(debug.PredictedTarget, target));
        Assert.Equal(Vector3.Normalize(debug.PursuitTarget - defender.Position), debug.ApproachDirection);
        Assert.Equal(DefenderAiState.Approach, debug.State);
        Assert.Null(debug.SelectedTackleTarget);
    }

    [Fact]
    public void BreakdownHysteresisAndRearChasingRemainIndependentOfTackleReachability()
    {
        var profile = new DefenderProfile();
        var config = new TackleAlleyConfig();
        DefenderApproach Plan(float distance, bool preparing, Vector3? forward = null) =>
            DefenderDecision.PlanApproach(profile, config, Vector3.Zero, new(0, 0, -distance),
                null, forward, 4, 4, 1, preparing);
        Assert.True(Plan(4, false).Ready);
        Assert.False(Plan(4.5f, false).Ready);
        Assert.True(Plan(4.5f, true).Ready);
        Assert.False(Plan(5.1f, true).Ready);
        Assert.False(Plan(4, true, -Vector3.UnitZ).Ready);
        Assert.Equal("Outside ready cone", Plan(4, true, -Vector3.UnitZ).Reason);
        Assert.True(Plan(4, true, Vector3.UnitZ).Ready);
    }
}
