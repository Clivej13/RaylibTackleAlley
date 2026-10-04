# User settings

Persistence uses JsonSettingsStore<UserSettings> from RaylibGameFramework.Configuration 0.1.1.
The package owns JSON reads and atomic replacement; UserSettingsSession owns preference
selection, defaults, validation, migration, change detection and retry policy.

Fullscreen, VSync, the debug master switch, CameraMode, LeftStickSensitivity, shared movement scales and binding overrides save to
%LOCALAPPDATA%/RaylibTackleAlley/settings.json. Gameplay tuning remains authored in config.json.
Load must receive the current input.json defaults before constructing the InputController.

## Session input choice

The title screen accepts Start/Menu (MiddleRight on gamepad 0) for controller play
or Space for keyboard and mouse. The first choice locks that input family for the
whole process, including returning to the main menu and restarting runs. Restart
the game to choose again; disconnecting a controller does not switch to the mouse.

SessionInput creates a filtered runtime binding config while preserving both
families in the saved config. Gameplay movement, gestures, camera look, pause and
menu actions use that runtime controller. Rebinds merge back only into the selected
family. Title input is consumed before menu navigation.

Input 0.1.5 has no device-lock API, so SessionInput pins its ActiveDeviceFamily UI
property. Menus 0.1.3 reads mouse events directly; ControllerMenu routes controller
navigation through the pinned package's private selection/activation methods.
These compatibility adapters need checking when either package is upgraded.
Keyboard/mouse sessions retain the native menu mouse layout and hit testing.

## Shared movement settings

Options > Movement groups Player Speed (50–150%), Player Acceleration (25–200%),
and Left Stick Sensitivity. The speed and acceleration sliders affect all players
on both teams; ratings supply the individual differences. Both default to 100%.
Changes refresh the active carrier and every defender without resetting the run,
apply to subsequent returner selections, and save as MovementSpeedScale and
MovementAccelerationScale. Missing fields retain authored values; invalid values
retain defaults and report a settings error.

## Left-stick sensitivity

Options > Movement (also accessible from Pause) has a 25–100% sensitivity slider in 5% steps; shipped default is 70%.
Lower values soften partial analogue movement inputs. Changes apply to the active level
and save between sessions; invalid saved values retain the authored default.
Keyboard movement and right-stick gestures keep their own bindings and response.
At values below 100%, stick turns while moving are limited to 240 degrees/second when
running and 180 when sprinting, adjusted by Agility. The 100% endpoint retains the
original steering response. Restarting or changing returner retains the selected sensitivity.

## Pause menu

The controller Menu/Start button (MiddleRight) opens and resumes the in-game menu;
keyboard Escape retains that function. B remains MenuBack within menus.
Pause order is Resume, Restart, Options, Main Menu, Exit. Exit closes the game.
Legacy full binding snapshots with the old B or View/Back pause defaults migrate to Menu/Start;
explicit modern overrides and other custom pause bindings remain supported.

## Input overrides and migration

New saves contain BindingOverrides: only differences from the first default binding for each
action/device family. They do not contain a full Input snapshot. Loading applies these overrides
over current shipped defaults, preserving new actions and secondary defaults such as D-pad
plus stick menu navigation. Empty lists leave all defaults intact.

Legacy Input.Bindings lists still load. The first valid entry per action/device family is
treated as a saved preference; secondary legacy entries do not replace current fallback bindings.
Unknown actions, invalid devices, blank/unknown input names, and disabled keys/buttons are
ignored with a warning while their defaults remain. Valid rebinds are retained. An existing
action can gain a binding for a previously unbound device family. Action/device/input matching
follows the Input package's case-insensitive semantics.

On the next preference change, legacy data is saved in the override format. Legacy files cannot
tell an old default apart from a deliberate rebind, so saved first bindings take precedence over
changed defaults for existing actions. New actions still receive their defaults.
Older executables that only understand Input.Bindings will not read the new rebind overrides.
Malformed JSON or a null legacy Bindings list is rejected without replacing defaults or
automatically overwriting the bad file. Delete settings.json to restore all shipped defaults.

## Save retry

Successful persistence and attempted state are tracked separately. A failed write retains the
last successfully saved state and leaves pending changes eligible for retry. Unchanged failed
data is retried at most once every two seconds while the menu processes settings; a changed
preference can trigger an immediate attempt. Shutdown requests one immediate retry regardless
of the delay. Unchanged successfully saved data is not rewritten. Unchanged missing/malformed
initial saves are left alone until a preference changes. Failures appear in Options and logs.

Options > Debug controls diagnostics, initially using DrawGameplayDebug from config.json.
ShowPlayerProfiles and DrawTackleAimingDebug remain category filters under the master switch.
Ragdoll diagnostics also require debug. The ordinary HUD and boundary markings remain visible.
