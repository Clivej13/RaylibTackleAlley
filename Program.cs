using System.Text.Json;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;
using RaylibTackleAlley.Application;
using RaylibTackleAlley.Game;

using RaylibGameFramework.Logging;

var log = GameDiagnostics.Create();
using var crashLogging = new CrashLogging(log);
try
{
    log.Write("INFO", $"Starting Tackle Alley; runtime={Environment.Version}; OS={Environment.OSVersion}; 64bit={Environment.Is64BitProcess}");
    foreach (var reference in typeof(GameApplication).Assembly.GetReferencedAssemblies())
        log.Write("INFO", $"Assembly: {reference.FullName}");
    string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    TackleAlleyConfig tuning = JsonSerializer.Deserialize<TackleAlleyConfig>(
        File.ReadAllText(configPath)) ?? new TackleAlleyConfig();
    
    // Fail on either role's movement config before creating a window or loading native assets.
    tuning.ValidateMovementScaling();
    
    // Validate the entire catalog before creating a window or loading native assets.
    DefenderProfileCatalog defenderProfiles = DefenderProfileCatalog.Load(
        Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
    LevelCatalog levels = LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), tuning, defenderProfiles);
    
    var windowConfig = ConfigLoader.Load(configPath);
    var inputConfig = InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json"));
    var settings = new UserSettingsSession(UserSettingsSession.DefaultPath, log);
    settings.Load(windowConfig, tuning, inputConfig);
    
    var application = new GameApplication(
        windowConfig,
        inputConfig,
        MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json")),
        // GameApplication removes missing optional returner exports before AssetManager validates.
        JsonSerializer.Deserialize<AssetConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets.json")))
            ?? throw new InvalidDataException("Asset config must be an object."),
        tuning,
        ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json")),
        levels.Resolve("level-1"), settings, log);
    
    application.Run();
    
    log.Write("INFO", "Clean shutdown");
}
catch (Exception exception)
{
    log.Write("FATAL", "Game terminated by managed exception", exception);
    Environment.ExitCode = 1;
}
