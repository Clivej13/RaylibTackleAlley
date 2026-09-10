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

var application = new GameApplication(
    ConfigLoader.Load(configPath),
    InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")),
    MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json")),
    AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")),
    tuning);

application.Run();
