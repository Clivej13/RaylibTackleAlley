using System.Text.Json;
using RaylibGameFramework.Logging;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Game;

namespace RaylibTackleAlley.Application;

public sealed class UserSettings
{
    public bool? Fullscreen { get; set; }
    public bool? VSync { get; set; }
    public bool? DebugEnabled { get; set; }
    public InputConfig? Input { get; set; }
}

public sealed class UserSettingsSession
{
    private readonly RotatingFileLogger? _log;
    private readonly JsonSettingsStore<UserSettings> _store;
    private string? _lastSaved;
    public string? Error { get; private set; }
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaylibTackleAlley", "settings.json");

    public UserSettingsSession(string path, RotatingFileLogger? log = null)
    {
        _store = new(path);
        _log = log;
    }

    public void Load(GameConfig config, TackleAlleyConfig tuning, InputConfig input)
    {
        try
        {
            _log?.Write("INFO", $"Loading settings: {_store.FilePath}");
            var saved = _store.Load();
            if (saved is null) return;
            if (saved.Input is { } bindings && (bindings.Bindings is null ||
                bindings.Bindings.Any(b => b is null || string.IsNullOrWhiteSpace(b.Action) ||
                    string.IsNullOrWhiteSpace(b.Device) || string.IsNullOrWhiteSpace(b.Input))))
                throw new JsonException("Saved input bindings are incomplete.");
            config.Fullscreen = saved.Fullscreen ?? config.Fullscreen;
            config.VSync = saved.VSync ?? config.VSync;
            tuning.DrawGameplayDebug = saved.DebugEnabled ?? tuning.DrawGameplayDebug;
            if (saved.Input is { } savedInput)
            {
                input.Bindings.Clear();
                input.Bindings.AddRange(savedInput.Bindings);
            }
            Error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Report("load", e);
        }
        finally
        {
            _lastSaved = JsonSerializer.Serialize(Capture(config, tuning, input));
        }
    }

    public void SaveIfChanged(GameConfig config, TackleAlleyConfig tuning, InputConfig input)
    {
        var settings = Capture(config, tuning, input);
        string snapshot = JsonSerializer.Serialize(settings);
        if (snapshot == _lastSaved) return;
        try
        {
            _store.Save(settings);
            _log?.Write("INFO", "Settings saved");
            _lastSaved = snapshot;
            Error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Avoid retrying a failed write every menu frame. A changed setting or
            // a new session will retry, and the menu displays the failure.
            _lastSaved = snapshot;
            Report("save", e);
        }
    }

    private static UserSettings Capture(GameConfig config, TackleAlleyConfig tuning, InputConfig input) => new()
    {
        Fullscreen = config.Fullscreen, VSync = config.VSync,
        DebugEnabled = tuning.DrawGameplayDebug, Input = input
    };

    private void Report(string operation, Exception exception)
    {
        Error = $"Could not {operation} settings.";
        _log?.Write("ERROR", $"{Error} Path: {_store.FilePath}", exception);
        Console.Error.WriteLine($"{Error} '{_store.FilePath}': {exception.Message}");
    }
}
