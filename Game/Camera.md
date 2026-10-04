# Gameplay cameras

Options contains one Camera selector: Third Person / Close / Medium / Far.
The same Options menu is accessible from Pause. Selection applies immediately without
advancing the simulation, persists through UserSettingsSession, and restores before game
construction. Missing/invalid saved modes retain DefaultCameraMode (shipped: Medium).
Changing returner or restarting a run retains the selected mode and resets framing.

GameplayCamera is a presentation-only component. It consumes world position, elapsed frame
time, facing yaw, sprint activity and a separate immutable CameraLookInput. These are
read-only presentation inputs; the camera cannot write player, AI or physics state.
It updates once after simulation substeps. PlayerInputSnapshot remains gameplay input.

Options also has Third Person Position: 0–180 degrees in steps of 5.
0 retains the right shoulder, 90 moves over the head, and 180 reaches the left shoulder.
The offset traces a vertical semicircle of radius ShoulderOffset; view direction and movement
are unchanged. Changes apply while paused and save as ShoulderAngleDegrees. Fixed presets
ignore this setting. Missing or invalid saved values retain the authored default (0).

Options > Third Person Tilt sets the resting vertical pitch from -10 to 75 degrees in
5-degree steps (default 20). Higher values look farther down. Changes apply immediately
while paused, save as CameraPitchDegrees, and restore on restart. Camera look can still
adjust pitch temporarily. Fixed views are unaffected; shoulder placement remains separate.

## Third Person

The camera follows from 3.2 metres behind a shoulder-height anchor (1.55 metres), with
a 20-degree downward pitch and a 0.65-metre camera-right shoulder offset. The player
sits left of centre, leaving a clear view ahead. Both position and target share the
rotating shoulder offset, keeping look direction aligned with camera-relative movement.
A follow response of 24 keeps the close camera attached during running and lateral cuts.
The camera orbits this smoothed player-position anchor plus TargetHeight and ShoulderOffset. Dedicated input
integrates desired yaw and pitch; exponential smoothing follows the desired angles.
Pitch is bounded; yaw can orbit all the way around. Entering ThirdPerson seeds the
view behind the returner. The camera smoothly swings behind the returner as they turn,
using the shortest yaw path with a response of 6 per second. Look input adds an orbit offset.
Left-stick/WASD movement starts relative to the displayed horizontal camera yaw.
A held direction keeps its movement basis while automatic follow catches up, preventing
steering feedback and circular running. Changing direction or releasing and pressing again
uses the current view; manual look continues to steer the movement basis.
Input is resolved once per frame before simulation substeps. Pitch never adds vertical movement or slows travel.
Fixed camera modes retain their world-relative movement controls.
Sprint smoothly reduces distance from 3.2 to 2.8 metres, returning to 3.2 when released.
Fixed cameras have no sprint zoom.

Default look bindings are J/L (yaw), I/K (pitch), and controller D-pad directions.
These are separate CameraLookLeft/Right/Up/Down actions in input.json, rebindable in Controls.
The existing mouse/right-stick evade gestures retain their bindings and behavior.
Up looks upward; Down looks downward. Fixed modes ignore these camera actions.

ThirdPersonCamera tuning in TackleAlleyConfig:
Distance, SprintDistance, ZoomSmoothing, TargetHeight, ShoulderOffset, InitialYawDegrees, InitialPitchDegrees, MinPitchDegrees,
MaxPitchDegrees, YawSpeedDegrees, PitchSpeedDegrees, FollowSmoothing, LookSmoothing, FovY.

## Fixed follow

CloseCamera, MediumCamera and FarCamera are instances of one FixedCameraPreset type.
All three use the same fixed-follow implementation, with world yaw zero (looking down -Z).
Both camera position and target translate from the same smoothed anchor. Follow lag therefore
cannot rotate the camera toward the player. Even airborne position changes translate the
whole view without changing pitch/yaw.

Preset fields: Distance, Height, PitchDegrees, LookAhead, FollowSmoothing, FovY.
Distance is the world +Z offset behind the anchor before the forward LookAhead translation;
Height is the world +Y offset; positive pitch looks downward. LookAhead translates the view
along world -Z and never turns it. Smoothing values are exponential response rates per second.

| Preset | Distance | Height | Pitch | Look-ahead | Follow response | FOV |
| --- | --- | --- | --- | --- | --- | --- |
| Close | 4.5 | 3.5 | 18 | 1 | 10 | 55 |
| Medium | 6.75 | 5.5 | 20 | 2 | 10 | 55 |
| Far | 10 | 8 | 25 | 3 | 10 | 55 |

Camera tuning is validated for finite values, positive distances/response rates, valid FOV
and nonsingular ordered pitch limits before use. Edit config.json and restart to tune values.
Mode selection itself is live.

## Removed behavior and compatibility

The old ThirdPersonCamera implementation and its flat camera config fields were deleted:
speed-tier distance anchors, automatic steering yaw, rear-view cone/hysteresis, and hybrid
look-at tracking. There is no compatibility camera. The directional-movement refactor
adds a full-circle orbit and separately configured sprint zoom only to
ThirdPerson. Shoulder follow now tracks facing through a full turn; it does not restore the old bounded steering/hybrid behavior. Existing config files that only contain
old camera keys use the new defaults; copy tuning into the new nested blocks when upgrading.
Old settings without CameraMode remain valid. Old full binding snapshots retain their valid
rebinds and receive the new default camera actions through binding-override migration.

## Validation and manual follow-up

Tests cover all fixed presets during movement and facing changes, cuts/jukes/spins/reversals,
third-person yaw/pitch and limits, follow/look smoothing, immediate paused-menu switching,
startup persistence, config validation, and identical player/defender/contact simulation while
fixed modes change. ThirdPerson tests cover rotated controller input, analogue magnitude,
diagonal limits, held-sideways stability, and mode switching without physical state mutation. Native rendering/controls tests run in hidden windows.

Manual playtesting still needs to judge framing at different aspect ratios and returner sizes,
orbit speed and smoothing, D-pad/keyboard look ergonomics, visibility during close tackles and
ragdoll outcomes, and geometry occlusion. This pass adds no camera collision/obstacle system.
