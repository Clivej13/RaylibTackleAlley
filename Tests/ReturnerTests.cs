using System.Reflection;
using System.Text.Json;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class ReturnerTests
{
    private static ReturnerCatalog Catalog() => ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json"));
    private static string Json(params ReturnerDefinition[] entries) => JsonSerializer.Serialize(new { Returners = entries });
    private static ReturnerDefinition Entry(string id = "test") => new() { Id = id, Profile = new() };

    [Fact]
    public void ShippedCatalogHasFiveUniqueDifferentPlayers()
    {
        var catalog = Catalog();
        Assert.Equal(5, catalog.Returners.Count);
        Assert.Equal(5, catalog.Returners.Select(r => r.Id).Distinct().Count());
        Assert.Equal(5, catalog.Returners.Select(r => r.Profile).Distinct().Count());
        Assert.Equal(5, catalog.Returners.Select(r => r.Uniform).Distinct().Count());
        Assert.All(catalog.Returners, r => Assert.False(string.IsNullOrWhiteSpace(r.Uniform)));
        Assert.Equal(5, catalog.Returners.Select(r => r.Taunt).Distinct().Count());
        Assert.All(catalog.Returners, r => Assert.False(string.IsNullOrWhiteSpace(r.Taunt)));
        foreach (var entry in catalog.Returners) Assert.Same(entry, catalog.Resolve(entry.Id));
    }

    [Fact]
    public void AvailableArtworkKeepsEveryAuthoredMappingAndProfile()
    {
        var catalog = Catalog();
        var assets = AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json"));
        var resolved = catalog.ResolveAvailableAssets(assets, "OffenseUniform");
        foreach (var entry in catalog.Returners)
            Assert.Equal(entry, resolved.Resolve(entry.Id));
    }

    [Fact]
    public void MissingArtworkFallsBackIndependentlyWithoutChangingAuthoredProfiles()
    {
        var catalog = Catalog();
        var assets = AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json"));
        var missing = catalog.ResolveAvailableAssets(assets, "OffenseUniform", _ => false);
        var uniformsOnly = catalog.ResolveAvailableAssets(assets, "OffenseUniform",
            path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        var tauntsOnly = catalog.ResolveAvailableAssets(assets, "OffenseUniform",
            path => path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase));
        var unregistered = catalog.ResolveAvailableAssets(new AssetConfig(), "CustomOffense");
        foreach (var entry in catalog.Returners)
        {
            Assert.Equal("OffenseUniform", missing.Resolve(entry.Id).Uniform);
            Assert.Equal(ReturnerCatalog.DefaultTaunt, missing.Resolve(entry.Id).Taunt);
            Assert.Same(entry.Profile, missing.Resolve(entry.Id).Profile);
            Assert.Equal(entry.Uniform, uniformsOnly.Resolve(entry.Id).Uniform);
            Assert.Equal(ReturnerCatalog.DefaultTaunt, uniformsOnly.Resolve(entry.Id).Taunt);
            Assert.Equal("OffenseUniform", tauntsOnly.Resolve(entry.Id).Uniform);
            Assert.Equal(entry.Taunt, tauntsOnly.Resolve(entry.Id).Taunt);
            Assert.Equal("CustomOffense", unregistered.Resolve(entry.Id).Uniform);
            Assert.Equal(ReturnerCatalog.DefaultTaunt, unregistered.Resolve(entry.Id).Taunt);
            Assert.NotEqual("OffenseUniform", entry.Uniform);
            Assert.NotEqual(ReturnerCatalog.DefaultTaunt, entry.Taunt);
        }
        var legacy = ReturnerCatalog.Parse(Json(Entry())).ResolveAvailableAssets(assets, "OffenseUniform");
        Assert.Equal("OffenseUniform", legacy.Resolve("test").Uniform);
        Assert.Equal(ReturnerCatalog.DefaultTaunt, legacy.Resolve("test").Taunt);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Returners\":null}")]
    [InlineData("{\"Returners\":[]}")]
    [InlineData("{\"Returners\":[null]}")]
    public void EmptyOrNullCatalogIsRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => ReturnerCatalog.Parse(json));

    [Theory]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("{\"Returners\":[],\"Typo\":1}")]
    public void MalformedOrMissingFieldsAreRejected(string json) =>
        Assert.Throws<JsonException>(() => ReturnerCatalog.Parse(json));

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("Upper")]
    [InlineData("test\n")]
    public void InvalidIdIsRejected(string id) =>
        Assert.Throws<InvalidDataException>(() => ReturnerCatalog.Parse(Json(Entry(id))));

    [Fact]
    public void EveryProfileFieldIsRequiredAndUnknownFieldsAreRejected()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(Json(Entry()))!;
        var profile = root["Returners"]![0]!["Profile"]!.AsObject();
        foreach (var name in profile.Select(p => p.Key).ToArray())
        {
            var value = profile[name]!.DeepClone();
            profile.Remove(name);
            Assert.Throws<JsonException>(() => ReturnerCatalog.Parse(root.ToJsonString()));
            profile[name] = value;
        }
        profile["Speeed"] = 90;
        Assert.Throws<JsonException>(() => ReturnerCatalog.Parse(root.ToJsonString()));
    }

    [Fact]
    public void DuplicateUnknownAndInvalidProfilesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => ReturnerCatalog.Parse(Json(Entry(), Entry())));
        Assert.Throws<InvalidDataException>(() => ReturnerCatalog.Parse(Json(Entry() with { Uniform = " " })));
        Assert.Null(ReturnerCatalog.Parse(Json(Entry())).Returners[0].Uniform);
        Assert.Null(ReturnerCatalog.Parse(Json(Entry())).Returners[0].Taunt);
        Assert.Throws<InvalidDataException>(() => ReturnerCatalog.Parse(Json(Entry() with { Taunt = " " })));
        Assert.Throws<ArgumentException>(() => Catalog().Resolve("missing"));
        Assert.Throws<InvalidDataException>(() => ReturnerCatalog.Parse(Json(Entry() with { Profile = null! })));
        foreach (var profile in new[] { new PlayerProfile { Speed = 0 }, new() { Juke = 101 },
            new() { JerseyNumber = 100 }, new() { Weight = 200 }, new() { Name = "" } })
            Assert.Throws<InvalidDataException>(() => ReturnerCatalog.Parse(Json(Entry() with { Profile = profile })));
    }

    [Fact]
    public void SelectionReachesCarrierAndResetRetainsItWithoutMutatingTuning()
    {
        var catalog = Catalog();
        var config = new TackleAlleyConfig();
        string before = JsonSerializer.Serialize(config);
        var input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        using var game = new TackleAlleyGame(config, input, new AssetManager(new AssetConfig()));
        foreach (var entry in catalog.Returners)
        {
            game.SelectReturner(catalog, entry.Id);
            var carrier = (BallCarrier)typeof(TackleAlleyGame).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
            Assert.Equal(entry.Id, game.SelectedReturnerId);
            Assert.Equal(entry.Profile, carrier.Profile);
            var expected = new PlayerMovementAttributes(entry.Profile, config);
            Assert.Equal(expected.RunningSpeed, carrier.CurrentForwardSpeed);
            Assert.Equal(expected.AccelerationRate, carrier.Movement.AccelerationRate);
            Assert.Equal(expected.JukeSpeed, carrier.Movement.JukeSpeed);
            Assert.Equal(entry.Profile.Weight, carrier.Physical.TotalMass);
            Assert.Equal(new PlayerPhysicalAttributes(entry.Profile, config).TackleResistanceMultiplier, carrier.Physical.TackleResistanceMultiplier);
            game.ResetRun();
            Assert.Equal(entry.Profile, game.SelectedProfile);
            Assert.Equal(config.PlayerSpawn.Position, carrier.Position);
            Assert.False(game.GameOver);
            Assert.False(game.Touchdown);
        }
        var selected = game.SelectedProfile;
        Assert.Throws<ArgumentException>(() => game.SelectReturner(catalog, "missing"));
        Assert.Same(selected, game.SelectedProfile);
        Assert.Equal(before, JsonSerializer.Serialize(config));
    }

    [Fact]
    public void JukeRatingChangesJukeWithoutChangingSpeedOrGlobalBalance()
    {
        var config = new TackleAlleyConfig();
        using var low = new BallCarrier(config, new() { Juke = 1 });
        using var high = new BallCarrier(config, new() { Juke = 100 });
        Assert.True(high.Movement.JukeSpeed > low.Movement.JukeSpeed);
        Assert.Equal(low.Speed, high.Speed);
        Assert.Equal(low.Movement.SpinSpeed, high.Movement.SpinSpeed);
    }
}
