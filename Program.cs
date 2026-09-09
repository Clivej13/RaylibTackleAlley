using System.Text.Json;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;
using RaylibTackleAlley.Application;
using RaylibTackleAlley.Game;

TackleAlleyConfig tuning = JsonSerializer.Deserialize<TackleAlleyConfig>(
    File.ReadAllText("config.json")) ?? new TackleAlleyConfig();

var application = new GameApplication(
    ConfigLoader.Load("config.json"),
    InputConfigLoader.Load("input.json"),
    MenuConfigLoader.Load("menu.json"),
    AssetConfigLoader.Load("assets.json"),
    tuning);

application.Run();
