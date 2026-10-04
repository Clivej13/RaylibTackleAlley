using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;
using RaylibTackleAlley.Application;
using RaylibTackleAlley.Game;
using CameraMode = RaylibTackleAlley.Game.CameraMode;
using Xunit;

public sealed class CameraIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CameraTests", Guid.NewGuid().ToString("N"));
    public CameraIntegrationTests()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Camera integration");
        NativeInputTestState.Clear();
        Raylib.SetExitKey(KeyboardKey.Null);
    }
    public void Dispose()
    {
        Raylib.CloseWindow();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Update(GameApplication app) => typeof(GameApplication).GetMethod("Update",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [0f]);
    private static unsafe void Event(uint type, int key, int button = 0, int axisValue = 0)
    {
        AutomationEvent e = new() { Type = type };
        e.Params[0] = key; e.Params[1] = button; e.Params[2] = axisValue;
        Raylib.PlayAutomationEvent(e);
    }
    private static void Frame() { Raylib.BeginDrawing(); Raylib.EndDrawing(); }

    [Fact]
    public void SelectorRestoresAndChangesImmediatelyWhilePausedAndPersists()
    {
        string path = Path.Combine(_directory, "settings.json");
        new JsonSettingsStore<UserSettings>(path).Save(new() { CameraMode = "ThirdPerson" });
        var window = new GameConfig();
        var tuning = new TackleAlleyConfig();
        var bindings = InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json"));
        var session = new UserSettingsSession(path);
        session.Load(window, tuning, bindings);
        var menus = MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json"));
        var app = new GameApplication(window, bindings, menus, new AssetConfig(), tuning,
            ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json")), settings: session);
        using var game = Field<TackleAlleyGame>(app, "_game");
        var input = Field<InputController>(app, "_input");
        var pause = Field<MenuManager>(app, "_pauseMenu");
        var state = typeof(GameApplication).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!;
        state.SetValue(app, Enum.Parse(state.FieldType, "Paused"));
        Assert.Equal(CameraMode.ThirdPerson, game.CameraMode);
        var selector = Assert.Single(menus.Menus["Options"].Items, i => i.Function == "SetCamera");
        Assert.Equal("Selector", selector.Type);
        Assert.Equal(new[] { "Third Person", "Close", "Medium", "Far" }, selector.Options);
        Assert.Equal("Third Person", selector.Value.GetString());
        void Press(KeyboardKey key)
        {
            Event(2, (int)key); input.Update(); Update(app); Frame();
            Event(1, (int)key); input.Update(); Update(app); Frame();
        }
        Press(KeyboardKey.Down); Press(KeyboardKey.Down); Press(KeyboardKey.Enter);
        Assert.Equal("Options", pause.CurrentMenuName);
        Press(KeyboardKey.Down); Press(KeyboardKey.Down);
        Vector3 playerPosition = Field<BallCarrier>(game, "_player").Position;
        var camera = Field<GameplayCamera>(game, "_camera");
        Vector3 oldPosition = camera.Camera.Position;
        // Move to the shoulder slider and apply while simulation is paused.
        Press(KeyboardKey.Down); Press(KeyboardKey.Down); Press(KeyboardKey.Down);
        Assert.Equal("SetShoulderAngle", pause.SelectedItem!.Function);
        Press(KeyboardKey.Right);
        Assert.Equal(5, tuning.ThirdPersonCamera.ShoulderAngleDegrees);
        Assert.NotEqual(oldPosition, camera.Camera.Position);
        Assert.Equal(playerPosition, Field<BallCarrier>(game, "_player").Position);
        Assert.Equal(5, new JsonSettingsStore<UserSettings>(path).Load()!.ShoulderAngleDegrees);
        Press(KeyboardKey.Down);
        Assert.Equal("SetCameraPitch", pause.SelectedItem!.Function);
        var beforeTilt = Vector3.Normalize(camera.Camera.Target - camera.Camera.Position);
        float yawBefore = camera.MovementYawDegrees;
        Press(KeyboardKey.Right);
        Assert.Equal(25, tuning.ThirdPersonCamera.InitialPitchDegrees);
        Assert.True(Vector3.Normalize(camera.Camera.Target - camera.Camera.Position).Y < beforeTilt.Y);
        Assert.Equal(yawBefore, camera.MovementYawDegrees);
        Assert.Equal(playerPosition, Field<BallCarrier>(game, "_player").Position);
        Assert.Equal(25, new JsonSettingsStore<UserSettings>(path).Load()!.CameraPitchDegrees);
        Press(KeyboardKey.Up);
        Press(KeyboardKey.Up); Press(KeyboardKey.Up); Press(KeyboardKey.Up);
        Press(KeyboardKey.Right);
        Assert.Equal(CameraMode.Close, game.CameraMode);
        Assert.NotEqual(oldPosition, camera.Camera.Position);
        Assert.Equal(playerPosition, Field<BallCarrier>(game, "_player").Position);
        Assert.Equal("Paused", state.GetValue(app)!.ToString());
        Assert.Equal("Close", selector.Value.GetString());
        Assert.Equal("Close", new JsonSettingsStore<UserSettings>(path).Load()!.CameraMode);
        Press(KeyboardKey.Escape);
        Assert.Equal("Pause", pause.CurrentMenuName);
        Assert.Equal("Paused", state.GetValue(app)!.ToString());
        var restored = new TackleAlleyConfig();
        new UserSettingsSession(path).Load(new(), restored, InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        Assert.Equal(CameraMode.Close, restored.DefaultCameraMode);
        Assert.Equal(5, restored.ThirdPersonCamera.ShoulderAngleDegrees);
        Assert.Equal(25, restored.ThirdPersonCamera.InitialPitchDegrees);
        Field<IDisposable>(app, "_returnerPreview").Dispose();
        Field<AssetManager>(app, "_assets").UnloadAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StickSliderAppliesToActiveLevelAndPersists(bool paused)
    {
        string path = Path.Combine(_directory, "stick-settings.json");
        new JsonSettingsStore<UserSettings>(path).Save(new() { LeftStickSensitivity = .7f });
        var window = new GameConfig();
        var tuning = new TackleAlleyConfig();
        var bindings = InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json"));
        var session = new UserSettingsSession(path);
        session.Load(window, tuning, bindings);
        var menus = MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json"));
        var level = LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), tuning).Resolve("level-1");
        var app = new GameApplication(window, bindings, menus, new AssetConfig(), tuning,
            ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json")), level, session);
        using var game = Field<TackleAlleyGame>(app, "_game");
        var input = Field<InputController>(app, "_input");
        var state = typeof(GameApplication).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // This component test intentionally exercises both binding families; session locking is covered separately.
        state.SetValue(app, Enum.Parse(state.FieldType, paused ? "Paused" : "MainMenu"));
        var menu = Field<MenuManager>(app, paused ? "_pauseMenu" : "_mainMenu");
        void Press(KeyboardKey key)
        {
            Event(2, (int)key); input.Update(); Update(app); Frame();
            Event(1, (int)key); input.Update(); Update(app); Frame();
        }
        for (int i = 0; i < (paused ? 2 : 1); i++) Press(KeyboardKey.Down);
        Press(KeyboardKey.Enter);
        Assert.Equal("Options", menu.CurrentMenuName);
        for (int i = 0; i < 4; i++) Press(KeyboardKey.Down);
        Press(KeyboardKey.Enter);
        Assert.Equal("Movement", menu.CurrentMenuName);
        var activePlayer = Field<BallCarrier>(game, "_player");
        var defenders = Field<Opponent[]>(game, "_opponents");
        var baseline = new[] { activePlayer.Movement }.Concat(defenders.Select(d => d.Movement)).ToArray();
        var positions = new[] { activePlayer.Position }.Concat(defenders.Select(d => d.Position)).ToArray();
        Press(KeyboardKey.Left); // All players' speed: 100 -> 95%.
        Press(KeyboardKey.Down);
        Press(KeyboardKey.Right); // All players' acceleration: 100 -> 105%.
        var changed = new[] { activePlayer.Movement }.Concat(defenders.Select(d => d.Movement)).ToArray();
        for (int i = 0; i < changed.Length; i++)
        for (int tier = 1; tier <= 3; tier++)
        {
            Assert.Equal(baseline[i].TierSpeed(tier) * .95f, changed[i].TierSpeed(tier), 4);
            Assert.Equal(baseline[i].TierAcceleration(tier) * 1.05f, changed[i].TierAcceleration(tier), 4);
        }
        Assert.Equal(positions, new[] { activePlayer.Position }.Concat(defenders.Select(d => d.Position)));
        Press(KeyboardKey.Down);
        var slider = Assert.Single(menus.Menus["Movement"].Items, i => i.Function == "SetLeftStickSensitivity");
        Assert.Equal("Slider", slider.Type);
        Assert.Equal(70, slider.Value.GetDouble());
        var player = Field<BallCarrier>(game, "_player");
        Event(9, 0); Event(13, 0, (int)GamepadAxis.LeftX, 16384); input.Update();
        float before = player.CaptureInput(input).Movement.X;
        Event(13, 0, (int)GamepadAxis.LeftX, 0); input.Update();
        Press(KeyboardKey.Left);
        Assert.Equal(65, slider.Value.GetDouble());
        Assert.Equal(.65f, tuning.LeftStickSensitivity);
        Assert.Equal(.65f, Field<TackleAlleyConfig>(game, "_config").LeftStickSensitivity);
        Event(13, 0, (int)GamepadAxis.LeftX, 16384); input.Update();
        Assert.InRange(player.CaptureInput(input).Movement.X, 0, before - .001f);
        Assert.Equal(.65f, new JsonSettingsStore<UserSettings>(path).Load()!.LeftStickSensitivity);
        var restored = new TackleAlleyConfig();
        new UserSettingsSession(path).Load(new(), restored, bindings);
        Assert.Equal(.65f, restored.LeftStickSensitivity);
        Assert.Equal(.95f, restored.MovementScaling.SpeedScale);
        Assert.Equal(1.05f, restored.MovementScaling.AccelerationScale);
        game.ResetRun();
        Assert.Same(changed[0], activePlayer.Movement);
        var roster = ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json"));
        game.SelectReturner(roster, "darius-stone");
        var selected = Field<BallCarrier>(game, "_player");
        var expected = new PlayerMovementAttributes(selected.Profile, tuning);
        Assert.Equal(expected.SprintSpeed, selected.Movement.SprintSpeed);
        Assert.Equal(expected.AccelerationRate, selected.Movement.AccelerationRate);
        Press(KeyboardKey.Escape);
        Assert.Equal("Options", menu.CurrentMenuName);
        Assert.Equal(paused ? "Paused" : "MainMenu", state.GetValue(app)!.ToString());
        Field<IDisposable>(app, "_returnerPreview").Dispose();
        Field<AssetManager>(app, "_assets").UnloadAll();
    }

    [Fact]
    public void FixedCameraModesAndLookInputLeaveMovementAiAndContactIdentical()
    {
        var config = new TackleAlleyConfig { OpponentSpawns = [new() { X = 0, Z = -8 }] };
        var input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        using var baseline = new TackleAlleyGame(config, input, new AssetManager(new AssetConfig()));
        using var changing = new TackleAlleyGame(config, input, new AssetManager(new AssetConfig()));
        var a = Field<BallCarrier>(baseline, "_player");
        var b = Field<BallCarrier>(changing, "_player");
        var defenderA = Field<Opponent[]>(baseline, "_opponents")[0];
        var defenderB = Field<Opponent[]>(changing, "_opponents")[0];
        Event(2, (int)KeyboardKey.L);
        Event(2, (int)KeyboardKey.W);
        bool sawCommitment = false;
        for (int frame = 0; frame < 240; frame++)
        {
            if (frame % 30 == 0) changing.SetCameraMode((CameraMode)(1 + (frame / 30) % 3));
            if (frame == 15) Event(2, (int)KeyboardKey.LeftShift);
            if (frame == 40) Event(1, (int)KeyboardKey.LeftShift);
            if (frame == 150) { Event(1, (int)KeyboardKey.W); Event(2, (int)KeyboardKey.S); }
            if (frame == 180) Event(2, (int)KeyboardKey.D);
            if (frame == 210) { Event(1, (int)KeyboardKey.S); Event(1, (int)KeyboardKey.D); Event(2, (int)KeyboardKey.A); }
            input.Update();
            baseline.Update(1f / 60); changing.Update(1f / 60);
            Assert.Equal(a.Position, b.Position);
            Assert.Equal(a.Velocity, b.Velocity);
            Assert.Equal(a.CurrentForwardSpeed, b.CurrentForwardSpeed);
            Assert.Equal(a.SpeedTier, b.SpeedTier);
            Assert.Equal(a.Movement.AccelerationRate, b.Movement.AccelerationRate);
            Assert.Equal(defenderA.Position, defenderB.Position);
            Assert.Equal(defenderA.State, defenderB.State);
            Assert.Equal(defenderA.AiState, defenderB.AiState);
            Assert.Equal(defenderA.CurrentSpeed, defenderB.CurrentSpeed);
            Assert.Equal(baseline.TacklePendingGroundImpact, changing.TacklePendingGroundImpact);
            Assert.Equal(baseline.GameOver, changing.GameOver);
            Assert.Equal(a.Ragdoll.IsActive, b.Ragdoll.IsActive);
            sawCommitment |= defenderA.State is DefenderState.LungeTackle or DefenderState.SetWrap;
            Frame();
        }
        Assert.True(sawCommitment);
        Event(1, (int)KeyboardKey.L);
    }

    [Theory]
    [InlineData(CameraMode.ThirdPerson, 90, 0, -1, false)]
    [InlineData(CameraMode.ThirdPerson, 180, 0, -1, false)]
    [InlineData(CameraMode.ThirdPerson, -90, 0, -1, false)]
    [InlineData(CameraMode.ThirdPerson, 45, 0, 1, false)]
    [InlineData(CameraMode.ThirdPerson, 90, 1, 0, false)]
    [InlineData(CameraMode.ThirdPerson, 180, -1, 0, false)]
    [InlineData(CameraMode.ThirdPerson, 45, 1, -1, true)]
    [InlineData(CameraMode.ThirdPerson, 90, 0, 1, true)]
    [InlineData(CameraMode.Close, 90, 0, -1, false)]
    [InlineData(CameraMode.Medium, 180, 0, -1, false)]
    [InlineData(CameraMode.Far, -90, 1, 0, true)]
    public void ControllerDirectionUsesCameraYawOnlyInThirdPerson(CameraMode mode, float yaw, float x, float z, bool sprint)
    {
        var config = new TackleAlleyConfig { OpponentSpawns = [], DefaultCameraMode = mode, PlayerSpawn = new() { Z = -30 } };
        var input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        using var game = new TackleAlleyGame(config, input, new AssetManager(new AssetConfig()));
        var player = Field<BallCarrier>(game, "_player");
        var camera = Field<GameplayCamera>(game, "_camera");
        camera.Reset(player.Position, yaw);
        Event(9, 0);
        Event(13, 0, (int)GamepadAxis.LeftX, (int)(x * 32768));
        Event(13, 0, (int)GamepadAxis.LeftY, (int)(z * 32768));
        if (sprint) Event(12, 0, (int)GamepadButton.RightTrigger2);
        input.Update();
        Vector3 start = player.Position;
        game.Update(.1f);
        Vector3 direction = new(x, 0, z);
        if (mode == CameraMode.ThirdPerson)
            direction = DirectionalMovement.Right(yaw) * x - DirectionalMovement.Forward(yaw) * z;
        Vector3 expected = Vector3.Normalize(direction) * (.5f * player.Movement.TierAcceleration(sprint ? 3 : 2) * .01f);
        Assert.InRange(Vector3.Distance(expected, player.Position - start), 0, .00001f);
        Assert.Equal(sprint ? 3 : 2, player.SpeedTier);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void HoldingSidewaysTurnsCameraBehindRunnerWithoutMakingTheRunnerCircle(int side)
    {
        var config = new TackleAlleyConfig { OpponentSpawns = [], DefaultCameraMode = CameraMode.ThirdPerson, PlayerSpawn = new() { Z = -30 } };
        var input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        using var game = new TackleAlleyGame(config, input, new AssetManager(new AssetConfig()));
        var player = Field<BallCarrier>(game, "_player");
        var camera = Field<GameplayCamera>(game, "_camera");
        camera.Reset(player.Position, 90);
        for (int i = 0; i < 90; i++)
        {
            Event(9, 0); Event(13, 0, (int)GamepadAxis.LeftX, side * 32768);
            input.Update(); game.Update(1f / 60); Frame();
            Assert.True(float.IsFinite(camera.MovementYawDegrees));
            Assert.InRange(Math.Abs(player.Position.X), 0, .00001f);
        }
        Assert.True((player.Position.Z + 30) * side > 0);
        Assert.InRange(Math.Abs(DirectionalMovement.Delta(camera.MovementYawDegrees, player.FacingYawDegrees)), 0, 1);
        Vector3 position = player.Position, velocity = player.Velocity;
        float speed = player.CurrentForwardSpeed, facing = player.FacingYawDegrees;
        foreach (CameraMode mode in Enum.GetValues<CameraMode>()) game.SetCameraMode(mode);
        Assert.Equal(position, player.Position);
        Assert.Equal(velocity, player.Velocity);
        Assert.Equal(speed, player.CurrentForwardSpeed);
        Assert.Equal(facing, player.FacingYawDegrees);
    }

    [Fact]
    public void RelativeSnapshotPreservesAnalogueMagnitudeAndGestureState()
    {
        var original = new PlayerInputSnapshot(.3f, 0, 1, new(.8f, -.2f), true, .4f, .7f);
        var world = original.RelativeToYaw(137);
        Assert.Equal(.5f, world.Movement.Length(), 5);
        Assert.Equal(original.Gesture, world.Gesture);
        Assert.Equal(original.GestureIsMouse, world.GestureIsMouse);
        Assert.Equal(original.Sprint, world.Sprint);
        Assert.Equal(original.Slow, world.Slow);
        Assert.Equal(new Vector2(.3f, -.4f), original.Movement);
        var diagonal = new PlayerInputSnapshot(1, 0, 0, default, false, 1).RelativeToYaw(45);
        Assert.Equal(1, diagonal.Movement.Length(), 5);
    }

    [Fact]
    public void ControllerCameraLookDoesNotUseMovementOrEvadeAxes()
    {
        var input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        Event(9, 0); Event(12, 0, (int)GamepadButton.LeftFaceRight);
        input.Update();
        Assert.Equal(new Vector2(1, 0), CameraLookInput.Capture(input).Rate);
        Assert.Equal(0, input.GetValue("MoveRight"));
        Assert.Equal(0, input.GetValue("RightStickRight"));
        Event(11, 0, (int)GamepadButton.LeftFaceRight);
    }
}
