Gameplay tuning is loaded from the existing root config.json into TackleAlleyConfig.
The base file retains existing settings; TackleAlleyConfig.Tuning.cs adds the migrated
settings and validation without changing the JSON's flat structure. Missing properties
use code defaults. The shipped file retains its existing player speed overrides
(6.5 / 9 / 11.5); the class defaults remain 4 / 6.5 / 9.

Rear view requires the stick within CameraLookBackHalfAngleDegrees of straight back
(default 10 degrees, giving the 170–190 degree sector), as well as the existing pull
threshold. The native left-stick angle is checked before movement deadzones discard
small sideways input.

Quick left/right reversals use PlayerReversalInputThreshold and PlayerReversalWindow.
Each costs PlayerReversalSpeedLoss of the selected pace (default 35%), down to zero.
PlayerReversalAccelerationDelay blocks acceleration for 0.45 seconds on the first
reversal; each chained reversal adds PlayerReversalAdditionalDelay (0.2 seconds),
capped by PlayerReversalMaximumDelay (1 second). Each hit restarts the delay. Normal
sideways steering shares the lost forward momentum. Once the delay expires, the
existing acceleration ramp restores speed; fully restored speed ends the chain.
Small stick noise, held directions, and slow reversals through neutral do not add
penalties. Set PlayerReversalSpeedLoss to zero to disable the penalty.

Juke and spin travel scales with CurrentForwardSpeed / PlayerForwardSpeed captured at
move start, before applying the move's own momentum penalty. Half normal pace travels
half as far; zero speed gives no sideways displacement. Animation duration stays the
same, and acceleration during the move cannot increase its captured travel distance.
PlayerEvadeMaximumDistanceScale caps the multiplier (default 1 preserves the existing
normal-running distance). PlayerJukeSpeed/PlayerSpinSpeed and their durations continue
to define the base distance. Both speeds now default to 4 m/s: a full-scale juke
travels 1.4 m and a spin 1.8 m. Their existing forward pause and animation timing remain.

Pursuit lead scales down with actual speed and drops after abrupt direction changes.
Each defender caps that lead near the carrier. Dive commitment checks closest approach
along the planned launch direction within the available dive time, using
OpponentLungeContactDistance as the reach tolerance. Recent velocity changes shorten
that window until observed motion stabilizes. Failed intercepts continue pursuit;
committed dives retain their launch direction, allowing late evasions to succeed.
OpponentLungeLaunchVerticalSpeed defaults to 1 m/s: with the default gravity this
adds only a 5 cm hop, keeping the forward-leaning tackle near the carrier's middle.
Forward momentum continues after ground level is reached until the 0.6-second
animation hands off to ragdoll physics.
A new dive also requires current carrier velocity to be within
OpponentLungeVelocityChangeTolerance (2 m/s) of the defender's filtered observation.
Sudden close-range changes cause grounded containment until motion stabilizes.
The predicted launch direction must fit the tackle facing cone as well as the
carrier's current position.

Defenders become engaged on entering OpponentSprintDistance or preparing/committing
a tackle. Once engaged, pursuit always targets OpponentSprintSpeed regardless of
separation, including after recovery. Tackle-ready movement still uses its controlled
pace. Engagement clears only when the run resets.

Defenders begin recovery after OpponentRecoveryGroundDelay (0.15 seconds) of grounded
torso contact, without waiting for full ragdoll settling. OpponentRecoveryBlendDuration
(0.2 seconds) blends the landed pose into Down with no extra idle hold.
OpponentGetUpPlaybackSpeed (2) plays get-up twice as fast. Carrier recovery retains
RagdollDownDuration and normal playback speed.

Gameplay AI only prepares a tackle while the defender is within
OpponentReadyHalfAngleDegrees (30 degrees either side) of the direction from the carrier to its predicted
pursuit point. Outside that cone, defenders resume direct sprint pursuit; distance hysteresis
and sudden velocity changes cannot hold them in the ready pose. Committed tackles
still finish normally. Visual facing and spin animation rotation do not rotate this cone. A prediction
point coinciding with the carrier has no direction, so defenders do not enter ready.
The cone only gates the ready stance: reachable wraps and dives still use their
existing distance, facing and interception checks, including during pursuit outside
the cone. Successful dive contact still activates both ragdolls.

CameraHeight already controls the view height above the carrier. The shipped camera
now uses 4.2 metres and CameraSpeed1Distance/CameraSpeed2Distance/CameraSpeed3Distance
of 6.48/5.4/4.32 metres (20% closer). CameraTargetHeight controls the look-at height.

Edit config.json and restart the game to apply tuning. Live reload is not introduced.
Distances are metres, time seconds, mass kilograms, and angles degrees unless a name
explicitly says Radians. Input thresholds and strength/retention shares are in [0, 1].

- Player settings cover pace, steering, facing response, cut/juke/spin gestures and
  durations, momentum retention, visual height and outer-field clearance.
- Opponent settings cover pace, ready/wrap/dive decisions, facing, launch/fall motion,
  and recovery timing. Pursuit and Lunge settings control observed-motion prediction.
- PlayerSpawn and OpponentSpawns use X/Z coordinates on the existing ground plane.
  OpponentSpawns controls both positions and defender count; an empty array is valid.
- Camera settings cover follow distance, height, field of view, target offset, steering
  and rear-view thresholds/smoothing. AutoRestartDelay and EndZoneStopFraction control
  the end-of-run flow.
- Ragdoll settings cover gravity, damping, ground friction, down/settling thresholds,
  active-drive motors, yielding and support, body masses/radii, and anatomical joint
  limits. Joint limits use three-element MinimumDegrees/MaximumDegrees arrays (X/Y/Z).
- Contact settings cover capsule radii, friction, restitution, impulse limits and
  angular/torso distribution. Ragdoll chest/pelvis radii describe ground collision and
  inertia; ContactTorsoRadius/ContactPelvisRadius describe character contact.
  ContactHitLimbAngularShare and ContactHitLimbPelvisShare leave the remaining angular
  share for the chest. Both characters share the game's contact-response settings;
  each body's dimensions and masses come from its owning configuration.
- ContactBroadPhaseDistance and ContactSubstepDistance bound collision search and
  short motion steps. Increase these alongside substantially enlarged characters;
  the substep distance must cover the broad-phase distance.

Game construction validates the full configuration; standalone consumers also validate
their relevant settings. Invalid finite ranges, zero divisors, reversed thresholds,
malformed joint limits and invalid spawn coordinates fail with configuration errors.

Implementation constants deliberately remain in code: fixed physics timestep, solver
iteration/catch-up limits, numerical tolerances and projection correction caps,
mathematical factors, skeleton topology/bone names, authored animation lengths and
exit phases, calibrated football attachment transforms, renderer/UI drawing details,
and the opt-in debug harness's test impulses. These describe solver/asset/test mechanics,
rather than gameplay balance. Authored cut length remains the default of the configurable
PlayerCutDuration because that timer owns gameplay movement.
