using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class PlayerProfileTests
{
    private static readonly BoundingBox Bounds = new(new(-.5f, -.1f, -.3f), new(.5f, 1.9f, .3f));

    [Fact]
    public void DefaultMatchesExistingVisualDimensions()
    {
        var visual = new PlayerVisualProfile(new());
        Assert.Equal(new TackleAlleyConfig().PlayerVisualHeight, visual.Profile.Height);
        Assert.Equal(1, visual.HeightRatio);
        Assert.Equal(1, visual.WidthRatio);
        Assert.Equal(1, visual.DepthRatio);
        Assert.Equal(Vector3.One, visual.ModelScale(Bounds));
        Assert.Equal(.1f, visual.GroundOffset(Bounds));
        Assert.Equal("Average", visual.BuildDescription);
    }

    [Theory]
    [InlineData(1.65f, .825f)]
    [InlineData(2.05f, 1.025f)]
    public void HeightAndGroundAlignmentUseScaledBounds(float height, float ratio)
    {
        var visual = new PlayerVisualProfile(new() { Height = height });
        Assert.Equal(ratio, visual.HeightRatio, 5);
        var scale = visual.ModelScale(Bounds);
        Assert.Equal(height, (Bounds.Max.Y - Bounds.Min.Y) * scale.Y, 5);
        Assert.Equal(0, Bounds.Min.Y * scale.Y + visual.GroundOffset(Bounds), 5);
    }

    [Fact]
    public void BuildsStayBoundedAndWeightIsRelativeToHeight()
    {
        foreach (float height in new[] { 1.65f, 2f, 2.05f })
        foreach (float weight in new[] { 70f, 110f, 160f })
        foreach (float build in new[] { -1f, 0f, 1f })
        {
            var visual = new PlayerVisualProfile(new() { Height = height, Weight = weight, Build = build });
            Assert.InRange(visual.WidthRatio, .86f, 1.14f);
            Assert.InRange(visual.DepthRatio, .82f, 1.18f);
            Assert.Equal(height / 2, visual.ModelScale(Bounds).Y);
        }
        var lean = new PlayerVisualProfile(new() { Weight = 70, Build = -1 });
        var heavy = new PlayerVisualProfile(new() { Weight = 160, Build = 1 });
        Assert.Equal("Lean", lean.BuildDescription);
        Assert.Equal("Heavy", heavy.BuildDescription);
        Assert.Equal("Stocky", new PlayerVisualProfile(new() { Build = .8f }).BuildDescription);
        Assert.True(new PlayerVisualProfile(new() { Height = 1.65f }).WidthRatio >
            new PlayerVisualProfile(new() { Height = 2.05f }).WidthRatio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidNamesHaveClearErrors(string? name) =>
        Assert.Contains("Name", Assert.Throws<ArgumentException>(() =>
            new PlayerProfile { Name = name! }.Validate()).Message);

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void InvalidNumbersAreRejected(int number) =>
        Assert.Contains("JerseyNumber", Assert.Throws<ArgumentException>(() =>
            new PlayerProfile { JerseyNumber = number }.Validate()).Message);

    [Theory]
    [InlineData("Height", 1.64f)]
    [InlineData("Height", 2.06f)]
    [InlineData("Height", float.NaN)]
    [InlineData("Height", float.PositiveInfinity)]
    [InlineData("Weight", 69f)]
    [InlineData("Weight", 161f)]
    [InlineData("Weight", float.NaN)]
    [InlineData("Weight", float.NegativeInfinity)]
    [InlineData("Build", -1.01f)]
    [InlineData("Build", 1.01f)]
    [InlineData("Build", float.NaN)]
    [InlineData("Build", float.PositiveInfinity)]
    public void InvalidDimensionsAreRejected(string property, float value)
    {
        var profile = new PlayerProfile();
        typeof(PlayerProfile).GetProperty(property)!.SetValue(profile, value);
        Assert.Contains(property, Assert.Throws<ArgumentException>(() => profile.Validate()).Message);
    }

    [Fact]
    public void ConfigurationValidatesEachProfileWithItsLocation()
    {
        var config = new TackleAlleyConfig { BallCarrierProfile = null! };
        Assert.Contains("BallCarrierProfile", Assert.Throws<ArgumentException>(config.Validate).Message);
        config.BallCarrierProfile = new();
        config.OpponentSpawns[1].Profile = null!;
        Assert.Contains("OpponentSpawns[1].Profile", Assert.Throws<ArgumentException>(config.Validate).Message);
        config.OpponentSpawns[1].Profile = new() { Weight = 200 };
        Assert.Contains("OpponentSpawns[1].Profile.Weight", Assert.Throws<ArgumentException>(config.Validate).Message);
    }

    [Fact]
    public void IndependentProfilesPersistAcrossResetsAndConfigReplacement()
    {
        var config = new TackleAlleyConfig { BallCarrierProfile = new() { Name = "Carrier", JerseyNumber = 0 } };
        using var carrier = new BallCarrier(config);
        using var first = new Opponent(Vector3.Zero, config, new() { Name = "First", JerseyNumber = 99, Height = 1.65f });
        using var second = new Opponent(Vector3.One, config, new() { Name = "Second", Height = 2.05f });
        var original = carrier.Profile;
        config.BallCarrierProfile = new() { Name = "Changed" };
        carrier.Reset(); first.Reset(); second.Reset();
        Assert.Same(original, carrier.Profile);
        Assert.Equal("Carrier", carrier.Profile.Name);
        Assert.Equal("First", first.Profile.Name);
        Assert.Equal("Second", second.Profile.Name);
        Assert.NotSame(first.Profile, second.Profile);
        Assert.NotEqual(first.VisualProfile.HeightRatio, second.VisualProfile.HeightRatio);
    }

    [Fact]
    public void GameAssociatesEachProfileWithItsSpawnAndKeepsCameraSettings()
    {
        var config = new TackleAlleyConfig
        {
            BallCarrierProfile = new() { Name = "Carrier", Height = 1.65f, Weight = 70, Build = -1 },
            OpponentSpawns =
            [
                new() { X = -3, Z = -20, Profile = new() { Name = "Left", JerseyNumber = 51 } },
                new() { X = 4, Z = -30, Profile = new() { Name = "Right", JerseyNumber = 92, Height = 2.05f } }
            ]
        };
        var input = new RaylibGameFramework.Input.InputController(
            RaylibGameFramework.Input.InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        var assets = new RaylibGameFramework.Assets.AssetManager(new RaylibGameFramework.Assets.AssetConfig());
        using var game = new TackleAlleyGame(config, input, assets);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var opponents = (Opponent[])typeof(TackleAlleyGame).GetField("_opponents", flags)!.GetValue(game)!;
        var camera = (ThirdPersonCamera)typeof(TackleAlleyGame).GetField("_camera", flags)!.GetValue(game)!;
        var expectedCamera = new ThirdPersonCamera(config);
        expectedCamera.Reset(config.PlayerSpawn.Position, config.PlayerForwardSpeed);
        Assert.Equal(expectedCamera.Camera.Position, camera.Camera.Position);
        Assert.Equal(expectedCamera.Camera.Target, camera.Camera.Target);
        game.ResetRun();
        for (int i = 0; i < opponents.Length; i++)
        {
            Assert.Equal(config.OpponentSpawns[i].Profile, opponents[i].Profile);
            Assert.Equal(config.OpponentSpawns[i].Position, opponents[i].Position);
            Assert.NotSame(config.OpponentSpawns[i].Profile, opponents[i].Profile);
        }
    }

    [Fact]
    public void ShippedProfilesAreDistinctAndRoundTrip()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json"));
        var config = JsonSerializer.Deserialize<TackleAlleyConfig>(json)!;
        config.Validate();
        var roundTrip = JsonSerializer.Deserialize<TackleAlleyConfig>(JsonSerializer.Serialize(config))!;
        Assert.Equal(config.BallCarrierProfile, roundTrip.BallCarrierProfile);
        Assert.Equal(config.OpponentSpawns.Select(s => s.Profile), roundTrip.OpponentSpawns.Select(s => s.Profile));
        var level = LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), config).Resolve("level-1");
        Assert.Equal(4, level.Defenders.Select(s => s.Profile.JerseyNumber).Distinct().Count());
        Assert.True(level.Defenders.Select(s => s.Profile.Height).Distinct().Count() > 1);
        foreach (var defender in level.Defenders)
            Assert.Equal(defender.Profile, JsonSerializer.Deserialize<PlayerProfile>(JsonSerializer.Serialize(defender.Profile)));
    }
}
