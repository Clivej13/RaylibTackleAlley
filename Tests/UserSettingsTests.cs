using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Application;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class UserSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TackleAlleySettingsTests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void OptionsAndPackageRebindingsSurviveANewSession()
    {
        var config = new GameConfig();
        var tuning = new TackleAlleyConfig();
        var input = new InputConfig();
        input.Bindings.Add(new() { Action = "Sprint", Device = "Keyboard", Input = "LeftShift" });
        var session = new UserSettingsSession(SettingsPath);
        session.Load(config, tuning, input);
        config.Fullscreen = true;
        config.VSync = false;
        tuning.DrawGameplayDebug = true;
        tuning.LeftStickSensitivity = .45f;
        tuning.ThirdPersonCamera.ShoulderAngleDegrees = 125;
        new InputController(input).ApplyRebind(new("Sprint", "Keyboard", "Space"));
        session.SaveIfChanged(config, tuning, input);

        var restoredConfig = new GameConfig();
        var restoredTuning = new TackleAlleyConfig();
        var restoredInput = new InputConfig();
        restoredInput.Bindings.Add(new() { Action = "Sprint", Device = "Keyboard", Input = "LeftShift" });
        new UserSettingsSession(SettingsPath).Load(restoredConfig, restoredTuning, restoredInput);
        Assert.True(restoredConfig.Fullscreen);
        Assert.False(restoredConfig.VSync);
        Assert.True(restoredTuning.DrawGameplayDebug);
        Assert.Equal(.45f, restoredTuning.LeftStickSensitivity);
        Assert.Equal(125, restoredTuning.ThirdPersonCamera.ShoulderAngleDegrees);
        Assert.Equal("Space", Assert.Single(restoredInput.Bindings).Input);
        Assert.Equal(new TackleAlleyConfig().PlayerForwardSpeed, restoredTuning.PlayerForwardSpeed);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Input\":{\"Bindings\":null}}")]
    public void InvalidSaveKeepsDefaultsAndIsNotOverwrittenUntilSettingsChange(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, json);
        var config = new GameConfig { VSync = false };
        var tuning = new TackleAlleyConfig();
        var input = new InputConfig();
        var session = new UserSettingsSession(SettingsPath);
        session.Load(config, tuning, input);
        Assert.NotNull(session.Error);
        Assert.False(config.VSync);
        session.SaveIfChanged(config, tuning, input);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void MissingSavePreservesAuthoredDefaultsAndUnchangedFramesDoNotWrite()
    {
        var config = new GameConfig { Fullscreen = true };
        var tuning = new TackleAlleyConfig { DrawGameplayDebug = true };
        var input = new InputConfig();
        var session = new UserSettingsSession(SettingsPath);
        session.Load(config, tuning, input);
        session.SaveIfChanged(config, tuning, input);
        Assert.True(config.Fullscreen);
        Assert.True(tuning.DrawGameplayDebug);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void InvalidSavedMovementScalesKeepAuthoredDefaults()
    {
        new JsonSettingsStore<UserSettings>(SettingsPath).Save(new()
        {
            MovementSpeedScale = -1, MovementAccelerationScale = 5, LeftStickSensitivity = .5f
        });
        var tuning = new TackleAlleyConfig();
        var session = new UserSettingsSession(SettingsPath);
        session.Load(new(), tuning, new());
        Assert.Equal(1, tuning.MovementScaling.SpeedScale);
        Assert.Equal(1, tuning.MovementScaling.AccelerationScale);
        Assert.Equal(.5f, tuning.LeftStickSensitivity);
        Assert.Contains("movement scaling", session.Error!);
    }

    [Theory]
    [InlineData(false, "RightFaceRight", "MiddleRight")]
    [InlineData(false, "MiddleLeft", "MiddleRight")]
    [InlineData(true, "MiddleLeft", "MiddleLeft")]
    [InlineData(false, "MiddleRight", "MiddleRight")]
    [InlineData(true, "MiddleRight", "MiddleRight")]
    public void PauseDefaultMigratesFromLegacySnapshotsWithoutLosingCustomRebinds(bool modern, string savedButton, string expected)
    {
        var binding = new InputBinding { Device = "GamepadButton", Input = savedButton, Action = "Pause" };
        var saved = modern ? new UserSettings { BindingOverrides = [binding] }
            : new UserSettings { Input = new InputConfig { Bindings = [binding] } };
        new JsonSettingsStore<UserSettings>(SettingsPath).Save(saved);
        var input = InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json"));
        new UserSettingsSession(SettingsPath).Load(new(), new(), input);
        var controller = new InputController(input);
        Assert.Equal(expected, controller.GetBinding("Pause", InputDeviceFamily.Gamepad)!.Input);
        Assert.Equal("RightFaceRight", controller.GetBinding("MenuBack", InputDeviceFamily.Gamepad)!.Input);
    }

    [Fact]
    public void FailedSaveIsReportedWithoutTerminatingGame()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "blocked"), "file");
        var session = new UserSettingsSession(Path.Combine(_directory, "blocked", "settings.json"));
        session.SaveIfChanged(new(), new(), new());
        Assert.Equal("Could not save settings.", session.Error);
    }

    [Fact]
    public void GenericStoreReplacesExistingSaveAndLeavesNoTemporaryFiles()
    {
        var store = new JsonSettingsStore<UserSettings>(SettingsPath);
        store.Save(new() { DebugEnabled = true });
        store.Save(new() { DebugEnabled = false });
        Assert.False(store.Load()!.DebugEnabled);
        Assert.Single(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
