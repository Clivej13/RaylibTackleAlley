using System.Text.Json;
using System.Text.Json.Serialization;
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
    public string? CameraMode { get; set; }
    public float? CameraPitchDegrees { get; set; }
    public float? ShoulderAngleDegrees { get; set; }
    public float? LeftStickSensitivity { get; set; }
    public float? MovementSpeedScale { get; set; }
    public float? MovementAccelerationScale { get; set; }
    public List<InputBinding>? BindingOverrides { get; set; }
    // Read compatibility with the original full-binding-list format.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InputConfig? Input { get; set; }
}

public sealed class UserSettingsSession
{
    private readonly RotatingFileLogger? _log;
    private readonly JsonSettingsStore<UserSettings> _store;
    private readonly TimeProvider _clock;
    private List<InputBinding>? _defaults;
    private string? _initialState, _lastSaved, _lastAttempted;
    private DateTimeOffset _retryAfter;
    public string? Error { get; private set; }
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaylibTackleAlley", "settings.json");

    public UserSettingsSession(string path, RotatingFileLogger? log = null, TimeProvider? clock = null)
    {
        _store = new(path);
        _log = log;
        _clock = clock ?? TimeProvider.System;
    }

    public void Load(GameConfig config, TackleAlleyConfig tuning, InputConfig input)
    {
        // Must be called with this version's shipped defaults, before InputController construction.
        _defaults = input.Bindings.ToList();
        _lastSaved = _lastAttempted = null;
        bool loaded = false;
        try
        {
            _log?.Write("INFO", $"Loading settings: {_store.FilePath}");
            var saved = _store.Load();
            if (saved is null) return;
            if (saved.Input is { Bindings: null })
                throw new JsonException("Saved input bindings are incomplete.");
            config.Fullscreen = saved.Fullscreen ?? config.Fullscreen;
            config.VSync = saved.VSync ?? config.VSync;
            tuning.DrawGameplayDebug = saved.DebugEnabled ?? tuning.DrawGameplayDebug;
            var overrides = saved.BindingOverrides ?? saved.Input?.Bindings ?? [];
            // Old full snapshots included B or View/Back as the shipped Pause default.
            // Migrate that default; explicit modern rebind overrides keep their meaning.
            if (saved.BindingOverrides is null && input.Bindings.Any(b =>
                string.Equals(b.Action, "Pause", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(b.Device, "GamepadButton", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(b.Input, "MiddleRight", StringComparison.OrdinalIgnoreCase)))
                overrides = overrides.Where(b => b is null ||
                    !string.Equals(b.Action, "Pause", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(b.Device, "GamepadButton", StringComparison.OrdinalIgnoreCase) ||
                    (!string.Equals(b.Input, "RightFaceRight", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(b.Input, "MiddleLeft", StringComparison.OrdinalIgnoreCase))).ToList();
            bool repaired = InputBindingOverrides.Apply(input, overrides);
            bool invalidCamera = false;
            if (saved.CameraMode is { } camera)
            {
                if (Enum.TryParse<CameraMode>(camera, true, out var mode) && Enum.IsDefined(mode))
                    tuning.DefaultCameraMode = mode;
                else invalidCamera = true;
            }
            if (saved.ShoulderAngleDegrees is { } angle)
            {
                if (ThirdPersonCameraTuning.ValidShoulderAngle(angle))
                    tuning.ThirdPersonCamera.ShoulderAngleDegrees = MathF.Round(angle / 5) * 5;
                else invalidCamera = true;
            }
            if (saved.CameraPitchDegrees is { } pitch)
            {
                if (float.IsFinite(pitch) && pitch >= tuning.ThirdPersonCamera.MinPitchDegrees && pitch <= tuning.ThirdPersonCamera.MaxPitchDegrees)
                    tuning.ThirdPersonCamera.InitialPitchDegrees = pitch;
                else invalidCamera = true;
            }
            bool invalidSensitivity = false;
            if (saved.LeftStickSensitivity is { } sensitivity)
            {
                if (float.IsFinite(sensitivity) && sensitivity >= .25f && sensitivity <= 1)
                    tuning.LeftStickSensitivity = sensitivity;
                else invalidSensitivity = true;
            }
            bool invalidMovement = false;
            if (saved.MovementSpeedScale is { } speedScale)
            {
                if (MovementScaling.ValidSpeedScale(speedScale)) tuning.MovementScaling.SpeedScale = speedScale;
                else invalidMovement = true;
            }
            if (saved.MovementAccelerationScale is { } accelerationScale)
            {
                if (MovementScaling.ValidAccelerationScale(accelerationScale)) tuning.MovementScaling.AccelerationScale = accelerationScale;
                else invalidMovement = true;
            }
            loaded = true;
            Error = repaired ? "Some saved bindings were invalid; defaults retained."
                : invalidCamera ? "Saved camera setting was invalid; default retained."
                : invalidSensitivity ? "Saved stick sensitivity was invalid; default retained."
                : invalidMovement ? "Saved movement scaling was invalid; defaults retained." : null;
            if (Error is not null) _log?.Write("WARN", Error);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Report("load", e);
        }
        finally
        {
            _initialState = JsonSerializer.Serialize(Capture(config, tuning, input));
            if (loaded) _lastSaved = _initialState;
        }
    }

    public void SaveIfChanged(GameConfig config, TackleAlleyConfig tuning, InputConfig input, bool retryNow = false)
    {
        _defaults ??= input.Bindings.ToList();
        var settings = Capture(config, tuning, input);
        string snapshot = JsonSerializer.Serialize(settings);
        if (snapshot == _lastSaved || (_lastAttempted is null && snapshot == _initialState)) return;
        if (!retryNow && snapshot == _lastAttempted && _clock.GetUtcNow() < _retryAfter) return;
        _lastAttempted = snapshot;
        try
        {
            _store.Save(settings);
            _log?.Write("INFO", "Settings saved");
            _lastSaved = snapshot;
            Error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Retain the last successful state. Retry the same pending data at most
            // once every two seconds, and allow one immediate shutdown retry.
            _retryAfter = _clock.GetUtcNow().AddSeconds(2);
            Report("save", e);
        }
    }

    private UserSettings Capture(GameConfig config, TackleAlleyConfig tuning, InputConfig input) => new()
    {
        Fullscreen = config.Fullscreen, VSync = config.VSync,
        DebugEnabled = tuning.DrawGameplayDebug,
        CameraMode = tuning.DefaultCameraMode.ToString(),
        ShoulderAngleDegrees = tuning.ThirdPersonCamera.ShoulderAngleDegrees,
        CameraPitchDegrees = tuning.ThirdPersonCamera.InitialPitchDegrees,
        LeftStickSensitivity = tuning.LeftStickSensitivity,
        MovementSpeedScale = tuning.MovementScaling.SpeedScale,
        MovementAccelerationScale = tuning.MovementScaling.AccelerationScale,
        BindingOverrides = InputBindingOverrides.Capture(_defaults!, input)
    };

    private void Report(string operation, Exception exception)
    {
        Error = $"Could not {operation} settings.";
        _log?.Write("ERROR", $"{Error} Path: {_store.FilePath}", exception);
        Console.Error.WriteLine($"{Error} '{_store.FilePath}': {exception.Message}");
    }
}
