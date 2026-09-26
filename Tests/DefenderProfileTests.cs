using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class DefenderProfileTests
{
    private static string ProfileJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
    private static DefenderProfileCatalog Profiles() => DefenderProfileCatalog.Parse(ProfileJson);
    private static DefenderApproach Plan(DefenderProfile profile, float distance = 4, float speed = 6.5f) =>
        DefenderDecision.PlanApproach(profile, new(), Vector3.Zero, new(0, 0, -distance),
            new(1, 0, -distance - 1), null, speed, 4, 1, false);

    [Fact]
    public void BalancedDefaultsMatchLegacyDecisionAndCorrectionTuning()
    {
        var config = new TackleAlleyConfig();
        Assert.Equal(DefenderProfile.Balanced(config), Profiles().Resolve("balanced"));
        foreach (float speed in new[] { 4f, 6.5f, 9f })
        {
            config.OpponentJogSpeed = speed;
            using var previous = new Opponent(Vector3.Zero, config);
            using var balanced = new Opponent(Vector3.Zero, config, behaviorProfile: Profiles().Resolve("balanced"));
            foreach (float distance in new[] { 30f, 8f, 4f, 2.6f, 1.8f, .9f, 30f })
            {
                var target = previous.Position + new Vector3(0, 0, -distance);
                previous.Update(target, .02f);
                balanced.Update(target, .02f);
                Assert.Equal(previous.State, balanced.State);
                Assert.Equal(previous.AiState, balanced.AiState);
                Assert.Equal(previous.Position, balanced.Position);
                Assert.Equal(previous.Velocity, balanced.Velocity);
                Assert.Equal(previous.CurrentContactSolution, balanced.CurrentContactSolution);
            }
        }
    }

    [Fact]
    public void EveryProfileUsesTheSamePipelineRegardlessOfIdOrName()
    {
        var profiles = Profiles();
        Assert.Equal(3, profiles.Profiles.Count);
        foreach (var profile in profiles.Profiles)
        {
            var renamed = profile with { Id = "custom", Name = "Custom" };
            Assert.Equal(Plan(profile), Plan(renamed));
            using var named = new Opponent(Vector3.Zero, new(), behaviorProfile: profile);
            using var custom = new Opponent(Vector3.Zero, new(), behaviorProfile: renamed);
            Assert.Equal(named.GetType(), custom.GetType());
            foreach (float distance in new[] { 30f, 8f, 4f, 2.6f, 1.8f, .9f })
            {
                Vector3 target = named.Position + new Vector3(0, 0, -distance);
                named.Update(target, .03f, target + Vector3.UnitX);
                custom.Update(target, .03f, target + Vector3.UnitX);
                Assert.Equal(named.State, custom.State);
                Assert.Equal(named.AiState, custom.AiState);
                Assert.Equal(named.Position, custom.Position);
                Assert.Equal(named.CurrentContactSolution, custom.CurrentContactSolution);
            }
        }
    }

    [Fact]
    public void ParametersIndependentlyChangeApproachWithoutChangingBodyOrPhysics()
    {
        var balanced = Profiles().Resolve("balanced");
        Assert.NotEqual(Plan(balanced).PursuitTarget, Plan(balanced with { PursuitPredictionStrength = 0 }).PursuitTarget);
        Assert.NotEqual(Plan(balanced).PursuitTarget, Plan(balanced with { ContainBias = .8f }).PursuitTarget);
        Assert.True(Plan(balanced with { ReactionTime = .6f }).RequiredStoppingDistance > Plan(balanced).RequiredStoppingDistance);
        Assert.True(Plan(balanced).Ready);
        Assert.False(Plan(balanced with { BreakdownDistance = 3 }).Ready);
        using var passive = new Opponent(Vector3.Zero, new(), behaviorProfile: balanced with { PursuitAggression = .5f });
        using var eager = new Opponent(Vector3.Zero, new(), behaviorProfile: balanced with { PursuitAggression = 1.5f });
        passive.Update(new(0, 0, 10), 0);
        eager.Update(new(0, 0, 10), 0);
        Assert.Equal(OpponentPace.Run, passive.Pace);
        Assert.Equal(OpponentPace.Sprint, eager.Pace);
        Assert.Equal(passive.Profile, eager.Profile);
        Assert.Equal(passive.Physical.TotalMass, eager.Physical.TotalMass);
        Assert.Equal(passive.Movement.SprintSpeed, eager.Movement.SprintSpeed);
    }

    [Fact]
    public void PreferenceAndCommitDistanceNeverBypassReachability()
    {
        var p = new DefenderProfile();
        var near = Plan(p, .9f, 4);
        Assert.Equal(DefenderTackleChoice.Wrap, DefenderDecision.SelectTackle(p, near, true, true, true, 1, 1, 2));
        Assert.Equal(DefenderTackleChoice.Lunge, DefenderDecision.SelectTackle(p with { WrapPreference = .2f },
            near, true, true, true, 1, 1, 2));
        foreach (var profile in Profiles().Profiles)
        {
            Assert.Equal(DefenderTackleChoice.None, DefenderDecision.SelectTackle(profile, near, true, false, false, 1, 1, 2));
            Assert.Equal(DefenderTackleChoice.None, DefenderDecision.SelectTackle(profile,
                near with { Distance = 11 }, true, true, true, 1, 1, 12));
        }
        Assert.Equal(DefenderTackleChoice.None, DefenderDecision.SelectTackle(p with { TackleCommitDistance = .5f },
            near, true, true, true, 1, 1, 2));
        Assert.Equal(DefenderTackleChoice.None, DefenderDecision.SelectTackle(p with { LungePreference = 0 },
            Plan(p, 1.8f), true, false, true, 1, 1, 2));
    }

    [Fact]
    public void LevelsResolveBehaviorSeparatelyFromPlayerProfileAndResetPreservesIt()
    {
        var config = new TackleAlleyConfig();
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "levels.json")))!;
        root["Levels"]![0]!["Defenders"]![0]!["BehaviorProfile"] = "contain";
        var registry = Profiles();
        var level = LevelCatalog.Parse(root.ToJsonString(), config, registry).Resolve("level-1");
        Assert.Equal("marcus-hill", level.Defenders[0].ProfileReference);
        Assert.Same(registry.Resolve("contain"), level.Defenders[0].BehaviorProfile);
        var input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        using var game = new TackleAlleyGame(config, input, new AssetManager(new AssetConfig()), level);
        var defenders = (Opponent[])typeof(TackleAlleyGame).GetField("_opponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        Assert.Equal("contain", defenders[0].BehaviorProfile.Id);
        game.ResetRun();
        Assert.Equal("contain", defenders[0].BehaviorProfile.Id);
        Assert.Equal(DefenderAiState.Pursuit, defenders[0].AiState);
        foreach (string? invalid in new[] { "", "unknown", "Balanced", null })
        {
            root["Levels"]![0]!["Defenders"]![0]!["BehaviorProfile"] = invalid;
            Assert.Throws<InvalidDataException>(() => LevelCatalog.Parse(root.ToJsonString(), config, registry));
        }
        root["Levels"]![0]!["Defenders"]![0]!.AsObject().Remove("BehaviorProfile");
        Assert.Throws<InvalidDataException>(() => LevelCatalog.Parse(root.ToJsonString(), config, registry));
    }

    [Fact]
    public void EveryScalarRejectsNonFiniteAndAuthoredMissingValues()
    {
        foreach (var field in typeof(DefenderProfile).GetProperties().Where(p => p.PropertyType == typeof(float)))
        {
            var profile = new DefenderProfile();
            field.SetValue(profile, float.NaN);
            Assert.Throws<ArgumentException>(profile.Validate);
            var json = JsonNode.Parse(ProfileJson)!;
            json["Profiles"]![0]!.AsObject().Remove(field.Name);
            Assert.Throws<InvalidDataException>(() => DefenderProfileCatalog.Parse(json.ToJsonString()));
        }
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("blank-name")]
    [InlineData("missing-name")]
    [InlineData("invalid-id")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("unknown-field")]
    [InlineData("invalid-ranges")]
    [InlineData("no-tackles")]
    [InlineData("correction")]
    public void InvalidCatalogsFailAtStartup(string scenario)
    {
        var root = JsonNode.Parse(ProfileJson)!;
        var first = root["Profiles"]![0]!;
        switch (scenario)
        {
            case "duplicate": root["Profiles"]!.AsArray().Add(first.DeepClone()); break;
            case "blank-name": first["Name"] = " "; break;
            case "missing-name": first.AsObject().Remove("Name"); break;
            case "invalid-id": first["Id"] = "Bad ID"; break;
            case "null": root["Profiles"]![0] = null; break;
            case "empty": root["Profiles"] = new JsonArray(); break;
            case "unknown-field": first["TackleChance"] = 1; break;
            case "invalid-ranges": first["BreakdownDistance"] = 9; break;
            case "no-tackles": first["WrapPreference"] = 0; first["LungePreference"] = 0; break;
            case "correction": first["MaximumCorrectionDegrees"] = 90; break;
        }
        Assert.Throws<InvalidDataException>(() => DefenderProfileCatalog.Parse(root.ToJsonString()));
    }
}
