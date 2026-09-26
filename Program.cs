using System.Text.Json;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;
using RaylibTackleAlley.Application;
using RaylibTackleAlley.Game;

string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
TackleAlleyConfig tuning = JsonSerializer.Deserialize<TackleAlleyConfig>(
    File.ReadAllText(configPath)) ?? new TackleAlleyConfig();

// Validate the entire catalog before creating a window or loading native assets.
DefenderProfileCatalog defenderProfiles = DefenderProfileCatalog.Load(
    Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
LevelCatalog levels = LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), tuning, defenderProfiles);

var application = new GameApplication(
    ConfigLoader.Load(configPath),
    InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")),
    MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json")),
    // GameApplication removes missing optional returner exports before AssetManager validates.
    JsonSerializer.Deserialize<AssetConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets.json")))
        ?? throw new InvalidDataException("Asset config must be an object."),
    tuning,
    ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json")),
    levels.Resolve("level-1"));

application.Run();
