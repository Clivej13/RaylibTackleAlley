# User settings

Persistence uses JsonSettingsStore<UserSettings> from RaylibGameFramework.Configuration 0.1.1.
The package owns JSON file reads and replacement writes; UserSettingsSession owns game-specific
preferences, defaults, validation, change detection and error reporting.

Fullscreen, VSync, the debug master switch and control bindings are saved when changed to
%LOCALAPPDATA%/RaylibTackleAlley/settings.json. Existing settings files remain compatible.
Authored config.json and input.json remain defaults; gameplay tuning is not written back.
Delete settings.json to restore defaults. Malformed saves report an error and remain untouched
until a preference changes. Options displays failures; stderr includes the path and cause.

Options > Debug controls diagnostics, initially using DrawGameplayDebug from config.json.
ShowPlayerProfiles and DrawTackleAimingDebug are category filters beneath that master switch.
Ragdoll command-line diagnostics also require debug. The ordinary HUD and out-of-bounds
markings remain visible.
