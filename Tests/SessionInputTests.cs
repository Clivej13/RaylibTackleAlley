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

public sealed class SessionInputTests : IDisposable
{
    private readonly GameApplication _app;
    private readonly InputConfig _bindings;
    private InputController Input => Field<InputController>(_app, "_input");
    private MenuManager Menu => Field<MenuManager>(_app, "_mainMenu");
    private string State => Field<object>(_app, "_state").ToString()!;
    private TackleAlleyGame Game => Field<TackleAlleyGame>(_app, "_game");
    private BallCarrier Player => Field<BallCarrier>(Game, "_player");
    private static T Field<T>(object value, string name) =>
        (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Invoke(object value, string name, params object[] args) =>
        value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);

    public SessionInputTests()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Session input");
        Raylib.SetExitKey(KeyboardKey.Null);
        NativeInputTestState.Clear();
        _bindings = InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json"));
        _app = new(new GameConfig(), _bindings,
            MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json")),
            new AssetConfig(), new TackleAlleyConfig { OpponentSpawns = [] },
            ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json")));
    }

    private static unsafe void Event(uint type, int a, int b = 0, int c = 0)
    {
        AutomationEvent e = new() { Type = type };
        e.Params[0] = a; e.Params[1] = b; e.Params[2] = c;
        Raylib.PlayAutomationEvent(e);
    }
    private void Tick(float dt = 0)
    {
        Input.Update(); Invoke(_app, "Update", dt);
        Raylib.BeginDrawing(); Raylib.EndDrawing();
    }
    private void Key(KeyboardKey key)
    {
        Event(2, (int)key); Tick();
        Event(1, (int)key); Tick();
    }
    private void Button(GamepadButton button)
    {
        Event(9, 0); Event(12, 0, (int)button); Tick();
        Event(11, 0, (int)button); Tick();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TitleLocksMenusAndGameplayToSelectedFamily(bool controller)
    {
        Assert.Equal("Title", State);
        Key(KeyboardKey.Enter);
        Button(GamepadButton.RightFaceDown);
        Assert.Equal("Title", State);
        if (controller) Button(GamepadButton.MiddleRight); else Key(KeyboardKey.Space);
        Assert.Equal("MainMenu", State);
        Assert.Equal("Main", Menu.CurrentMenuName);
        Assert.Equal(controller ? InputDeviceFamily.Gamepad : InputDeviceFamily.KeyboardMouse, Input.ActiveDeviceFamily);
        // Neither the other device's title button nor navigation can switch the session.
        if (controller) { Key(KeyboardKey.Space); Key(KeyboardKey.Enter); Key(KeyboardKey.Down); }
        else { Button(GamepadButton.MiddleRight); Button(GamepadButton.RightFaceDown); Button(GamepadButton.LeftFaceDown); }
        Assert.Equal("Main", Menu.CurrentMenuName);
        Assert.Equal(0, Menu.SelectedIndex);
        if (controller)
        {
            // Hover, click and wheel must not activate/scroll anything.
            Event(7, 320, 300); Event(5, 0); Event(8, 0, -4); Tick();
            Event(6, 0); Tick();
            Assert.Equal("Main", Menu.CurrentMenuName);
            Assert.Equal(0, Menu.SelectedIndex);
            Button(GamepadButton.RightFaceDown); Button(GamepadButton.RightFaceDown);
        }
        else { Key(KeyboardKey.Enter); Key(KeyboardKey.Enter); }
        Assert.Equal("Playing", State);
        Player.IgnoreNextMouseDelta(); Tick();
        if (controller)
        {
            Event(2, (int)KeyboardKey.D); Event(7, 600, 100); Tick(.1f);
            Assert.Equal(Vector2.Zero, Player.CaptureInput(Input).Movement);
            Assert.Equal(Vector2.Zero, Player.CaptureInput(Input).Gesture);
            Assert.Equal(PlayerControlState.Auto, Player.ControlState);
            Key(KeyboardKey.Escape);
            Assert.Equal("Playing", State);
            Button(GamepadButton.MiddleRight);
        }
        else
        {
            Event(13, 0, (int)GamepadAxis.LeftX, 32768);
            Event(13, 0, (int)GamepadAxis.RightX, 32768); Tick(.1f);
            Assert.Equal(Vector2.Zero, Player.CaptureInput(Input).Movement);
            Assert.Equal(Vector2.Zero, Player.CaptureInput(Input).Gesture);
            Assert.Equal(PlayerControlState.Auto, Player.ControlState);
            Button(GamepadButton.MiddleRight);
            Assert.Equal("Playing", State);
            Key(KeyboardKey.Escape);
        }
        Assert.Equal("Paused", State);
        Assert.Contains(_bindings.Bindings, b => b.Device == "Keyboard");
        Assert.Contains(_bindings.Bindings, b => b.Device == "GamepadAxis");
    }

    [Fact]
    public void ControllerRebindIgnoresKeyboardAndPreservesOtherFamily()
    {
        Button(GamepadButton.MiddleRight);
        Button(GamepadButton.LeftFaceDown); Button(GamepadButton.LeftFaceDown);
        Button(GamepadButton.RightFaceDown);
        Assert.Equal("Controls", Menu.CurrentMenuName);
        Button(GamepadButton.RightFaceDown);
        Assert.True(Input.IsRebinding);
        Key(KeyboardKey.Z);
        Key(KeyboardKey.Escape);
        Assert.True(Input.IsRebinding);
        Button(GamepadButton.RightFaceUp);
        Assert.False(Input.IsRebinding);
        Assert.Equal("RightFaceUp", Input.GetBinding("MoveLeft", InputDeviceFamily.Gamepad)!.Input);
        Assert.Contains(_bindings.Bindings, b => b.Action == "MoveLeft" && b.Device == "Keyboard" && b.Input == "A");
        Assert.Contains(_bindings.Bindings, b => b.Action == "MoveLeft" && b.Device == "GamepadButton" && b.Input == "RightFaceUp");
        Button(GamepadButton.RightFaceRight);
        Assert.Equal("Main", Menu.CurrentMenuName);
    }

    [Fact]
    public void KeyboardRebindIgnoresControllerAndSessionSurvivesMainMenuReturn()
    {
        Key(KeyboardKey.Space);
        Key(KeyboardKey.Down); Key(KeyboardKey.Down); Key(KeyboardKey.Enter);
        Assert.Equal("Controls", Menu.CurrentMenuName);
        Key(KeyboardKey.Enter);
        Assert.True(Input.IsRebinding);
        Button(GamepadButton.RightFaceUp);
        Assert.True(Input.IsRebinding);
        Key(KeyboardKey.Z);
        Assert.False(Input.IsRebinding);
        Assert.Equal("Z", Input.GetBinding("MoveLeft", InputDeviceFamily.KeyboardMouse)!.Input);
        Assert.Contains(_bindings.Bindings, b => b.Action == "MoveLeft" && b.Device == "GamepadAxis");
        Key(KeyboardKey.Escape);
        Key(KeyboardKey.Enter); Key(KeyboardKey.Enter);
        Assert.Equal("Playing", State);
        Key(KeyboardKey.Escape);
        Key(KeyboardKey.Down); Key(KeyboardKey.Down); Key(KeyboardKey.Down); Key(KeyboardKey.Enter);
        Assert.Equal("MainMenu", State);
        Button(GamepadButton.RightFaceDown);
        Assert.Equal("Main", Menu.CurrentMenuName);
        Assert.Null(Input.GetBinding("MenuConfirm", InputDeviceFamily.Gamepad));
    }

    public void Dispose()
    {
        Field<IDisposable>(_app, "_returnerPreview").Dispose();
        Game.Dispose();
        Field<AssetManager>(_app, "_assets").UnloadAll();
        Raylib.CloseWindow();
    }
}
