using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class LevelCatalogTests
{
    private static string CatalogJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "levels.json"));
    private static TackleAlleyConfig Tuning() => JsonSerializer.Deserialize<TackleAlleyConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json")))!;
    private static T Field<T>(object value, string name) =>
        (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static InputController Input() => new(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));

    [Fact]
    public void ShippedCatalogLoadsExactlyTheMigratedLevelOne()
    {
        var catalog = LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), Tuning());
        var level = Assert.Single(catalog.Levels);
        Assert.Same(level, catalog.Resolve("level-1"));
        Assert.Equal("Level 1", level.Name);
        Assert.Equal(24, level.FieldWidth);
        Assert.Equal(Vector3.Zero, level.PlayerSpawn);
        var legacy = LegacySetup();
        Assert.Equal(legacy.OpponentSpawns.Select(s => s.Position), level.Defenders.Select(s => s.Position));
        Assert.Equal(legacy.OpponentSpawns.Select(s => s.Profile), level.Defenders.Select(s => s.Profile));
        Assert.Equal(new[] { "marcus-hill", "devon-brooks", "andre-cole", "sam-taylor" },
            level.Defenders.Select(s => s.ProfileReference));
        Assert.Throws<ArgumentException>(() => catalog.Resolve("missing"));
        Assert.Throws<NotSupportedException>(() => ((IList<LevelDefinition>)catalog.Levels).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<LevelDefender>)level.Defenders).Clear());
    }

    public static IEnumerable<object[]> InvalidCatalogs()
    {
        string[] cases = [
            "duplicate-level", "duplicate-profile", "missing-id", "invalid-id", "blank-id",
            "missing-name", "null-name", "blank-name", "missing-width", "zero-width", "negative-width",
            "too-wide", "too-narrow", "overflow-width", "nan-width", "null-player", "missing-player",
            "missing-x", "missing-z", "player-outside-x", "player-on-sideline", "player-behind-start",
            "player-in-endzone", "player-overflow", "defender-outside-x", "defender-before-start",
            "defender-in-endzone", "missing-defenders", "empty-defenders", "null-defenders", "null-defender",
            "missing-reference", "null-reference", "blank-reference", "unknown-reference", "wrong-case-reference",
            "empty-levels", "null-levels", "null-level", "empty-profiles", "null-profile-entry",
            "null-profile", "invalid-player-profile", "unknown-property", "unknown-spawn-property"
        ];
        foreach (string value in cases) yield return [value];
    }

    [Theory]
    [MemberData(nameof(InvalidCatalogs))]
    public void InvalidAuthoredDataFailsBeforeGameplay(string scenario)
    {
        var root = JsonNode.Parse(CatalogJson)!;
        var level = root["Levels"]![0]!;
        var spawn = level["PlayerSpawn"]!;
        var defender = level["Defenders"]![0]!;
        switch (scenario)
        {
            case "duplicate-level": root["Levels"]!.AsArray().Add(level.DeepClone()); break;
            case "duplicate-profile": root["DefenderProfiles"]!.AsArray().Add(root["DefenderProfiles"]![0]!.DeepClone()); break;
            case "missing-id": level.AsObject().Remove("Id"); break;
            case "invalid-id": level["Id"] = "Level One"; break;
            case "blank-id": level["Id"] = ""; break;
            case "missing-name": level.AsObject().Remove("Name"); break;
            case "null-name": level["Name"] = null; break;
            case "blank-name": level["Name"] = "  "; break;
            case "missing-width": level.AsObject().Remove("FieldWidth"); break;
            case "zero-width": level["FieldWidth"] = 0; break;
            case "negative-width": level["FieldWidth"] = -1; break;
            case "too-wide": level["FieldWidth"] = 100; break;
            case "too-narrow": level["FieldWidth"] = .1; break;
            case "overflow-width": level["FieldWidth"] = JsonNode.Parse("1e100"); break;
            case "nan-width": level["FieldWidth"] = "NaN"; break;
            case "null-player": level["PlayerSpawn"] = null; break;
            case "missing-player": level.AsObject().Remove("PlayerSpawn"); break;
            case "missing-x": spawn.AsObject().Remove("X"); break;
            case "missing-z": defender.AsObject().Remove("Z"); break;
            case "player-outside-x": spawn["X"] = 13; break;
            case "player-on-sideline": spawn["X"] = 12; break;
            case "player-behind-start": spawn["Z"] = 1; break;
            case "player-in-endzone": spawn["Z"] = -72; break;
            case "player-overflow": spawn["X"] = JsonNode.Parse("1e100"); break;
            case "defender-outside-x": defender["X"] = -13; break;
            case "defender-before-start": defender["Z"] = 1; break;
            case "defender-in-endzone": defender["Z"] = -80; break;
            case "missing-defenders": level.AsObject().Remove("Defenders"); break;
            case "empty-defenders": level["Defenders"] = new JsonArray(); break;
            case "null-defenders": level["Defenders"] = null; break;
            case "null-defender": level["Defenders"]![0] = null; break;
            case "missing-reference": defender.AsObject().Remove("Profile"); break;
            case "null-reference": defender["Profile"] = null; break;
            case "blank-reference": defender["Profile"] = " "; break;
            case "unknown-reference": defender["Profile"] = "missing"; break;
            case "wrong-case-reference": defender["Profile"] = "Marcus-Hill"; break;
            case "empty-levels": root["Levels"] = new JsonArray(); break;
            case "null-levels": root["Levels"] = null; break;
            case "null-level": root["Levels"]![0] = null; break;
            case "empty-profiles": root["DefenderProfiles"] = new JsonArray(); break;
            case "null-profile-entry": root["DefenderProfiles"]![0] = null; break;
            case "null-profile": root["DefenderProfiles"]![0]!["Profile"] = null; break;
            case "invalid-player-profile": root["DefenderProfiles"]![0]!["Profile"]!["Weight"] = 500; break;
            case "unknown-property": level["FieldWidht"] = 24; break;
            case "unknown-spawn-property": spawn["Y"] = 20; break;
        }
        Assert.Throws<InvalidDataException>(() => LevelCatalog.Parse(root.ToJsonString(), Tuning()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("[]")]
    public void MalformedOrIncompleteCatalogsHaveConsistentErrors(string json) =>
        Assert.Throws<InvalidDataException>(() => LevelCatalog.Parse(json, Tuning()));

    [Fact]
    public void FileErrorsIdentifyTheCatalogPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "levels-missing.json");
        Assert.Contains(path, Assert.Throws<InvalidDataException>(() => LevelCatalog.Load(path, Tuning())).Message);
    }

    [Fact]
    public void LevelOneMatchesLegacyFieldActorsCameraAndReset()
    {
        var config = Tuning();
        var level = LevelCatalog.Parse(CatalogJson, config).Resolve("level-1");
        var assets = new AssetManager(new AssetConfig());
        using var game = new TackleAlleyGame(config, Input(), assets, level);
        using var previous = new TackleAlleyGame(LegacySetup(), Input(), assets);
        Assert.Same(level, game.CurrentLevel);
        Assert.Equal(Field<FootballField>(previous, "_field").Width, Field<FootballField>(game, "_field").Width);
        Assert.Equal(Field<FootballField>(previous, "_field").GoalLineZ, Field<FootballField>(game, "_field").GoalLineZ);
        Assert.Equal(Field<BallCarrier>(previous, "_player").Position, Field<BallCarrier>(game, "_player").Position);
        Assert.Equal(Field<ThirdPersonCamera>(previous, "_camera").Camera.Position, Field<ThirdPersonCamera>(game, "_camera").Camera.Position);
        var defenders = Field<Opponent[]>(game, "_opponents");
        var oldDefenders = Field<Opponent[]>(previous, "_opponents");
        for (int i = 0; i < defenders.Length; i++)
        {
            Assert.Equal(oldDefenders[i].Profile, defenders[i].Profile);
            Assert.Equal(oldDefenders[i].Position, defenders[i].Position);
            Assert.Equal(oldDefenders[i].CurrentSpeed, defenders[i].CurrentSpeed);
            Assert.Equal(oldDefenders[i].State, defenders[i].State);
        }
        game.ResetRun();
        Assert.Same(level, game.CurrentLevel);
        Assert.Equal(level.Defenders.Select(s => s.Position), defenders.Select(d => d.Position));
        Assert.Equal(0, game.SuccessfulRuns);
    }

    [Fact]
    public void SelectedLevelOverridesLegacyGeometryAndSurvivesReturnerSelectionWithoutMutatingConfig()
    {
        var config = Tuning();
        var json = JsonNode.Parse(CatalogJson)!;
        var authored = json["Levels"]![0]!;
        authored["FieldWidth"] = 18;
        authored["PlayerSpawn"]!["X"] = 2;
        authored["PlayerSpawn"]!["Z"] = -3;
        authored["Defenders"]![0]!["X"] = -2;
        var level = LevelCatalog.Parse(json.ToJsonString(), config).Resolve("level-1");
        // Deliberately unusable legacy setup must not supply the selected level's data.
        config.FieldWidth = 0;
        config.PlayerSpawn = new() { X = 99, Z = 99 };
        config.OpponentSpawns = [];
        using var game = new TackleAlleyGame(config, Input(), new AssetManager(new AssetConfig()), level);
        Assert.Equal(18, Field<FootballField>(game, "_field").Width);
        Assert.Equal(new Vector3(2, 0, -3), Field<BallCarrier>(game, "_player").Position);
        Assert.Equal(-2, Field<Opponent[]>(game, "_opponents")[0].Position.X);
        var returners = ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json"));
        foreach (var returner in returners.Returners)
        {
            game.SelectReturner(returners, returner.Id);
            game.ResetRun();
            Assert.Equal(level.PlayerSpawn, Field<BallCarrier>(game, "_player").Position);
            Assert.Equal(returner.Id, game.SelectedReturnerId);
            Assert.Same(level, game.CurrentLevel);
        }
        Assert.Equal(0, config.FieldWidth);
        Assert.Equal(99, config.PlayerSpawn.X);
        Assert.Empty(config.OpponentSpawns);
        config.FieldAssetWidth = 12;
        Assert.Throws<InvalidDataException>(() => new TackleAlleyGame(config, Input(), new AssetManager(new AssetConfig()), level));
    }

    private static TackleAlleyConfig LegacySetup()
    {
        var config = Tuning();
        config.FieldWidth = 24;
        config.PlayerSpawn = new() { X = 0, Z = 0 };
        // Frozen pre-migration fixture, independent of the authored levels.json.
        config.OpponentSpawns = JsonSerializer.Deserialize<DefenderSpawn[]>(
            """
            [
              {
                "X": -5.5,
                "Z": -18,
                "Profile": {
                  "Name": "Marcus Hill",
                  "JerseyNumber": 24,
                  "Height": 1.65,
                  "Weight": 75,
                  "Build": -1,
                  "Speed": 85,
                  "Acceleration": 90,
                  "Agility": 90,
                  "Strength": 35
                }
              },
              {
                "X": 5.5,
                "Z": -31,
                "Profile": {
                  "Name": "Devon Brooks",
                  "JerseyNumber": 52,
                  "Height": 1.88,
                  "Weight": 108,
                  "Build": 0,
                  "Speed": 60,
                  "Acceleration": 65,
                  "Agility": 60,
                  "Strength": 65
                }
              },
              {
                "X": -4.5,
                "Z": -46,
                "Profile": {
                  "Name": "Andre Cole",
                  "JerseyNumber": 90,
                  "Height": 2.05,
                  "Weight": 145,
                  "Build": 0.6,
                  "Speed": 45,
                  "Acceleration": 40,
                  "Agility": 35,
                  "Strength": 95
                }
              },
              {
                "X": 4.5,
                "Z": -61,
                "Profile": {
                  "Name": "Sam Taylor",
                  "JerseyNumber": 97,
                  "Height": 1.78,
                  "Weight": 150,
                  "Build": 1,
                  "Speed": 30,
                  "Acceleration": 30,
                  "Agility": 25,
                  "Strength": 85
                }
              }
            ]
            """)!;
        return config;
    }
}
