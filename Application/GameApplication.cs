using System.Text.Json;
using RaylibGameFramework.Logging;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;
using RaylibTackleAlley.Game;

namespace RaylibTackleAlley.Application;

public sealed class GameApplication
{
    private readonly RotatingFileLogger? _log;
    private readonly UserSettingsSession? _settings;
    private readonly MenuItemDefinition? _settingsStatus;
    private readonly GameConfig _config;
    private readonly ReturnerCatalog _returners;
    private readonly TackleAlleyConfig _tuning;
    private readonly InputController _input;
    private readonly InputConfig _inputConfig;
    private readonly MenuManager _mainMenu;
    private readonly MenuManager _pauseMenu;
    private readonly AssetManager _assets;
    private readonly TackleAlleyGame _game;
    private readonly ReturnerPreview _returnerPreview;
    private readonly ReturnerSelectionView _returnerSelection;
    private GameState _state = GameState.MainMenu;
    private bool _exitRequested;
    private bool _mouseCaptured;

    public GameApplication(GameConfig config, InputConfig input, MenuConfig menus, AssetConfig assets, TackleAlleyConfig tuning, ReturnerCatalog returners, LevelDefinition? level = null, UserSettingsSession? settings = null, RotatingFileLogger? log = null)
    {
        _log = log;
        _log?.Write("INFO", "Constructing game and validating assets");
        _config = config;
        _settings = settings;
        _settingsStatus = menus.Menus["Options"].Items.FirstOrDefault(i => i.Function == "SettingsStatus");
        assets = returners.PrepareAssets(assets, tuning.OffenseUniform);
        _returners = returners = returners.ResolveAvailableAssets(assets, tuning.OffenseUniform);
        _tuning = tuning;
        _input = new InputController(input);
        _inputConfig = input;
        foreach (MenuItemDefinition item in menus.Menus["Options"].Items)
        {
            if (item.Function == "SetFullscreen")
                item.Value = JsonSerializer.SerializeToElement(config.Fullscreen);
            else if (item.Function == "SetVSync")
                item.Value = JsonSerializer.SerializeToElement(config.VSync);
            else if (item.Function == "SetDebug")
                item.Value = JsonSerializer.SerializeToElement(tuning.DrawGameplayDebug);
        }
        menus.Menus["Returners"].Items.Clear();
        foreach (var entry in returners.Returners)
            menus.Menus["Returners"].Items.Add(new MenuItemDefinition
            {
                Type = "Button",
                Text = $"#{entry.Profile.JerseyNumber} {entry.Profile.Name}",
                Function = "SelectReturner",
                Value = JsonSerializer.SerializeToElement(entry.Id)
            });
        menus.Menus["Returners"].Items.Add(new MenuItemDefinition
        {
            Type = "Button", Text = "Back", Function = "Back"
        });
        _mainMenu = new MenuManager(menus, _input, input);
        _pauseMenu = new MenuManager(new MenuConfig { StartMenu = "Pause", Menus = menus.Menus }, _input, input);
        _assets = new AssetManager(assets);
        _returnerPreview = new ReturnerPreview(_assets, tuning.OffenseUniform);
        _returnerSelection = new(returners, menus.Menus["Returners"], _returnerPreview);
        _game = level is null ? new TackleAlleyGame(tuning, _input, _assets)
            : new TackleAlleyGame(tuning, _input, _assets, level);
    }

    public void Run()
    {
        _log?.Write("INFO", $"Creating window {_config.WindowWidth}x{_config.WindowHeight}; fullscreen={_config.Fullscreen}; VSync={_config.VSync}");
        ConfigFlags flags = _config.VSync ? ConfigFlags.VSyncHint : 0;
        if (_config.Fullscreen) flags |= ConfigFlags.FullscreenMode;
        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(Math.Max(640, _config.WindowWidth), Math.Max(480, _config.WindowHeight), _config.WindowTitle);
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(Math.Max(1, _config.TargetFps));

        try
        {
            _log?.Write("INFO", "Queuing required assets");
            _assets.RequireAssets("FootballField", "Stadium", "Football", "FootballPlayer", "FootballPlayerAnimations", "FootballPlayerRunAnimations", "FootballPlayerSprintAnimations",
                "FootballPlayerCarryJogAnimations", "FootballPlayerCarryRunAnimations", "FootballPlayerCarrySprintAnimations",
                "FootballPlayerCutLeftAnimations", "FootballPlayerCutRightAnimations",
                "FootballPlayerJukeLeftAnimations", "FootballPlayerJukeRightAnimations",
                "FootballPlayerSpinLeftAnimations", "FootballPlayerSpinRightAnimations");
            _assets.RequireAssets(Opponent.AnimationAssetKeys);
            _assets.RequireAssets(_tuning.OffenseUniform, _tuning.DefenseUniform);
            foreach (var uniform in _returners.Returners.Select(r => r.Uniform).OfType<string>().Distinct())
                _assets.RequireAsset(uniform);
            foreach (var taunt in _returners.Returners.Select(r => r.Taunt).OfType<string>().Distinct())
                _assets.RequireAsset(taunt);
            _log?.Write("INFO", "Loading asset queue");
            while (!_assets.ProcessNext())
            {
                // Models must be loaded after the graphics context is initialized.
            }

            _log?.Write("INFO", "Assets loaded; initializing player visuals");
            _game.InitializeVisuals(_assets);
            _game.ApplyPlayerUniform(_assets, _tuning.OffenseUniform);
            _game.ApplyOpponentUniforms(_assets, _tuning.DefenseUniform);

            _log?.Write("INFO", "Visuals ready; entering main loop");
            while (!_exitRequested && !Raylib.WindowShouldClose())
            {
                bool captureMouse = _state == GameState.Playing && Raylib.IsWindowFocused();
                if (captureMouse != _mouseCaptured)
                {
                    if (captureMouse) Raylib.DisableCursor();
                    else Raylib.EnableCursor();
                    _mouseCaptured = captureMouse;
                    _game.IgnoreNextMouseDelta();
                }
                _input.Update();
                var previousState = _state;
                string previousMenu = _mainMenu.CurrentMenuName;
                Update(Raylib.GetFrameTime());
                if (_state != previousState || _mainMenu.CurrentMenuName != previousMenu)
                    _log?.Write("INFO", $"State {previousState} -> {_state}; menu={_mainMenu.CurrentMenuName}; returner={_game.SelectedReturnerId}");
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(12, 22, 32, 255));
                if (_state is GameState.Playing or GameState.Touchdown or GameState.GameOver)
                    _game.Draw();
                else if (_state == GameState.Paused)
                {
                    _game.Draw();
                    MenuDisplay.Draw(_pauseMenu, _inputConfig);
                }
                else
                {
                    if (_mainMenu.CurrentMenuName != "Returners")
                        MenuDisplay.Draw(_mainMenu, _inputConfig);
                    DrawReturnerDetail();
                }
                Raylib.EndDrawing();
            }
        }
        catch (Exception exception)
        {
            _log?.Write("FATAL", $"Game loop failed; state={_state}; menu={_mainMenu.CurrentMenuName}; returner={_game.SelectedReturnerId}", exception);
            throw;
        }
        finally
        {
            _log?.Write("INFO", "Releasing game resources");
            SaveSettings();
            _returnerPreview.Dispose();
            _game.Dispose();
            _assets.UnloadAll();
            Raylib.CloseWindow();
        }
    }

    private void DrawReturnerDetail() => _returnerSelection.Draw(_mainMenu);

    private void Update(float deltaTime)
    {
        switch (_state)
        {
            case GameState.MainMenu:
                HandleMenu(_returnerSelection.Update(_mainMenu));
                SaveSettings();
                break;
            case GameState.Playing:
                if (_input.WasPressed("Pause")) { _pauseMenu.ReturnToStartMenu(); _state = GameState.Paused; break; }
                _game.Update(deltaTime);
                if (_game.GameOver) _state = GameState.GameOver;
                else if (_game.Touchdown) _state = GameState.Touchdown;
                break;
            case GameState.Touchdown:
            case GameState.GameOver:
                if (_input.WasPressed("Pause") || _input.WasPressed("MenuConfirm") || (_game.EndStateElapsed >= _tuning.AutoRestartDelay && _game.OutcomeCelebrationComplete))
                {
                    _log?.Write("INFO", "Resetting run after outcome");
                    _game.ResetRun();
                    _state = GameState.Playing;
                }
                else _game.Update(deltaTime);
                break;
            case GameState.Paused:
                if (_input.WasPressed("Pause")) { _state = GameState.Playing; break; }
                HandlePauseMenu(_pauseMenu.Update());
                break;
        }
    }

    private void SaveSettings()
    {
        _settings?.SaveIfChanged(_config, _tuning, _inputConfig);
        if (_settingsStatus is not null)
            _settingsStatus.Text = _settings?.Error ?? "Settings are saved automatically.";
    }

    private void HandleMenu(MenuAction? action)
    {
        if (action is not null) _log?.Write("INFO", $"Menu action: {action.Function}; value={action.Value}");
        switch (action?.Function)
        {
            case "SelectReturner" when action.Value is JsonElement { ValueKind: JsonValueKind.String } value:
                _game.SelectReturner(_returners, value.GetString()!);
                _state = GameState.Playing;
                break;
            case "ExitGame": _exitRequested = true; break;
            case "SetDebug" when action.Value is bool debug:
                _tuning.DrawGameplayDebug = debug;
                _game.SetDebugEnabled(debug);
                break;
            case "SetFullscreen" when action.Value is bool fullscreen:
                if (Raylib.IsWindowFullscreen() != fullscreen) Raylib.ToggleFullscreen();
                _config.Fullscreen = fullscreen;
                break;
            case "SetVSync" when action.Value is bool vsync:
                if (vsync) Raylib.SetWindowState(ConfigFlags.VSyncHint);
                else Raylib.ClearWindowState(ConfigFlags.VSyncHint);
                _config.VSync = vsync;
                break;
        }
    }

    private void HandlePauseMenu(MenuAction? action)
    {
        if (action is not null) _log?.Write("INFO", $"Menu action: {action.Function}; value={action.Value}");
        switch (action?.Function)
        {
            case "Resume": _state = GameState.Playing; break;
            case "Restart": _game.ResetRun(); _state = GameState.Playing; break;
            case "MainMenu": _mainMenu.ReturnToStartMenu(); _state = GameState.MainMenu; break;
        }
    }

    private enum GameState { MainMenu, Playing, Paused, Touchdown, GameOver }
}
