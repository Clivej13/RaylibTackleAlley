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
        new InputController(input).ApplyRebind(new("Sprint", "Keyboard", "Space"));
        session.SaveIfChanged(config, tuning, input);

        var restoredConfig = new GameConfig();
        var restoredTuning = new TackleAlleyConfig();
        var restoredInput = new InputConfig();
        new UserSettingsSession(SettingsPath).Load(restoredConfig, restoredTuning, restoredInput);
        Assert.True(restoredConfig.Fullscreen);
        Assert.False(restoredConfig.VSync);
        Assert.True(restoredTuning.DrawGameplayDebug);
        Assert.Equal("Space", Assert.Single(restoredInput.Bindings).Input);
        Assert.Equal(new TackleAlleyConfig().OpponentRunSpeed, restoredTuning.OpponentRunSpeed);
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
