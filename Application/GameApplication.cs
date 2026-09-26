using System.Text.Json;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;
using RaylibTackleAlley.Game;

namespace RaylibTackleAlley.Application;

public sealed class GameApplication
{
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

    public GameApplication(GameConfig config, InputConfig input, MenuConfig menus, AssetConfig assets, TackleAlleyConfig tuning, ReturnerCatalog returners, LevelDefinition? level = null)
    {
        _config = config;
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
        ConfigFlags flags = _config.VSync ? ConfigFlags.VSyncHint : 0;
        if (_config.Fullscreen) flags |= ConfigFlags.FullscreenMode;
        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(Math.Max(640, _config.WindowWidth), Math.Max(480, _config.WindowHeight), _config.WindowTitle);
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(Math.Max(1, _config.TargetFps));

        try
        {
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
            while (!_assets.ProcessNext())
            {
                // Models must be loaded after the graphics context is initialized.
            }

            _game.InitializeVisuals(_assets);
            _game.ApplyPlayerUniform(_assets, _tuning.OffenseUniform);
            _game.ApplyOpponentUniforms(_assets, _tuning.DefenseUniform);

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
                Update(Raylib.GetFrameTime());
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
        finally
        {
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

    private void HandleMenu(MenuAction? action)
    {
        switch (action?.Function)
        {
            case "SelectReturner" when action.Value is JsonElement { ValueKind: JsonValueKind.String } value:
                _game.SelectReturner(_returners, value.GetString()!);
                _state = GameState.Playing;
                break;
            case "ExitGame": _exitRequested = true; break;
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
        switch (action?.Function)
        {
            case "Resume": _state = GameState.Playing; break;
            case "Restart": _game.ResetRun(); _state = GameState.Playing; break;
            case "MainMenu": _mainMenu.ReturnToStartMenu(); _state = GameState.MainMenu; break;
        }
    }

    private enum GameState { MainMenu, Playing, Paused, Touchdown, GameOver }
}
