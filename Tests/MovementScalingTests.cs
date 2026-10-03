using System.Numerics;
using System.Text.Json;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class MovementScalingTests
{
    private static TackleAlleyConfig ShippedConfig() => JsonSerializer.Deserialize<TackleAlleyConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json")))!;

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void ReturnerSpeedInfluenceIncreasesByTier(int rating)
    {
        var movement = new PlayerMovementAttributes(new() { Speed = rating }, new());
        float jog = Math.Abs(movement.JogSpeedMultiplier - 1);
        float run = Math.Abs(movement.RunningSpeedMultiplier - 1);
        float sprint = Math.Abs(movement.SprintSpeedMultiplier - 1);
        Assert.Equal(.05f, jog, 5);
        Assert.Equal(.15f, run, 5);
        Assert.Equal(.30f, sprint, 5);
        Assert.True(sprint > run && run > jog);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AccelerationTiersAndSpeedAreIndependent(bool defender)
    {
        var config = new TackleAlleyConfig();
        var scaling = defender ? config.DefenderMovementScaling : config.ReturnerMovementScaling;
        scaling.AccelerationTierInfluence = new() { Jog = .1f, Run = .3f, Sprint = .5f };
        var neutral = new PlayerMovementAttributes(new(), config, defender);
        var speed = new PlayerMovementAttributes(new() { Speed = 100 }, config, defender);
        var acceleration = new PlayerMovementAttributes(new() { Acceleration = 100 }, config, defender);
        for (int tier = 1; tier <= 3; tier++)
        {
            Assert.Equal(neutral.TierAcceleration(tier), speed.TierAcceleration(tier));
            Assert.Equal(neutral.TierSpeed(tier), acceleration.TierSpeed(tier));
            Assert.Equal(config.ForwardAcceleration * (1 + new[] { .1f, .3f, .5f }[tier - 1]),
                acceleration.TierAcceleration(tier), 5);
        }
    }

    [Fact]
    public void BothComponentsUseSharedCalculationWithIndependentRoleSettings()
    {
        var config = new TackleAlleyConfig();
        config.DefenderMovementScaling.SpeedTierInfluence = new() { Jog = .01f, Run = .08f, Sprint = .12f };
        config.DefenderMovementScaling.AccelerationTierInfluence = new() { Jog = .05f, Run = .1f, Sprint = .15f };
        var profile = new PlayerProfile { Speed = 100, Acceleration = 100 };
        using var carrier = new BallCarrier(config, profile);
        using var opponent = new Opponent(Vector3.Zero, config, profile);
        var returnerExpected = new PlayerMovementAttributes(profile, config);
        var defenderExpected = new PlayerMovementAttributes(profile, config, defender: true);
        for (int tier = 1; tier <= 3; tier++)
        {
            Assert.Equal(returnerExpected.TierSpeed(tier), carrier.Movement.TierSpeed(tier));
            Assert.Equal(defenderExpected.TierSpeed(tier), opponent.Movement.TierSpeed(tier));
            Assert.Equal(returnerExpected.TierAcceleration(tier), carrier.Movement.TierAcceleration(tier));
            Assert.Equal(defenderExpected.TierAcceleration(tier), opponent.Movement.TierAcceleration(tier));
            Assert.NotEqual(carrier.Movement.TierSpeed(tier), opponent.Movement.TierSpeed(tier));
            Assert.NotEqual(carrier.Movement.TierAcceleration(tier), opponent.Movement.TierAcceleration(tier));
        }
    }

    public static IEnumerable<object[]> InvalidInfluences()
    {
        foreach (bool defender in new[] { false, true })
        foreach (bool acceleration in new[] { false, true })
        foreach (string tier in new[] { "Jog", "Run", "Sprint" })
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.01f, 1f, 2f })
            yield return new object[] { defender, acceleration, tier, value };
    }

    [Theory]
    [MemberData(nameof(InvalidInfluences))]
    public void EveryInfluenceRejectsInvalidValuesWithFieldPath(bool defender, bool acceleration, string tier, float value)
    {
        var config = new TackleAlleyConfig();
        var scaling = defender ? config.DefenderMovementScaling : config.ReturnerMovementScaling;
        var influence = acceleration ? scaling.AccelerationTierInfluence : scaling.SpeedTierInfluence;
        typeof(MovementTierInfluence).GetProperty(tier)!.SetValue(influence, value);
        string path = (defender ? "Defender" : "Returner") + "MovementScaling." +
            (acceleration ? "Acceleration" : "Speed") + "TierInfluence." + tier;
        Assert.Contains(path, Assert.Throws<ArgumentException>(config.ValidateMovementScaling).Message);
        Assert.Throws<ArgumentException>(() => new BallCarrier(config));
        Assert.Throws<ArgumentException>(() => new Opponent(Vector3.Zero, config));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnorderedAndOverflowingSpeedsFailForEitherRole(bool defender)
    {
        var config = new TackleAlleyConfig();
        var scaling = defender ? config.DefenderMovementScaling : config.ReturnerMovementScaling;
        scaling.SpeedTierInfluence.Sprint = .99f;
        Assert.Contains("jog < run < sprint", Assert.Throws<ArgumentException>(config.ValidateMovementScaling).Message);
        scaling.SpeedTierInfluence.Sprint = .3f;
        if (defender) config.OpponentSprintSpeed = float.MaxValue;
        else config.PlayerSprintSpeed = float.MaxValue;
        Assert.Throws<ArgumentException>(config.ValidateMovementScaling);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    [InlineData(float.MaxValue)]
    public void InvalidEffectiveAccelerationFails(float value)
    {
        var config = new TackleAlleyConfig { ForwardAcceleration = value };
        Assert.Contains("ForwardAcceleration", Assert.Throws<ArgumentException>(config.ValidateMovementScaling).Message);
    }

    [Theory]
    [InlineData("ReturnerMovementScaling", "null")]
    [InlineData("DefenderMovementScaling", "null")]
    [InlineData("ReturnerMovementScaling", "{\"SpeedTierInfluence\":null}")]
    [InlineData("DefenderMovementScaling", "{\"AccelerationTierInfluence\":null}")]
    public void NullConfigBlocksFailClearly(string role, string value)
    {
        var config = JsonSerializer.Deserialize<TackleAlleyConfig>("{\"" + role + "\":" + value + "}")!;
        Assert.Contains(role, Assert.Throws<ArgumentException>(config.ValidateMovementScaling).Message);
    }

    [Fact]
    public void AllRatingsRetainOrderedSpeedsAndValidAccelerations()
    {
        var config = ShippedConfig();
        config.ValidateMovementScaling();
        foreach (bool defender in new[] { false, true })
        for (int rating = 1; rating <= 100; rating++)
        {
            var movement = new PlayerMovementAttributes(new() { Speed = rating, Acceleration = rating }, config, defender);
            Assert.True(movement.JogSpeed < movement.RunningSpeed && movement.RunningSpeed < movement.SprintSpeed);
            for (int tier = 1; tier <= 3; tier++)
                Assert.True(float.IsFinite(movement.TierAcceleration(tier)) && movement.TierAcceleration(tier) >= 0);
        }
    }

    [Fact]
    public void CurrentRosterPreservesPreviousMovementDefaults()
    {
        var config = ShippedConfig();
        var returners = ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json"));
        var defenders = LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), config).Resolve("level-1").Defenders;
        foreach (var profile in returners.Returners.Select(r => r.Profile))
            Check(profile, false);
        foreach (var defender in defenders)
            Check(defender.Profile, true);
        void Check(PlayerProfile profile, bool defender)
        {
            var movement = new PlayerMovementAttributes(profile, config, defender);
            float offset = (profile.Speed - 50) / (profile.Speed <= 50 ? 49f : 50f);
            for (int tier = 1; tier <= 3; tier++)
            {
                float oldMultiplier = defender ? PlayerMovementAttributes.RatingMultiplier(profile.Speed, .8f, 1.2f)
                    : 1 + offset * new[] { .05f, .15f, .3f }[tier - 1];
                Assert.Equal(movement.BaselineSpeed(tier) * oldMultiplier, movement.TierSpeed(tier), 5);
                Assert.Equal(config.ForwardAcceleration * PlayerMovementAttributes.RatingMultiplier(profile.Acceleration, .75f, 1.25f),
                    movement.TierAcceleration(tier), 5);
            }
        }
    }

    [Fact]
    public void FasterSprintShortensTravelWindowMoreThanRunWithoutChangingEvadeDuration()
    {
        var config = ShippedConfig();
        var neutral = new PlayerMovementAttributes(new(), config);
        var fast = new PlayerMovementAttributes(new() { Speed = 100 }, config);
        float sprintWindowRatio = neutral.SprintSpeed / fast.SprintSpeed;
        float runWindowRatio = neutral.RunningSpeed / fast.RunningSpeed;
        Assert.True(sprintWindowRatio < .8f);
        Assert.True(runWindowRatio > .85f);
        Assert.True(sprintWindowRatio < runWindowRatio);
    }

    [Fact]
    public void ProfilesHaveOnlyIdentityBodyAndRawRatings()
    {
        var expected = new[] { "Name", "JerseyNumber", "Height", "Weight", "Build", "Strength", "Speed", "Acceleration", "Agility", "Juke" };
        Assert.Equal(expected.Order(), typeof(PlayerProfile).GetProperties().Select(p => p.Name).Order());
        foreach (var rating in new[] { "Strength", "Speed", "Acceleration", "Agility", "Juke" })
            Assert.Equal(typeof(int), typeof(PlayerProfile).GetProperty(rating)!.PropertyType);
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "returners.json"));
        Assert.Throws<JsonException>(() => ReturnerCatalog.Parse(json.Replace("\"Speed\": 98", "\"Speed\": 98, \"MovementScaling\": {}")));
        string levels = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "levels.json"));
        Assert.Throws<InvalidDataException>(() => LevelCatalog.Parse(
            levels.Replace("\"Speed\": 85", "\"Speed\": 85, \"SpeedTierInfluence\": {}"), ShippedConfig()));
    }
}
