using System.Text.Json;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Application;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class SettingsFoundationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SettingsFoundationTests", Guid.NewGuid().ToString("N"));
    private string PathToSettings => Path.Combine(_directory, "settings.json");
    private static InputConfig Defaults() => InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json"));
    private static InputBinding Binding(string action, string input, string device = "Keyboard") =>
        new() { Action = action, Input = input, Device = device };

    [Fact]
    public void LegacyFullBindingsPreserveRebindAndNewDefaultActionsAndSecondaryBindings()
    {
        var old = Defaults();
        new InputController(old).ApplyRebind(new("Sprint", "Keyboard", "Space"));
        new JsonSettingsStore<UserSettings>(PathToSettings).Save(new() { Input = old });
        var current = Defaults();
        current.Bindings.Add(Binding("NewAction", "N"));
        var session = new UserSettingsSession(PathToSettings);
        session.Load(new(), new(), current);
        Assert.Equal("Space", new InputController(current).GetBinding("Sprint", InputDeviceFamily.KeyboardMouse)!.Input);
        Assert.Contains(current.Bindings, b => b.Action == "NewAction" && b.Input == "N");
        Assert.Contains(current.Bindings, b => b.Action == "MenuUp" && b.Device == "GamepadButton");
        Assert.Contains(current.Bindings, b => b.Action == "MenuUp" && b.Device == "GamepadAxis");
        Assert.Equal(old.Bindings.Count + 1, current.Bindings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyPersistedBindingsKeepEveryShippedDefault(bool legacy)
    {
        new JsonSettingsStore<UserSettings>(PathToSettings).Save(legacy
            ? new() { Input = new() } : new() { BindingOverrides = [] });
        var input = Defaults();
        string before = JsonSerializer.Serialize(input);
        new UserSettingsSession(PathToSettings).Load(new(), new(), input);
        Assert.Equal(before, JsonSerializer.Serialize(input));
    }

    [Fact]
    public void OnlyChangedBindingIsSavedAndAllOtherDefaultsRemain()
    {
        var input = Defaults();
        var config = new GameConfig();
        var tuning = new TackleAlleyConfig();
        var session = new UserSettingsSession(PathToSettings);
        session.Load(config, tuning, input);
        new InputController(input).ApplyRebind(new("Sprint", "Keyboard", "Space"));
        session.SaveIfChanged(config, tuning, input);
        var saved = new JsonSettingsStore<UserSettings>(PathToSettings).Load()!;
        Assert.Null(saved.Input);
        Assert.Equal("Sprint", Assert.Single(saved.BindingOverrides!).Action);
        var restored = Defaults();
        new UserSettingsSession(PathToSettings).Load(new(), new(), restored);
        Assert.Equal(JsonSerializer.Serialize(input), JsonSerializer.Serialize(restored));
    }

    [Theory]
    [InlineData("Null", "Keyboard")]
    [InlineData("NotAKey", "Keyboard")]
    [InlineData("Unknown", "GamepadButton")]
    [InlineData("", "Keyboard")]
    [InlineData("Enter", "UnknownDevice")]
    public void InvalidNavigationOverrideKeepsWorkingDefaults(string value, string device)
    {
        new JsonSettingsStore<UserSettings>(PathToSettings).Save(new()
        {
            BindingOverrides = [Binding("MenuConfirm", value, device), Binding("Sprint", "Space")]
        });
        var input = Defaults();
        var session = new UserSettingsSession(PathToSettings);
        session.Load(new(), new(), input);
        Assert.NotNull(session.Error);
        Assert.Contains(input.Bindings, b => b.Action == "MenuConfirm" && b.Input == "Enter");
        Assert.Contains(input.Bindings, b => b.Action == "MenuConfirm" && b.Input == "RightFaceDown");
        Assert.Contains(input.Bindings, b => b.Action == "Sprint" && b.Input == "Space");
    }

    [Fact]
    public void FailedUnchangedSaveRetriesAfterRecoveryWithoutPerFrameWrites()
    {
        Directory.CreateDirectory(_directory);
        var clock = new Clock();
        var input = Defaults();
        var config = new GameConfig();
        var tuning = new TackleAlleyConfig();
        var session = new UserSettingsSession(PathToSettings, clock: clock);
        session.Load(config, tuning, input);
        Directory.CreateDirectory(PathToSettings); // Blocks atomic file replacement.
        config.Fullscreen = true;
        session.SaveIfChanged(config, tuning, input);
        Assert.Equal("Could not save settings.", session.Error);
        Directory.Delete(PathToSettings);
        for (int i = 0; i < 120; i++) session.SaveIfChanged(config, tuning, input);
        Assert.False(File.Exists(PathToSettings));
        clock.Now = clock.Now.AddSeconds(2);
        session.SaveIfChanged(config, tuning, input);
        Assert.Null(session.Error);
        Assert.True(new JsonSettingsStore<UserSettings>(PathToSettings).Load()!.Fullscreen);
        // A successful unchanged save should not write again even after the retry interval.
        File.Delete(PathToSettings);
        clock.Now = clock.Now.AddSeconds(3);
        session.SaveIfChanged(config, tuning, input);
        Assert.False(File.Exists(PathToSettings));
    }

    [Fact]
    public void ShutdownRetriesUnchangedPendingSettingsImmediately()
    {
        Directory.CreateDirectory(_directory);
        var session = new UserSettingsSession(PathToSettings, clock: new Clock());
        var input = Defaults();
        var config = new GameConfig();
        var tuning = new TackleAlleyConfig();
        session.Load(config, tuning, input);
        Directory.CreateDirectory(PathToSettings);
        config.VSync = false;
        session.SaveIfChanged(config, tuning, input);
        Assert.NotNull(session.Error);
        Directory.Delete(PathToSettings);
        session.SaveIfChanged(config, tuning, input, retryNow: true);
        Assert.Null(session.Error);
        Assert.False(new JsonSettingsStore<UserSettings>(PathToSettings).Load()!.VSync);
    }

    [Fact]
    public void ValidCaseInsensitiveAndNewDeviceFamilyRebindsSurvive()
    {
        var input = new InputConfig();
        input.Bindings.Add(Binding("Sprint", "LeftShift"));
        var config = new GameConfig();
        var tuning = new TackleAlleyConfig();
        var session = new UserSettingsSession(PathToSettings);
        session.Load(config, tuning, input);
        new InputController(input).ApplyRebind(new("sprint", "gamepadaxis", "righttrigger"));
        session.SaveIfChanged(config, tuning, input);
        var restored = new InputConfig();
        restored.Bindings.Add(Binding("Sprint", "LeftShift"));
        new UserSettingsSession(PathToSettings).Load(new(), new(), restored);
        Assert.Equal(2, restored.Bindings.Count);
        Assert.Equal("righttrigger", new InputController(restored).GetBinding("Sprint", InputDeviceFamily.Gamepad)!.Input);
        Assert.Equal("LeftShift", new InputController(restored).GetBinding("Sprint", InputDeviceFamily.KeyboardMouse)!.Input);
    }

    [Fact]
    public void LegacyFileIsWrittenAsOverridesOnNextPreferenceChange()
    {
        var old = Defaults();
        new InputController(old).ApplyRebind(new("Sprint", "Keyboard", "Space"));
        new JsonSettingsStore<UserSettings>(PathToSettings).Save(new() { Input = old });
        var input = Defaults();
        var config = new GameConfig();
        var tuning = new TackleAlleyConfig();
        var session = new UserSettingsSession(PathToSettings);
        session.Load(config, tuning, input);
        config.Fullscreen = !config.Fullscreen;
        session.SaveIfChanged(config, tuning, input);
        string json = File.ReadAllText(PathToSettings);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("Input", out _));
        var migrated = new JsonSettingsStore<UserSettings>(PathToSettings).Load()!;
        Assert.Equal("Space", Assert.Single(migrated.BindingOverrides!).Input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Hybrid")]
    [InlineData("99")]
    public void MissingOrInvalidCameraPreferenceRetainsConfiguredDefault(string? mode)
    {
        new JsonSettingsStore<UserSettings>(PathToSettings).Save(new() { CameraMode = mode, VSync = false });
        var config = new GameConfig();
        var tuning = new TackleAlleyConfig { DefaultCameraMode = CameraMode.Far };
        new UserSettingsSession(PathToSettings).Load(config, tuning, Defaults());
        Assert.Equal(CameraMode.Far, tuning.DefaultCameraMode);
        Assert.False(config.VSync);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
