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
    private BallCarrier _player;
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
        Assert.Equal(-(speedBefore + _player.CurrentForwardSpeed) * 0.5f * Dt
            * (direction == 0 ? 1f : 0.75f), _player.Position.Z - before.Z, 4);
        if (key == KeyboardKey.Left)
            Assert.True(_input.WasPressed("MenuLeft"));
        Event(1, (int)key);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-1f, 0.75f)]
    [InlineData(1f, 0.75f)]
    [InlineData(-0.5f, 0.875f)]
    [InlineData(0.5f, 0.875f)]
    [InlineData(0.75f, 0.8125f)]
    public void SteeringRetainsExpectedForwardTravel(float lateral, float retention)
    {
        _player.Reset();
        Axis(GamepadAxis.LeftX, lateral);
        Tick(dt: 0.1f);
        Assert.Equal(6.5f * 0.1f * retention, -_player.Position.Z, 4);
        Assert.Equal(lateral * 8f * 0.1f, _player.Position.X, 4);
        Assert.Equal(6.5f, _player.CurrentForwardSpeed);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void SprintSteeringGraduallyReducesForwardTravel(float side)
    {
        Event(2, (int)KeyboardKey.LeftShift);
        Tick(dt: 0.3f); // Finish acceleration before measuring steering.
        foreach (float retention in new[] { 11f / 12f, 5f / 6f, 0.75f })
        {
            Vector3 before = _player.Position;
            Axis(GamepadAxis.LeftX, side);
            Tick(dt: 0.1f);
            Assert.Equal(9f * 0.1f * retention, before.Z - _player.Position.Z, 4);
            Assert.Equal(9f, _player.CurrentForwardSpeed, 4);
        }
        Event(1, (int)KeyboardKey.LeftShift);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-1f, 0.75f)]
    [InlineData(0.5f, 0.875f)]
    public void SteeringScalesRampDistanceWithoutChangingAccelerationOrDeceleration(float lateral, float retention)
    {
        Axis(GamepadAxis.LeftX, lateral);
        Tick(dt: 0f); // Settle steering before Sprint.
        Event(2, (int)KeyboardKey.LeftShift);
        CheckStep(0.1f, 6.5f + _config.ForwardAcceleration * 0.1f,
            6.5f * 0.1f + 0.5f * _config.ForwardAcceleration * 0.01f);
        // Includes time travelling at the target after acceleration finishes.
        CheckStep(0.3f, 9f, (6.5f + _config.ForwardAcceleration * 0.1f + 9f) * 0.1f + 9f * 0.1f);
        Event(1, (int)KeyboardKey.LeftShift);
        CheckStep(0.1f, 7.75f, (9f + 7.75f) * 0.05f);
        CheckStep(0.2f, 6.5f, (7.75f + 6.5f) * 0.05f + 6.5f * 0.1f);
        Event(2, (int)KeyboardKey.S);
        CheckStep(0.3f, 4f, (6.5f + 4f) * 0.1f + 4f * 0.1f);
        Event(1, (int)KeyboardKey.S);

        void CheckStep(float dt, float expectedSpeed, float unscaledDistance)
        {
            float before = _player.Position.Z;
            Axis(GamepadAxis.LeftX, lateral);
            Tick(dt: dt);
            Assert.Equal(expectedSpeed, _player.CurrentForwardSpeed, 4);
            Assert.InRange(Math.Abs(unscaledDistance * retention - (before - _player.Position.Z)), 0f, 0.00001f);
        }
    }

    [Fact]
    public void ForwardSpeedRampsIndependentlyOfSprintSteeringAndResetRestoresRun()
    {
        _player.Reset();
        Event(2, (int)KeyboardKey.LeftShift);
        Event(2, (int)KeyboardKey.D);
        Tick(dt: 0f);
        Assert.Equal(3, _player.SpeedTier);
        Assert.Equal("CarrySprint", _player.AnimationName);
        Assert.Equal(9f, _player.TargetForwardSpeed);
        Assert.Equal(6.5f, _player.CurrentForwardSpeed);
        Tick(dt: 0.1f);
        Assert.InRange(_player.CurrentForwardSpeed, 6.6f, 8.9f);
        Assert.InRange(_player.Position.X, 0.01f, 0.79f);
        Tick(dt: 0.2f);
        Assert.Equal(9f, _player.CurrentForwardSpeed, 4);
        Event(1, (int)KeyboardKey.LeftShift);
        Tick(dt: 0f);
        Assert.Equal(6.5f, _player.TargetForwardSpeed);
        Assert.Equal("CarryRun", _player.AnimationName);
        Tick(dt: 0.1f);
        Assert.InRange(_player.CurrentForwardSpeed, 6.6f, 8.9f);
        Tick(dt: 0.1f);
        Assert.Equal(6.5f, _player.CurrentForwardSpeed, 4);
        Event(2, (int)KeyboardKey.S);
        Tick(dt: 0.2f);
        Assert.Equal(4f, _player.TargetForwardSpeed);
        Assert.Equal(4f, _player.CurrentForwardSpeed, 4);
        Assert.Equal("CarryJog", _player.AnimationName);
        _player.Reset();
        Assert.Equal(2, _player.SpeedTier);
        Assert.Equal(6.5f, _player.CurrentForwardSpeed);
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
        Assert.Equal(6.5f, player.CurrentForwardSpeed);
        Assert.Equal(6.75f, camera.Camera.Position.Z);
        game.Update(0.1f);
        float desiredZ = player.Position.Z + camera.DistanceForSpeed(player.CurrentForwardSpeed);
        float expectedZ = 6.75f + (desiredZ - 6.75f) * (1f - MathF.Exp(-1f));
        Assert.Equal(expectedZ, camera.Camera.Position.Z, 5);
        game.ResetRun();
        Assert.Equal(6.5f, player.CurrentForwardSpeed);
        Assert.Equal(new Vector3(0f, 5.5f, 6.75f), camera.Camera.Position);
        Event(1, (int)KeyboardKey.LeftShift);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(1f, 0f)]
    [InlineData(0f, 1f)]
    public void LeftStickControlsGameCameraAlongsideMovement(float x, float y)
    {
        var game = new TackleAlleyGame(_config, _input, new AssetManager(new AssetConfig()));
        var camera = Field<ThirdPersonCamera>(game, "_camera");
        var player = Field<BallCarrier>(game, "_player");
        Axis(GamepadAxis.LeftX, x);
        Axis(GamepadAxis.LeftY, y);
        _input.Update();
        for (int i = 0; i < 30; i++) game.Update(Dt);
        Vector3 view = camera.Camera.Target - camera.Camera.Position;
        if (y > 0)
        {
            Assert.True(view.Z > 0);
            Assert.Equal(1, player.SpeedTier);
            Assert.True(player.Position.Z < 0);
        }
        else
        {
            Assert.True(view.X * x > 0);
            Assert.True(view.Z < 0);
            Assert.True(player.Position.X * x > 0);
        }
        Axis(GamepadAxis.LeftX, 0);
        Axis(GamepadAxis.LeftY, 0);
    }

    [Fact]
    public void ConfiguredSteeringGestureThresholdsAndEvadeMomentumAreUsed()
    {
        _config.PlayerSpawn = new() { X = 1, Z = -2 };
        _config.FullSteeringForwardRetention = .5f;
        _config.JukeInputThreshold = .9f;
        _config.JukeSpeedRetention = .8f;
        _player.Reset();
        Axis(GamepadAxis.LeftX, 1);
        Tick(dt: .1f);
        Assert.Equal(1.8f, _player.Position.X, 4);
        Assert.Equal(-2.325f, _player.Position.Z, 4);
        Axis(GamepadAxis.LeftX, 0);
        Axis(GamepadAxis.RightX, .8f);
        Tick(dt: 0);
        Assert.Equal(0, Field<float>(_player, "_jukeRemaining"));
        Axis(GamepadAxis.RightX, 1);
        Tick(dt: 0);
        Assert.Equal(_config.PlayerForwardSpeed * .8f, _player.CurrentForwardSpeed, 4);
        Assert.Equal(_config.PlayerJukeDuration, Field<float>(_player, "_jukeRemaining"));

        Axis(GamepadAxis.RightX, 0);
        _config.PlayerSpinDuration = .8f;
        _config.PlayerSpinSpeed = 12;
        _config.SpinSpeedRetention = .6f;
        _player = new BallCarrier(_config); // Derived attributes snapshot global balance at creation.
        _player.Reset();
        TriggerMomentumMove(true);
        Assert.Equal(.8f, Field<float>(_player, "_spinRemaining"));
        Assert.Equal(_config.PlayerForwardSpeed * .6f, _player.CurrentForwardSpeed, 4);
        Tick(dt: .2f);
        Assert.Equal(3.4f, _player.Position.X, 4);
        Assert.Equal(-2, _player.Position.Z, 4);
    }

    [Fact]
    public void ConfiguredSpawnsAndOpponentCountAreRestoredOnReset()
    {
        _config.PlayerSpawn = new() { X = 2, Z = -3 };
        _config.OpponentSpawns = [new() { X = -3, Z = -25 }, new() { X = 4, Z = -40 }];
        var game = new TackleAlleyGame(_config, _input, new AssetManager(new AssetConfig()));
        var player = Field<BallCarrier>(game, "_player");
        var opponents = Field<Opponent[]>(game, "_opponents");
        Assert.Equal(new Vector3(2, 0, -3), player.Position);
        Assert.Equal(2, opponents.Length);
        Assert.Equal(new Vector3(-3, 0, -25), opponents[0].Position);
        game.Update(.1f);
        game.ResetRun();
        Assert.Equal(_config.PlayerSpawn.Position, player.Position);
        Assert.Equal(_config.OpponentSpawns.Select(s => s.Position), opponents.Select(o => o.Position));
        Assert.ThrowsAny<ArgumentException>(() => new TackleAlleyGame(
            new() { ContactMaximumImpulse = -1 }, _input, new AssetManager(new AssetConfig())));
    }

    [Fact]
    public void RepeatedSteeringReversalsStopTravelAndDelayAcceleration()
    {
        Axis(GamepadAxis.LeftX, 1); Tick(dt: 0);
        Axis(GamepadAxis.LeftX, -1); Tick(dt: 0);
        Assert.Equal(6.5f * .65f, _player.CurrentForwardSpeed, 4);
        Assert.Equal(.45f, _player.SteeringRecoveryRemaining, 4);
        float x = _player.Position.X;
        Axis(GamepadAxis.LeftX, -1); Tick(dt: .1f);
        Assert.Equal(6.5f * .65f, _player.CurrentForwardSpeed, 4);
        Assert.Equal(x - 8 * .65f * .1f, _player.Position.X, 4);
        Axis(GamepadAxis.LeftX, 1); Tick(dt: 0);
        Assert.Equal(6.5f * .3f, _player.CurrentForwardSpeed, 4);
        Assert.Equal(.65f, _player.SteeringRecoveryRemaining, 4);
        Axis(GamepadAxis.LeftX, -1); Tick(dt: 0);
        Assert.Equal(0, _player.CurrentForwardSpeed);
        Assert.Equal(.85f, _player.SteeringRecoveryRemaining, 4);
        Vector3 stopped = _player.Position;
        Event(2, (int)KeyboardKey.LeftShift);
        Tick(dt: .4f);
        Assert.Equal(stopped, _player.Position);
        Assert.Equal(0, _player.CurrentForwardSpeed);
        Tick(dt: .45f);
        Assert.InRange(_player.CurrentForwardSpeed, 0, .00001f);
        Tick(dt: .1f);
        Assert.Equal(_config.ForwardAcceleration * .1f, _player.CurrentForwardSpeed, 4);
        Event(1, (int)KeyboardKey.LeftShift);
        _player.Reset();
        Assert.Equal(0, _player.SteeringRecoveryRemaining);
        Assert.Equal(6.5f, _player.CurrentForwardSpeed);
    }

    [Fact]
    public void SmallSteeringNoiseAndSlowReversalsDoNotPenalize()
    {
        foreach (float side in new[] { .2f, -.2f, .3f, -.3f })
        {
            Axis(GamepadAxis.LeftX, side); Tick(dt: .1f);
            Assert.Equal(6.5f, _player.CurrentForwardSpeed);
        }
        Axis(GamepadAxis.LeftX, 1); Tick(dt: .1f);
        Axis(GamepadAxis.LeftX, 0); Tick(dt: .3f);
        Axis(GamepadAxis.LeftX, -1); Tick(dt: .1f);
        Assert.Equal(6.5f, _player.CurrentForwardSpeed);
        Assert.Equal(0, _player.SteeringRecoveryRemaining);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void ReversalRecoveryIntegratesTheDelayBoundaryAtEveryFrameRate(int fps)
    {
        _config.PlayerReversalSpeedLoss = .5f;
        _config.PlayerReversalAccelerationDelay = .17f;
        _config.PlayerReversalAdditionalDelay = 0;
        var coarse = Run(1);
        var fine = Run(fps);
        Assert.Equal(coarse.Speed, fine.Speed, 4);
        Assert.Equal(coarse.Position.X, fine.Position.X, 4);
        Assert.Equal(coarse.Position.Z, fine.Position.Z, 4);
        (float Speed, Vector3 Position) Run(int steps)
        {
            _player.Reset();
            Axis(GamepadAxis.LeftX, 1); Tick(dt: 0);
            Axis(GamepadAxis.LeftX, -1); Tick(dt: 0);
            for (int i = 0; i < steps; i++) Tick(dt: .4f / steps);
            return (_player.CurrentForwardSpeed, _player.Position);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvadesCannotRestoreForwardSpeedDuringReversalLockout(bool spin)
    {
        Axis(GamepadAxis.LeftX, 1); Tick(dt: 0);
        Axis(GamepadAxis.LeftX, -1); Tick(dt: 0);
        Axis(GamepadAxis.LeftX, 1); Tick(dt: 0);
        Axis(GamepadAxis.LeftX, -1); Tick(dt: 0);
        Vector3 stopped = _player.Position;
        TriggerMomentumMove(spin);
        Assert.Equal(0, _player.CurrentForwardSpeed);
        Tick(dt: .1f);
        Assert.Equal(0, _player.CurrentForwardSpeed);
        Assert.Equal(stopped, _player.Position);
        Assert.True(_player.SteeringRecoveryRemaining > 0);
    }

    [Theory]
    [InlineData(169f, false)]
    [InlineData(175f, true)]
    [InlineData(185f, true)]
    [InlineData(191f, false)]
    [InlineData(135f, false)]
    public void NativeLeftStickRearViewRespectsAngle(float angle, bool behind)
    {
        _config.OpponentSpawns = [];
        var game = new TackleAlleyGame(_config, _input, new AssetManager(new AssetConfig()));
        float radians = angle * MathF.PI / 180;
        Axis(GamepadAxis.LeftX, MathF.Sin(radians));
        Axis(GamepadAxis.LeftY, -MathF.Cos(radians));
        _input.Update();
        for (int i = 0; i < 60; i++) game.Update(Dt);
        var camera = Field<ThirdPersonCamera>(game, "_camera").Camera;
        Assert.Equal(behind, camera.Target.Z > camera.Position.Z);
    }

    [Theory]
    [InlineData(false, 0f)]
    [InlineData(false, .25f)]
    [InlineData(false, .5f)]
    [InlineData(false, 1f)]
    [InlineData(false, 1.5f)]
    [InlineData(true, 0f)]
    [InlineData(true, .25f)]
    [InlineData(true, .5f)]
    [InlineData(true, 1f)]
    [InlineData(true, 1.5f)]
    public void EvadeDistanceUsesEntrySpeedAndKeepsAnimationDuration(bool spin, float speedFraction)
    {
        typeof(BallCarrier).GetProperty(nameof(BallCarrier.CurrentForwardSpeed))!
            .SetValue(_player, _config.PlayerForwardSpeed * speedFraction);
        Vector3 start = _player.Position;
        TriggerMomentumMove(spin);
        float duration = spin ? _config.PlayerSpinDuration : _config.PlayerJukeDuration;
        float speed = spin ? _config.PlayerSpinSpeed : _config.PlayerJukeSpeed;
        Assert.Equal(duration, Field<float>(_player, spin ? "_spinRemaining" : "_jukeRemaining"));
        Tick(dt: duration);
        Assert.Equal(speed * duration * Math.Min(speedFraction, 1), _player.Position.X - start.X, 4);
        Assert.Equal(start.Z, _player.Position.Z);
        Assert.Equal(0, Field<float>(_player, spin ? "_spinRemaining" : "_jukeRemaining"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlowEvadeDistanceIsFrameIndependentAndHonoursConfiguredCap(bool spin)
    {
        _config.PlayerEvadeMaximumDistanceScale = .4f;
        Vector3 whole = Run(1);
        Vector3 split = Run(60);
        Assert.True(Vector3.Distance(whole, split) < .0001f);
        float speed = spin ? _config.PlayerSpinSpeed : _config.PlayerJukeSpeed;
        float duration = spin ? _config.PlayerSpinDuration : _config.PlayerJukeDuration;
        Assert.Equal(speed * duration * .4f, whole.X, 4);

        Vector3 Run(int steps)
        {
            Axis(GamepadAxis.RightX, 0); Axis(GamepadAxis.RightY, 0);
            _player.Reset(); Tick(dt: 0);
            typeof(BallCarrier).GetProperty(nameof(BallCarrier.CurrentForwardSpeed))!
                .SetValue(_player, _config.PlayerForwardSpeed * .5f);
            TriggerMomentumMove(spin);
            float duration = spin ? _config.PlayerSpinDuration : _config.PlayerJukeDuration;
            for (int i = 0; i < steps; i++) Tick(dt: duration / steps);
            return _player.Position;
        }
    }

    private void TriggerMomentumMove(bool spin)
    {
        if (spin)
        {
            Axis(GamepadAxis.RightY, 1f); Tick(dt: 0f);
            Axis(GamepadAxis.RightY, 0f);
        }
        Axis(GamepadAxis.RightX, 1f); Tick(dt: 0f);
    }

    [Theory]
    [InlineData(false, false, 0.50f)]
    [InlineData(false, true, 0.50f)]
    [InlineData(true, false, 0.30f)]
    [InlineData(true, true, 0.30f)]
    public void EvasiveMomentumUsesTriggerTierOnceAndRecovers(bool spin, bool slow, float retention)
    {
        // Change tier on the trigger update, before speed has reached that target.
        if (slow) Event(2, (int)KeyboardKey.S);
        TriggerMomentumMove(spin);
        float target = slow ? _config.PlayerSlowSpeed : _config.PlayerForwardSpeed;
        Assert.Equal(target * retention, _player.CurrentForwardSpeed, 5);
        Assert.True(Field<float>(_player, spin ? "_spinRemaining" : "_jukeRemaining") > 0f);
        for (int i = 1; i <= 10; i++)
        {
            // Held input cannot reapply the penalty, even after the move ends.
            Axis(GamepadAxis.RightX, 1f); Tick(dt: 0.1f);
            Assert.Equal(Math.Min(target, target * retention + _config.ForwardAcceleration * i * 0.1f),
                _player.CurrentForwardSpeed, 4);
        }
        Assert.Equal(target, _player.CurrentForwardSpeed, 4);
        if (slow) Event(1, (int)KeyboardKey.S);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvadeOwnsSidewaysTravelAndResumesForwardTravelWithinItsFinalFrame(bool spin)
    {
        TriggerMomentumMove(spin);
        float duration = spin ? 0.45f : _config.PlayerJukeDuration;
        float lateralSpeed = spin ? _config.PlayerSpinSpeed : _config.PlayerJukeSpeed;
        Vector3 before = _player.Position;
        Tick(dt: duration - 0.05f);
        Assert.Equal(before.Z, _player.Position.Z);
        Assert.Equal(lateralSpeed * (duration - 0.05f), _player.Position.X - before.X, 4);
        float initialSpeed = _config.PlayerForwardSpeed * (spin ? 0.30f : 0.50f);
        // The next frame contains 0.05s evading and 0.05s normal forward travel.
        Tick(dt: 0.1f);
        float target = _config.PlayerForwardSpeed;
        float speedAtEvadeEnd = Math.Min(target, initialSpeed + _config.ForwardAcceleration * duration);
        float acceleratingTime = Math.Min(0.05f, (target - speedAtEvadeEnd) / _config.ForwardAcceleration);
        float expectedDistance = speedAtEvadeEnd * acceleratingTime
            + 0.5f * _config.ForwardAcceleration * acceleratingTime * acceleratingTime
            + target * (0.05f - acceleratingTime);
        Assert.Equal(expectedDistance, before.Z - _player.Position.Z, 4);
        Assert.Equal(lateralSpeed * duration, _player.Position.X - before.X, 4);
        Assert.Equal(0f, Field<float>(_player, spin ? "_spinRemaining" : "_jukeRemaining"));
        if (!spin)
        {
            Assert.True(duration < 0.45f);
            Assert.True(lateralSpeed * duration < 4.5f);
        }
        before = _player.Position;
        Tick(dt: 0.1f);
        Assert.True(_player.Position.Z < before.Z);
    }
    [Theory]
    [InlineData(false, 30)]
    [InlineData(false, 60)]
    [InlineData(false, 120)]
    [InlineData(true, 30)]
    [InlineData(true, 60)]
    [InlineData(true, 120)]
    public void EvasiveRecoveryRetargetsWithExistingRatesAndIsFrameRateIndependent(bool spin, int fps)
    {
        // Non-default rates ensure recovery uses the configured existing ramp.
        _config.ForwardAcceleration = 10f;
        _config.ForwardDeceleration = 4f;
        _player = new BallCarrier(_config);
        var coarse = Simulate(1);
        var fine = Simulate(fps);
        Assert.Equal(coarse.Speed, fine.Speed, 4);
        Assert.Equal(coarse.Position.Z, fine.Position.Z, 4);
        Assert.Equal(coarse.Position.X, fine.Position.X, 4);

        (float Speed, Vector3 Position) Simulate(int steps)
        {
            Event(1, (int)KeyboardKey.S);
            Event(1, (int)KeyboardKey.LeftShift);
            Axis(GamepadAxis.RightX, 0f); Axis(GamepadAxis.RightY, 0f);
            _player.Reset(); Tick(dt: 0f);
            TriggerMomentumMove(spin);
            float initial = _player.CurrentForwardSpeed;
            Advance(0.5f);
            Assert.Equal(Math.Min(6.5f, initial + 5f), _player.CurrentForwardSpeed, 4);
            // Lower target during recovery: the configured deceleration must take over.
            Event(2, (int)KeyboardKey.S);
            float before = _player.CurrentForwardSpeed;
            Advance(0.1f);
            Assert.Equal(Math.Max(4f, before - 0.4f), _player.CurrentForwardSpeed, 4);
            // Higher tier retargets the same ramp without another move penalty.
            Event(1, (int)KeyboardKey.S);
            Event(2, (int)KeyboardKey.LeftShift);
            before = _player.CurrentForwardSpeed;
            Advance(0.2f);
            Assert.Equal(Math.Min(9f, before + 2f), _player.CurrentForwardSpeed, 4);
            Event(1, (int)KeyboardKey.LeftShift);
            return (_player.CurrentForwardSpeed, _player.Position);

            void Advance(float duration)
            {
                int frames = steps == 1 ? 1 : (int)MathF.Round(duration * steps);
                for (int i = 0; i < frames; i++) Tick(dt: duration / frames);
            }
        }
    }

    private void ArmCut(float initial)
    {
        Event(2, (int)KeyboardKey.LeftShift);
        Axis(GamepadAxis.LeftX, initial); Tick();
        Event(1, (int)KeyboardKey.LeftShift); Tick(dt: 0f);
    }

    [Fact]
    public void SprintReleaseArmsCutWindow()
    {
        ArmCut(-0.9f);
        Assert.Equal("CarryRun", _player.AnimationName);
        Assert.Equal(0.40f, Field<float>(_player, "_cutReversalRemaining"));
        Assert.Equal(-1, Field<int>(_player, "_lastCutSide"));
    }

    [Theory]
    [InlineData(-0.9f, "CutRight", 0f)]
    [InlineData(0.9f, "CutLeft", 0f)]
    [InlineData(-0.9f, "CutRight", 0.35f)]
    [InlineData(0.9f, "CutLeft", 0.38f)]
    public void ReleasedSprintReversalCutsIncludingBriefNeutral(float initial, string expected, float neutralTime)
    {
        ArmCut(initial);
        Axis(GamepadAxis.LeftX, 0f); Tick(dt: neutralTime);
        Axis(GamepadAxis.LeftX, -initial); Tick(dt: 0.01f);
        Assert.Equal(expected, _player.AnimationName);
        Tick(dt: 0.4f);
        Assert.Equal("CarryRun", _player.AnimationName);
        Tick();
        Assert.Equal("CarryRun", _player.AnimationName);
    }

    [Fact]
    public void SprintRepressPreservesWindowAndUsesRawSteering()
    {
        ArmCut(-0.9f);
        Event(2, (int)KeyboardKey.LeftShift);
        Axis(GamepadAxis.LeftX, 0f); Tick(dt: 0.1f);
        Assert.Equal(0.30f, Field<float>(_player, "_cutReversalRemaining"), 5);
        Axis(GamepadAxis.LeftX, 0.9f); Tick();
        Assert.True(Field<float>(_player, "_effectiveLateral") < 0.65f);
        Assert.Equal("CutRight", _player.AnimationName);
        Event(1, (int)KeyboardKey.LeftShift);
    }

    [Theory]
    [InlineData(0.9f, 0.41f)]
    [InlineData(0.5f, 0.02f)]
    [InlineData(0f, 0.02f)]
    public void ExpiredOrMissingStrongDirectionDoesNotCut(float strength, float neutralTime)
    {
        ArmCut(-strength);
        Axis(GamepadAxis.LeftX, 0f); Tick(dt: neutralTime);
        Axis(GamepadAxis.LeftX, 0.9f); Tick();
        Assert.Equal("CarryRun", _player.AnimationName);
    }

    [Fact]
    public void ReversalWithoutSprintReleaseDoesNotCut()
    {
        Axis(GamepadAxis.LeftX, -0.9f); Tick();
        Axis(GamepadAxis.LeftX, 0.9f); Tick();
        Assert.Equal("CarryRun", _player.AnimationName);
        Event(2, (int)KeyboardKey.LeftShift);
        Axis(GamepadAxis.LeftX, -0.9f); Tick();
        Assert.Equal("CarrySprint", _player.AnimationName);
        Event(1, (int)KeyboardKey.LeftShift);
    }

    [Fact]
    public void SprintDuringCutFinishesOneShotThenReturnsToSprint()
    {
        ArmCut(-0.9f);
        Axis(GamepadAxis.LeftX, 0.9f); Tick();
        Event(2, (int)KeyboardKey.LeftShift); Tick(dt: 0.1f);
        Assert.Equal(3, _player.SpeedTier);
        Assert.Equal("CutRight", _player.AnimationName);
        float penalizedSpeed = _player.CurrentForwardSpeed;
        Assert.True(penalizedSpeed < _config.PlayerForwardSpeed);
        Assert.True(_player.SteeringRecoveryRemaining > 0);
        Tick(dt: 0.2f);
        Assert.Equal("CutRight", _player.AnimationName);
        Tick(dt: 0.06f);
        Assert.Equal("CarrySprint", _player.AnimationName);
        Assert.Equal(penalizedSpeed, _player.CurrentForwardSpeed);
        Tick(dt: 1f);
        Assert.Equal(_config.PlayerSprintSpeed, _player.CurrentForwardSpeed);
        Event(1, (int)KeyboardKey.LeftShift);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void SprintReversalIsRateLimitedAndReachesFullInputInPointSixSeconds(int fps)
    {
        Axis(GamepadAxis.LeftX, -1f); Tick();
        Event(2, (int)KeyboardKey.LeftShift);
        Axis(GamepadAxis.LeftX, 1f);
        float before = _player.Position.X;
        Tick(dt: 1f / fps);
        Assert.Equal(1f, _input.GetValue("MoveRight"));
        Assert.True(_player.Position.X < before); // Still travelling left despite raw right input.
        Assert.InRange(Field<float>(_player, "_effectiveLateral"), -1f, -0.8f);
        for (int i = 1; i < fps / 2; i++) { Axis(GamepadAxis.LeftX, 1f); Tick(dt: 1f / fps); }
        Assert.InRange(Field<float>(_player, "_effectiveLateral"), 0.65f, 0.68f);
        for (int i = fps / 2; i < fps * 7 / 10; i++) { Axis(GamepadAxis.LeftX, 1f); Tick(dt: 1f / fps); }
        Assert.Equal(1f, Field<float>(_player, "_effectiveLateral"), 5);
        before = _player.Position.X;
        float speedBefore = _player.CurrentForwardSpeed;
        Axis(GamepadAxis.LeftX, 1f);
        Tick(dt: 1f / fps);
        float averageSpeed = (speedBefore + _player.CurrentForwardSpeed) * .5f;
        Assert.Equal(_config.PlayerLateralSpeed / fps * averageSpeed / _player.TargetForwardSpeed,
            _player.Position.X - before, 4);
        Event(1, (int)KeyboardKey.LeftShift);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(1f, 0f)]
    [InlineData(-1f, -0.5f)]
    [InlineData(1f, 0.5f)]
    public void SprintFacingTracksMovementThroughReversalAndUpdatedInput(float initial, float updated)
    {
        Axis(GamepadAxis.LeftX, initial); Tick();
        Event(2, (int)KeyboardKey.LeftShift);
        Axis(GamepadAxis.LeftX, -initial);
        CheckStep();
        Assert.Equal(Math.Sign(initial), Math.Sign(Field<float>(_player, "_effectiveLateral")));
        Assert.Equal(-initial, _input.GetValue(initial < 0f ? "MoveRight" : "MoveLeft"));

        for (int i = 0; i < 12; i++)
        {
            Axis(GamepadAxis.LeftX, updated);
            CheckStep();
        }
        Assert.Equal(updated, Field<float>(_player, "_effectiveLateral"), 4);
        Assert.Equal(-updated * 45f, VisualYaw, 4);
        Event(1, (int)KeyboardKey.LeftShift);

        void CheckStep()
        {
            Vector3 before = _player.Position;
            Tick(dt: 0.05f);
            float steering = Field<float>(_player, "_effectiveLateral");
            float forwardRetention = 1 + (_config.FullSteeringForwardRetention - 1) * Math.Abs(steering);
            float forwardDistance = (before.Z - _player.Position.Z) / forwardRetention;
            float movementSteering = (_player.Position.X - before.X) /
                (_config.PlayerLateralSpeed * forwardDistance / _player.TargetForwardSpeed);
            Assert.Equal(movementSteering * 45f, Field<float>(_player, "_targetRunYaw"), 4);
            Assert.Equal(-movementSteering * 45f, VisualYaw, 4);
        }
    }

    [Fact]
    public void ReleasingSprintRestoresImmediateSteeringAndFastCuts()
    {
        Axis(GamepadAxis.LeftX, -1f); Tick();
        Event(2, (int)KeyboardKey.LeftShift);
        Axis(GamepadAxis.LeftX, 1f); Tick();
        Assert.True(Field<float>(_player, "_effectiveLateral") < 0f);
        Event(1, (int)KeyboardKey.LeftShift);
        float before = _player.Position.X;
        Axis(GamepadAxis.LeftX, 1f);
        Tick();
        Assert.Equal(2, _player.SpeedTier);
        Assert.Equal(_config.PlayerLateralSpeed * Dt * _player.CurrentForwardSpeed / _player.TargetForwardSpeed,
            _player.Position.X - before, 4);
        Assert.True(_player.SteeringRecoveryRemaining > 0);
        Axis(GamepadAxis.LeftX, -1f); Tick();
        Assert.Equal(-1f, Field<float>(_player, "_effectiveLateral"));
        Assert.Equal("CutLeft", _player.AnimationName);
    }

    [Fact]
    public void SmallSprintCorrectionSettlesQuicklyAndResetClearsSteering()
    {
        Axis(GamepadAxis.LeftX, 0.5f); Tick();
        Event(2, (int)KeyboardKey.LeftShift);
        Axis(GamepadAxis.LeftX, 0.75f); Tick(dt: 0.1f);
        Assert.Equal(0.75f, Field<float>(_player, "_effectiveLateral"), 4);
        _player.Reset();
        Assert.Equal(0f, Field<float>(_player, "_effectiveLateral"));
        Event(1, (int)KeyboardKey.LeftShift);
    }

    [Theory]
    [InlineData(-1f, "CutRight", false)]
    [InlineData(1f, "CutLeft", false)]
    [InlineData(-1f, "CutRight", true)]
    [InlineData(1f, "CutLeft", true)]
    public void CutFreezesIncomingYawAndHandsOffBeforeLocomotion(float incomingSide, string cut, bool sprint)
    {
        Axis(GamepadAxis.LeftX, incomingSide);
        Event(2, (int)KeyboardKey.LeftShift);
        Tick(dt: 0.6f);
        float incomingYaw = Field<float>(_player, "_currentRunYaw");
        Assert.Equal(incomingSide * 45f, incomingYaw);
        Event(1, (int)KeyboardKey.LeftShift);
        Axis(GamepadAxis.LeftX, -incomingSide);
        Tick(dt: Dt);
        Assert.Equal(cut, _player.AnimationName);
        CheckFrozen();

        if (sprint) Event(2, (int)KeyboardKey.LeftShift);
        for (int i = 0; i < 20; i++)
        {
            // Live input alternates between the incoming and destination directions.
            Axis(GamepadAxis.LeftX, i % 2 == 0 ? incomingSide : -incomingSide);
            Tick(dt: Dt);
            Assert.Equal(cut, _player.AnimationName);
            CheckFrozen();
        }

        Axis(GamepadAxis.LeftX, -incomingSide);
        Tick(dt: Field<float>(_player, "_cutRemaining"));
        Assert.Equal(sprint ? "CarrySprint" : "CarryRun", _player.AnimationName);
        float destination = -incomingSide * 45f;
        Assert.Equal(destination, Field<float>(_player, "_currentRunYaw"));
        Assert.Equal(destination, Field<float>(_player, "_targetRunYaw"));
        Assert.Equal(-destination, VisualYaw);

        Event(1, (int)KeyboardKey.LeftShift);
        Axis(GamepadAxis.LeftX, -incomingSide);
        Tick();
        Assert.Equal("CarryRun", _player.AnimationName);
        Assert.Equal(destination, Field<float>(_player, "_currentRunYaw"));
        Assert.Equal(destination, Field<float>(_player, "_targetRunYaw"));

        void CheckFrozen()
        {
            Assert.Equal(incomingYaw, Field<float>(_player, "_currentRunYaw"));
            Assert.Equal(incomingYaw, Field<float>(_player, "_targetRunYaw"));
            Assert.Equal(-incomingYaw, VisualYaw);
        }
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void EndZoneCutKeepsIncomingYawUntilHandoff(float incomingSide)
    {
        ArmCut(incomingSide);
        float incomingYaw = Field<float>(_player, "_currentRunYaw");
        Axis(GamepadAxis.LeftX, -incomingSide); Tick(dt: 0f);
        _player.RunIntoEndZone(0.1f, -100f);
        Assert.Equal(incomingYaw, Field<float>(_player, "_currentRunYaw"));
        _player.RunIntoEndZone(Field<float>(_player, "_cutRemaining"), -100f);
        Assert.Equal("CarryRun", _player.AnimationName);
        Assert.Equal(-incomingSide * 45f, Field<float>(_player, "_currentRunYaw"));
        Assert.Equal(-incomingSide * 45f, Field<float>(_player, "_targetRunYaw"));
    }

    private float VisualYaw => (float)typeof(BallCarrier)
        .GetProperty("VisualYawDegrees", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_player)!;

    [Theory]
    [InlineData(KeyboardKey.W, 0f)]
    [InlineData(KeyboardKey.A, -45f)]
    [InlineData(KeyboardKey.D, 45f)]
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
        Assert.Equal(lateral * 45f, Field<float>(_player, "_targetRunYaw"), 4);
        Assert.Equal(2, _player.SpeedTier);
        Assert.Equal(-_config.PlayerForwardSpeed * Dt * (1f - 0.25f * Math.Abs(axis)), _player.Position.Z, 4);
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
        Assert.Equal(-45f, Field<float>(_player, "_targetRunYaw"));
        Assert.Equal(singleStep, Field<float>(_player, "_currentRunYaw"), 4);
        Tick();
        Assert.InRange(Field<float>(_player, "_currentRunYaw"), -44.99f, singleStep - 0.01f);
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
        Assert.Equal(spinDirection * _config.PlayerSpinSpeed * Dt, _player.Position.X - before.X, 4);
        Assert.Equal(before.Z, _player.Position.Z);
        float runYaw = Field<float>(_player, "_currentRunYaw");
        Assert.Equal(45f, Field<float>(_player, "_targetRunYaw"));
        Assert.Equal(-runYaw + spinDirection * 360f * (Dt / 0.45f), VisualYaw, 3);
        Event(1, (int)KeyboardKey.D);
        Event(2, (int)KeyboardKey.A);
        Tick(dt: 0.2f);
        Assert.Equal(-45f, Field<float>(_player, "_targetRunYaw"));
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
    public void SprintHeadFakeCancelsMoves(bool gamepad)
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
    public void AiDefendersPrepareButMissingVisualPoseCannotInventContact()
    {
        var game = new TackleAlleyGame(_config, _input, new AssetManager(new AssetConfig()));
        var defender = Field<Opponent[]>(game, "_opponents")[0];
        var runner = Field<BallCarrier>(game, "_player");
        var position = typeof(Opponent).GetField("_position", BindingFlags.Instance | BindingFlags.NonPublic)!;
        position.SetValue(defender, runner.Position + new Vector3(0, 0, -3));
        game.Update(0);
        Assert.Equal(DefenderState.Locomotion, defender.State); // No predicted direction yet.
        game.Update(1f / 60f);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        Assert.False(game.GameOver);
        // Establish stable observed motion before testing the interception decision.
        for (int i = 0; i < 30; i++)
        {
            position.SetValue(defender, runner.Position + new Vector3(0, 0, -4));
            game.Update(1f / 60f);
        }
        position.SetValue(defender, runner.Position + new Vector3(0, 0, -2));
        game.Update(1f / 60f);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
        Assert.False(game.GameOver);
        position.SetValue(defender, runner.Position);
        game.Update(0);
        Assert.False(game.GameOver); // Contact requires real body capsules, not only coincident origins.
        Assert.False(game.Touchdown);
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
    [InlineData("Touchdown", 2.5f)]
    [InlineData("GameOver", 2.5f)]
    [InlineData("Touchdown", 4f)]
    [InlineData("GameOver", 4f)]
    public void EndStatesRestartAfterConfiguredDelay(string state, float delay)
    {
        _config.AutoRestartDelay = delay;
        var menus = MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json"));
        var app = new GameApplication(new GameConfig(), _bindings, menus, new AssetConfig(), _config,
            ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json")));
        var game = Field<TackleAlleyGame>(app, "_game");
        var stateField = typeof(GameApplication).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!;
        stateField.SetValue(app, Enum.Parse(stateField.FieldType, state));
        typeof(TackleAlleyGame).GetProperty(state)!.SetValue(game, true);
        if (state == "Touchdown")
        {
            var carrier = Field<BallCarrier>(game, "_player");
            var field = Field<FootballField>(game, "_field");
            float stop = field.GoalLineZ - field.EndZoneLength * _config.EndZoneStopFraction;
            typeof(BallCarrier).GetProperty("Position")!.SetValue(carrier, new Vector3(0, 0, stop));
            typeof(TackleAlleyGame).GetProperty("EndStateElapsed")!.SetValue(game, delay);
            Invoke(app, "Update", .1f);
            Assert.Equal("Touchdown", stateField.GetValue(app)!.ToString());
            Assert.False(game.OutcomeCelebrationComplete);
            carrier.RunIntoEndZone(3f, stop);
            Assert.True(game.OutcomeCelebrationComplete);
        }
        typeof(TackleAlleyGame).GetProperty("EndStateElapsed")!.SetValue(game, delay - .01f);
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
        Assert.Equal(expected, (float)normalize.Invoke(null, [delta, sensitivity, new TackleAlleyConfig().MouseGestureNoisePixels])!, 4);
    }
}
