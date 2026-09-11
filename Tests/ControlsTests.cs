using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;
using RaylibTackleAlley.Application;
using RaylibTackleAlley.Game;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class ControlsTests : IDisposable
{
    private readonly InputConfig _bindings = InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json"));
    private readonly TackleAlleyConfig _config = new();
    private readonly InputController _input;
    private readonly BallCarrier _player;
    private readonly FootballField _field;
    private int _mouseX = 640;
    private int _mouseY = 360;
    private const float Dt = 1f / 60f;

    public ControlsTests()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(1280, 720, "Tackle Alley controls validation");
        Assert.True(Raylib.IsWindowReady());
        Raylib.SetExitKey(KeyboardKey.Null);
        _input = new InputController(_bindings);
        _player = new BallCarrier(_config);
        _field = new FootballField(_config, new AssetManager(new AssetConfig()));
        Tick();
    }

    public void Dispose() => Raylib.CloseWindow();

    // Raylib's native automation event IDs: keyboard up/down, mouse position,
    // gamepad connect/button up/button down/axis motion. These exercise the packages,
    // not a replacement input implementation. Axis values use signed 32768 units.
    private static unsafe void Event(uint type, int a = 0, int b = 0, int c = 0)
    {
        AutomationEvent e = new() { Type = type };
        e.Params[0] = a; e.Params[1] = b; e.Params[2] = c;
        Raylib.PlayAutomationEvent(e);
    }

    private void Tick(int dx = 0, int dy = 0, float dt = Dt)
    {
        _mouseX += dx; _mouseY += dy;
        Event(7, _mouseX, _mouseY);
        _input.Update();
        _player.Update(_input, dt, _field);
        EndFrame();
    }

    private static void EndFrame()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        Raylib.EndDrawing();
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static object? Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static string BindingDisplayName(InputBinding? binding)
    {
        Type display = typeof(GameApplication).Assembly.GetType("RaylibTackleAlley.Application.MenuDisplay")!;
        return (string)display.GetMethod("GetBindingDisplayName", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [binding])!;
    }

    private void DrawMenu(MenuManager manager)
    {
        Type display = typeof(GameApplication).Assembly.GetType("RaylibTackleAlley.Application.MenuDisplay")!;
        display.GetMethod("Draw")!.Invoke(null, [manager, _bindings]);
    }

    private static void Axis(GamepadAxis axis, float value)
    {
        Event(9, 0);
        Event(13, 0, (int)axis, (int)(value * 32768));
        Assert.Equal(value, Raylib.GetGamepadAxisMovement(0, axis), 3);
    }

    [Theory]
    [InlineData(KeyboardKey.A, -1, 2)]
    [InlineData(KeyboardKey.D, 1, 2)]
    [InlineData(KeyboardKey.W, 0, 2)]
    [InlineData(KeyboardKey.S, 0, 1)]
    [InlineData(KeyboardKey.LeftShift, 0, 3)]
    [InlineData(KeyboardKey.Left, 0, 2)]
    public void KeyboardMovementRemainsAnAutoRunner(KeyboardKey key, int direction, int tier)
    {
        Vector3 before = _player.Position;
        float speedBefore = _player.CurrentForwardSpeed;
        Event(2, (int)key);
        Tick();
        Assert.Equal(direction * _config.PlayerLateralSpeed * Dt, _player.Position.X - before.X, 4);
        Assert.Equal(tier, _player.SpeedTier);
        Assert.Equal(-(speedBefore + _player.CurrentForwardSpeed) * 0.5f * Dt, _player.Position.Z - before.Z, 4);
        if (key == KeyboardKey.W)
            Assert.True(Field<Vector3>(_player, "_bodyOffset").Z < -0.1f);
        if (key == KeyboardKey.Left)
            Assert.True(_input.WasPressed("MenuLeft"));
        Event(1, (int)key);
    }

    [Fact]
    public void ForwardSpeedRampsWithoutDelayingSteeringAndResetRestoresRun()
    {
        _player.Reset();
        Event(2, (int)KeyboardKey.LeftShift);
        Event(2, (int)KeyboardKey.D);
        Tick(dt: 0f);
        Assert.Equal(3, _player.SpeedTier);
        Assert.Equal("CarrySprint", _player.AnimationName);
        Assert.Equal(11.5f, _player.TargetForwardSpeed);
        Assert.Equal(9f, _player.CurrentForwardSpeed);
        Tick(dt: 0.1f);
        Assert.InRange(_player.CurrentForwardSpeed, 9.1f, 11.4f);
        Assert.Equal(0.8f, _player.Position.X, 5);
        Tick(dt: 0.2f);
        Assert.Equal(11.5f, _player.CurrentForwardSpeed, 4);
        Event(1, (int)KeyboardKey.LeftShift);
        Tick(dt: 0f);
        Assert.Equal(9f, _player.TargetForwardSpeed);
        Assert.Equal("CarryRun", _player.AnimationName);
        Tick(dt: 0.1f);
        Assert.InRange(_player.CurrentForwardSpeed, 9.1f, 11.4f);
        Tick(dt: 0.1f);
        Assert.Equal(9f, _player.CurrentForwardSpeed, 4);
        Event(2, (int)KeyboardKey.S);
        Tick(dt: 0.2f);
        Assert.Equal(6.5f, _player.TargetForwardSpeed);
        Assert.Equal(6.5f, _player.CurrentForwardSpeed, 4);
        Assert.Equal("CarryJog", _player.AnimationName);
        _player.Reset();
        Assert.Equal(2, _player.SpeedTier);
        Assert.Equal(9f, _player.CurrentForwardSpeed);
        Assert.Equal("CarryRun", _player.AnimationName);
        Event(1, (int)KeyboardKey.D);
        Event(1, (int)KeyboardKey.S);
    }

    [Theory]
    [InlineData(KeyboardKey.LeftShift)]
    [InlineData(KeyboardKey.S)]
    public void ForwardRampSpeedAndDistanceAreFrameRateIndependent(KeyboardKey key)
    {
        Event(2, (int)key);
        _player.Reset();
        Tick(dt: 0.4f);
        float speed = _player.CurrentForwardSpeed;
        Vector3 position = _player.Position;
        _player.Reset();
        for (int i = 0; i < 24; i++) Tick(dt: 1f / 60f);
        Assert.Equal(speed, _player.CurrentForwardSpeed, 4);
        Assert.Equal(position.Z, _player.Position.Z, 4);
        Event(1, (int)key);
    }

    [Fact]
    public void GameCameraUsesPhysicalSpeedAndResetDiscardsOldZoom()
    {
        var game = new TackleAlleyGame(_config, _input, new AssetManager(new AssetConfig()));
        var player = Field<BallCarrier>(game, "_player");
        var camera = Field<ThirdPersonCamera>(game, "_camera");
        Event(2, (int)KeyboardKey.LeftShift);
        _input.Update();
        game.Update(0f);
        Assert.Equal(3, player.SpeedTier);
        Assert.Equal(9f, player.CurrentForwardSpeed);
        Assert.Equal(6.75f, camera.Camera.Position.Z);
        game.Update(0.1f);
        float desiredZ = player.Position.Z + camera.DistanceForSpeed(player.CurrentForwardSpeed);
        float expectedZ = 6.75f + (desiredZ - 6.75f) * (1f - MathF.Exp(-1f));
        Assert.Equal(expectedZ, camera.Camera.Position.Z, 5);
        game.ResetRun();
        Assert.Equal(9f, player.CurrentForwardSpeed);
        Assert.Equal(new Vector3(0f, 5.5f, 6.75f), camera.Camera.Position);
        Event(1, (int)KeyboardKey.LeftShift);
    }

    private float VisualYaw => (float)typeof(BallCarrier)
        .GetProperty("VisualYawDegrees", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_player)!;

    [Theory]
    [InlineData(KeyboardKey.W, 0f)]
    [InlineData(KeyboardKey.A, -30f)]
    [InlineData(KeyboardKey.D, 30f)]
    public void RunningYawUsesMovementInputAndTurnsTowardTravel(KeyboardKey key, float target)
    {
        _player.Reset();
        Event(2, (int)key);
        Tick();
        Assert.Equal(target, Field<float>(_player, "_targetRunYaw"));
        float yaw = Field<float>(_player, "_currentRunYaw");
        if (target == 0f) Assert.Equal(0f, yaw);
        else
        {
            Assert.Equal(Math.Sign(target), Math.Sign(yaw));
            Assert.InRange(Math.Abs(yaw), 0.01f, Math.Abs(target) - 0.01f);
        }
        Vector3 facing = Vector3.Transform(-Vector3.UnitZ,
            Matrix4x4.CreateRotationY(VisualYaw * MathF.PI / 180f));
        Assert.Equal(Math.Sign(target), Math.Sign(facing.X));
        Assert.True(facing.Z < 0f);
        Event(1, (int)key);
        Tick();
        Assert.Equal(0f, Field<float>(_player, "_targetRunYaw"));
        Assert.True(Math.Abs(Field<float>(_player, "_currentRunYaw")) <= Math.Abs(yaw));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(-0.5f)]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void ControllerYawUsesTheSameAnalogValueAsLateralMovement(float axis)
    {
        _player.Reset();
        Axis(GamepadAxis.LeftX, axis);
        Tick();
        float lateral = _player.Position.X / (_config.PlayerLateralSpeed * Dt);
        Assert.Equal(lateral * 30f, Field<float>(_player, "_targetRunYaw"), 4);
        Assert.Equal(2, _player.SpeedTier);
        Assert.Equal(-_config.PlayerForwardSpeed * Dt, _player.Position.Z, 4);
    }

    [Fact]
    public void RunningYawIsFrameRateIndependentAndReversesSmoothly()
    {
        _player.Reset();
        Event(2, (int)KeyboardKey.D);
        Tick(dt: 0.1f);
        float singleStep = Field<float>(_player, "_currentRunYaw");
        _player.Reset();
        for (int i = 0; i < 10; i++) Tick(dt: 0.01f);
        Assert.Equal(singleStep, Field<float>(_player, "_currentRunYaw"), 4);
        Event(1, (int)KeyboardKey.D);
        Event(2, (int)KeyboardKey.A);
        Tick(dt: 0f);
        Assert.Equal(-30f, Field<float>(_player, "_targetRunYaw"));
        Assert.Equal(singleStep, Field<float>(_player, "_currentRunYaw"), 4);
        Tick();
        Assert.InRange(Field<float>(_player, "_currentRunYaw"), -29.99f, singleStep - 0.01f);
        Event(1, (int)KeyboardKey.A);
    }

    [Theory]
    [InlineData(-20, -1)]
    [InlineData(20, 1)]
    public void SpinAddsItsOwnRotationAndResetClearsBothYaws(int mouseDelta, int spinDirection)
    {
        _player.Reset();
        Event(2, (int)KeyboardKey.D);
        Tick();
        Tick(0, 20);
        Vector3 before = _player.Position;
        Tick(mouseDelta);
        Assert.Equal(spinDirection, Field<int>(_player, "_spinDirection"));
        Assert.Equal(0.45f - Dt, Field<float>(_player, "_spinRemaining"), 4);
        Assert.Equal(spinDirection * 10f * Dt, _player.Position.X - before.X, 4);
        Assert.Equal(-_player.Speed * Dt, _player.Position.Z - before.Z, 4);
        float runYaw = Field<float>(_player, "_currentRunYaw");
        Assert.Equal(30f, Field<float>(_player, "_targetRunYaw"));
        Assert.Equal(-runYaw + spinDirection * 360f * (Dt / 0.45f), VisualYaw, 3);
        Event(1, (int)KeyboardKey.D);
        Event(2, (int)KeyboardKey.A);
        Tick(dt: 0.2f);
        Assert.Equal(-30f, Field<float>(_player, "_targetRunYaw"));
        Assert.True(Field<float>(_player, "_spinRemaining") > 0f);
        Tick(dt: 0.3f);
        Assert.Equal(0f, Field<float>(_player, "_spinRemaining"));
        Assert.Equal(-Field<float>(_player, "_currentRunYaw"), VisualYaw);
        Tick(0, 20); Tick(mouseDelta);
        Assert.True(Field<float>(_player, "_spinRemaining") > 0f);
        _player.Reset();
        Assert.Equal(0f, Field<float>(_player, "_currentRunYaw"));
        Assert.Equal(0f, Field<float>(_player, "_targetRunYaw"));
        Assert.Equal(0f, VisualYaw);
        Assert.Equal(Vector3.Zero, _player.Position);
        Assert.Equal(2, _player.SpeedTier);
        Event(1, (int)KeyboardKey.A);
    }

    [Fact]
    public void EndZoneTravelSmoothlyFacesDownfieldWithoutChangingMovement()
    {
        Event(2, (int)KeyboardKey.D);
        Tick(dt: 0.1f);
        Event(1, (int)KeyboardKey.D);
        float yaw = Field<float>(_player, "_currentRunYaw");
        Vector3 before = _player.Position;
        _player.RunIntoEndZone(Dt, -100f);
        Assert.Equal(0f, Field<float>(_player, "_targetRunYaw"));
        Assert.InRange(Field<float>(_player, "_currentRunYaw"), 0.01f, yaw - 0.01f);
        Assert.Equal(before.X, _player.Position.X);
        Assert.Equal(before.Y, _player.Position.Y);
        Assert.Equal(before.Z - _player.Speed * Dt, _player.Position.Z, 4);
    }

    [Theory]
    [InlineData(-20, -1)]
    [InlineData(20, 1)]
    public void MouseJukesUseNormalizedDirections(int delta, int direction)
    {
        Tick(delta);
        Assert.Equal(direction, Field<int>(_player, "_jukeDirection"));
        Assert.Equal(_config.PlayerJukeDuration - Dt, Field<float>(_player, "_jukeRemaining"), 4);
        Assert.Equal(direction * _config.PlayerJukeSpeed * Dt, _player.Position.X, 4);
    }

    [Fact]
    public void MouseNoiseCannotTriggerAJukeOrHideControllerInput()
    {
        Tick(2);
        Assert.Equal(0f, Field<float>(_player, "_jukeRemaining"));
        Axis(GamepadAxis.RightX, -0.9f);
        Tick(1);
        Assert.Equal(-1, Field<int>(_player, "_jukeDirection"));
    }

    [Theory]
    [InlineData(-20, -1)]
    [InlineData(20, 1)]
    public void MouseBackThenSideSpinsAcrossIdleFrames(int delta, int direction)
    {
        Tick(0, 20);
        Tick(dt: 0.1f);
        Tick(delta);
        Assert.Equal(direction, Field<int>(_player, "_spinDirection"));
        Assert.Equal(0.45f - Dt, Field<float>(_player, "_spinRemaining"), 4);
        Assert.Equal(0f, Field<float>(_player, "_jukeRemaining"));
    }

    [Fact]
    public void MouseSpinWindowStillExpires()
    {
        Tick(0, 20);
        Tick(dt: 0.41f);
        Tick(-20);
        Assert.Equal(0f, Field<float>(_player, "_spinRemaining"));
        Assert.Equal(-1, Field<int>(_player, "_jukeDirection"));
    }

    [Theory]
    [InlineData(-0.9f, -1)]
    [InlineData(0.9f, 1)]
    public void ControllerBackRollAndJukeRetainTheirTiming(float side, int direction)
    {
        Axis(GamepadAxis.RightY, 0.9f); Tick();
        Axis(GamepadAxis.RightY, 0.6f);
        Axis(GamepadAxis.RightX, side); Tick();
        Assert.Equal(direction, Field<int>(_player, "_spinDirection"));
        Assert.Equal(0.45f - Dt, Field<float>(_player, "_spinRemaining"), 4);
        _player.Reset();
        Axis(GamepadAxis.RightY, 0f); Axis(GamepadAxis.RightX, 0f); Tick();
        Axis(GamepadAxis.RightX, side); Tick();
        Assert.Equal(direction, Field<int>(_player, "_jukeDirection"));
        Assert.Equal(_config.PlayerJukeDuration - Dt, Field<float>(_player, "_jukeRemaining"), 4);
    }

    [Fact]
    public void ControllerNeutralStillCancelsAPendingSpin()
    {
        Axis(GamepadAxis.RightY, 0.9f); Tick();
        Axis(GamepadAxis.RightY, 0f); Tick();
        Axis(GamepadAxis.RightX, -0.9f); Tick();
        Assert.Equal(0f, Field<float>(_player, "_spinRemaining"));
        Assert.Equal(-1, Field<int>(_player, "_jukeDirection"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SprintHeadFakeCancelsMovesAndDoesNotLeanTheTorso(bool gamepad)
    {
        Tick(-20);
        Assert.True(Field<float>(_player, "_jukeRemaining") > 0f);
        if (gamepad)
        {
            Event(9, 0); Event(12, 0, (int)GamepadButton.RightTrigger2);
            Axis(GamepadAxis.RightX, 0.9f);
            Tick();
        }
        else
        {
            Event(2, (int)KeyboardKey.LeftShift);
            Tick(20);
        }
        Assert.Equal(3, _player.SpeedTier);
        Assert.Equal(0f, Field<float>(_player, "_jukeRemaining"));
        Assert.Equal(0f, Field<float>(_player, "_spinRemaining"));
        // Existing juke lean blends out; the head moves right independently of the torso.
        Assert.True(Field<Vector3>(_player, "_headOffset").X > Field<Vector3>(_player, "_bodyOffset").X * 2f);
        if (gamepad) Event(11, 0, (int)GamepadButton.RightTrigger2);
        else Event(1, (int)KeyboardKey.LeftShift);
    }

    [Fact]
    public void RebindingTheSharedDirectionUpdatesGameplay()
    {
        _input.ApplyRebind(new InputRebindResult("RightStickLeft", "Keyboard", "Q"));
        Event(2, (int)KeyboardKey.Q); Tick();
        Assert.Equal(-1, Field<int>(_player, "_jukeDirection"));
        Event(1, (int)KeyboardKey.Q);
        _player.Reset(); Tick();
        _input.ApplyRebind(new InputRebindResult("RightStickLeft", "MouseAxis", "MouseXNegative"));
        Tick(-20);
        Assert.Equal(-1, Field<int>(_player, "_jukeDirection"));
    }

    [Fact]
    public void ControlsRowsLoadWithFriendlyNamesAndDescriptions()
    {
        MenuConfig menus = MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json"));
        var rows = menus.Menus["Controls"].Items;
        Assert.Equal(8, rows.Count(r => r.Type == "KeyBind"));
        Assert.Equal(3, rows.Count(r => r.Type == "ControlDescription"));
        string[] names = ["Move Left", "Move Right", "Move Forward", "Move Backward", "Sprint", "Juke Left", "Juke Right", "Pause"];
        string[] keyboard = ["A", "D", "W", "S", "Left Shift", "Mouse Left", "Mouse Right", "Escape"];
        string[] controller = ["Left Stick Left", "Left Stick Right", "Left Stick Up", "Left Stick Down", "RT", "Right Stick Left", "Right Stick Right", "B"];
        var rebindable = rows.Where(r => r.Type == "KeyBind").ToArray();
        for (int i = 0; i < names.Length; i++)
        {
            Assert.Equal(names[i], rebindable[i].Text);
            Assert.Equal(keyboard[i], BindingDisplayName(_input.GetBinding(rebindable[i].Action!, InputDeviceFamily.KeyboardMouse)));
            Assert.Equal(controller[i], BindingDisplayName(_input.GetBinding(rebindable[i].Action!, InputDeviceFamily.Gamepad)));
            Assert.Equal(2, _bindings.Bindings.Count(b => b.Action == rebindable[i].Action));
        }
        Assert.Equal(new[] { "Mouse Down -> Left", "Mouse Down -> Right", "Shift + Mouse" },
            rows.Where(r => r.Type == "ControlDescription").Select(r => r.KeyboardMouseDescription));
        foreach (var row in rows.Where(r => r.Type == "ControlDescription"))
        {
            Assert.True(string.IsNullOrEmpty(row.Action));
            Assert.True(string.IsNullOrEmpty(row.Function));
        }
    }

    [Fact]
    public void ControlsRenderScrollAndKeepDescriptionsOutOfRebindMode()
    {
        MenuConfig menus = MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json"));
        menus.StartMenu = "Controls";
        var manager = new MenuManager(menus, _input, _bindings);
        foreach (var row in menus.Menus["Controls"].Items.Where(r => r.Type == "ControlDescription"))
        {
            Invoke(manager, "ActivateItem", row, 1);
            Assert.False(_input.IsRebinding);
        }
        // Track scrolling during navigation, including when selection wraps to the top.
        float maxScroll = 0f;
        var visited = new HashSet<int>();
        for (int i = 0; i < 13; i++)
        {
            Event(2, (int)KeyboardKey.Down); _input.Update(); manager.Update();
            Raylib.BeginDrawing(); Raylib.ClearBackground(Color.Black); DrawMenu(manager); Raylib.EndDrawing();
            maxScroll = Math.Max(maxScroll, Field<float>(manager, "_scrollOffset"));
            visited.Add(Field<int>(manager, "_selectedIndex"));
            Assert.Equal("LeftShift", _input.GetBinding("Sprint", InputDeviceFamily.KeyboardMouse)!.Input);
            Event(1, (int)KeyboardKey.Down); _input.Update(); manager.Update(); EndFrame();
        }
        Assert.True(maxScroll > 0f);
        Assert.Contains(12, visited);
        manager.ReturnToStartMenu();
        Event(2, (int)KeyboardKey.Enter); _input.Update(); manager.Update();
        Assert.True(_input.IsRebinding);
        _input.CancelRebind(); Event(1, (int)KeyboardKey.Enter);
        _input.Update(); manager.Update(); EndFrame();
        // Native controller A enters the normal row's gamepad rebind mode.
        manager.ReturnToStartMenu();
        Event(9, 0); Event(12, 0, (int)GamepadButton.RightFaceDown);
        _input.Update(); manager.Update();
        Assert.True(_input.IsRebinding);
        Assert.Equal(InputDeviceFamily.Gamepad, _input.RebindingDeviceFamily);
        _input.CancelRebind();
    }

    [Fact]
    public void ControllerDpadNavigatesWithoutMovingTheCarrier()
    {
        MenuConfig menus = MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json"));
        menus.StartMenu = "Controls";
        var manager = new MenuManager(menus, _input, _bindings);
        Event(9, 0); Event(12, 0, (int)GamepadButton.LeftFaceDown);
        _input.Update(); manager.Update();
        Assert.Equal(1, Field<int>(manager, "_selectedIndex"));
        Assert.False(_input.IsDown("MoveBackward"));
        Event(11, 0, (int)GamepadButton.LeftFaceDown);
        _input.Update(); manager.Update(); EndFrame();
        Axis(GamepadAxis.LeftY, 0.9f);
        _input.Update(); manager.Update();
        Assert.Equal(2, Field<int>(manager, "_selectedIndex"));
    }

    [Fact]
    public void MouseAxisRebindCaptureIsAppliedByTheControlsMenu()
    {
        MenuConfig menus = MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json"));
        menus.StartMenu = "Controls";
        var manager = new MenuManager(menus, _input, _bindings);
        _input.BeginRebind("RightStickLeft", InputDeviceFamily.KeyboardMouse);
        Event(7, _mouseX - 30, _mouseY);
        _input.Update();
        Assert.Equal(new InputRebindResult("RightStickLeft", "MouseAxis", "MouseXNegative"), _input.CompletedRebind);
        manager.Update();
        Assert.False(_input.IsRebinding);
        Assert.Equal("MouseXNegative", _input.GetBinding("RightStickLeft", InputDeviceFamily.KeyboardMouse)!.Input);
    }

    [Fact]
    public void HeadFakeCancelsSpinAndRequiresNeutralBeforeAnotherMove()
    {
        Tick(0, 20); Tick(-20);
        Assert.True(Field<float>(_player, "_spinRemaining") > 0f);
        Event(2, (int)KeyboardKey.LeftShift); Tick(20);
        Assert.Equal(0f, Field<float>(_player, "_spinRemaining"));
        Event(1, (int)KeyboardKey.LeftShift); Tick(20);
        Assert.Equal(0f, Field<float>(_player, "_jukeRemaining"));
        Tick(); Tick(20);
        Assert.True(Field<float>(_player, "_jukeRemaining") > 0f);
    }

    [Fact]
    public void CursorTransitionDiscardsOnlyTheFirstMouseSample()
    {
        _player.IgnoreNextMouseDelta();
        Tick(100);
        Assert.Equal(0f, Field<float>(_player, "_jukeRemaining"));
        Tick(-20);
        Assert.Equal(-1, Field<int>(_player, "_jukeDirection"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PauseAndBackBindingsRemainAvailable(bool gamepad)
    {
        if (gamepad) { Event(9, 0); Event(12, 0, (int)GamepadButton.RightFaceRight); }
        else Event(2, (int)KeyboardKey.Escape);
        _input.Update();
        Assert.True(_input.WasPressed("Pause"));
        Assert.True(_input.WasPressed("MenuBack"));
    }

    [Theory]
    [InlineData("Touchdown")]
    [InlineData("GameOver")]
    public void EndStatesRestartAfterTwoAndAHalfSeconds(string state)
    {
        var menus = MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json"));
        var app = new GameApplication(new GameConfig(), _bindings, menus, new AssetConfig(), _config);
        var game = Field<TackleAlleyGame>(app, "_game");
        var stateField = typeof(GameApplication).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!;
        stateField.SetValue(app, Enum.Parse(stateField.FieldType, state));
        typeof(TackleAlleyGame).GetProperty(state)!.SetValue(game, true);
        typeof(TackleAlleyGame).GetProperty("EndStateElapsed")!.SetValue(game, 2.49f);
        Invoke(app, "Update", 0.02f);
        Assert.Equal(state, stateField.GetValue(app)!.ToString());
        Invoke(app, "Update", Dt);
        Assert.Equal("Playing", stateField.GetValue(app)!.ToString());
        Assert.False(game.Touchdown);
        Assert.False(game.GameOver);
        Assert.Equal(0f, game.EndStateElapsed);
    }
}

public sealed class MouseNormalizationTests
{
    [Theory]
    [InlineData(-100f, 0.05f, -1f)]
    [InlineData(-13f, 0.05f, -0.65f)]
    [InlineData(-2f, 0.05f, 0f)]
    [InlineData(0f, 0.05f, 0f)]
    [InlineData(2f, 0.05f, 0f)]
    [InlineData(12f, 0.05f, 0.6f)]
    [InlineData(13f, 0.05f, 0.65f)]
    [InlineData(20f, 0.05f, 1f)]
    [InlineData(100f, 0.05f, 1f)]
    [InlineData(10f, 0.1f, 1f)]
    [InlineData(20f, -1f, 0f)]
    [InlineData(float.NaN, 0.05f, 0f)]
    [InlineData(20f, float.PositiveInfinity, 0f)]
    public void RawPixelsPreserveSignRespectSensitivityAndRejectNoise(float delta, float sensitivity, float expected)
    {
        Type type = typeof(BallCarrier).Assembly.GetType("RaylibTackleAlley.Game.RightStickInput")!;
        var normalize = type.GetMethod("NormalizeMouseDelta", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, (float)normalize.Invoke(null, [delta, sensitivity])!, 4);
    }
}
