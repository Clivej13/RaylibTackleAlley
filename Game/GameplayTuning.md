Defender decision parameters now live in defender-profiles.json; levels reference
those shared behavior presets with BehaviorProfile. See [DefenderAI.md](DefenderAI.md)
for stage ownership, schema, diagnostics and compatibility with global tuning.

Authored level geometry, spawns and defender profile references live in levels.json;
see [Levels.md](Levels.md). BallCarrierProfile remains in config.json, while named
defender PlayerProfile values are preserved in the level catalog. See [PlayerProfiles.md](PlayerProfiles.md) for units, ranges,
build calculations, movement ratings, jersey numbers and the ShowPlayerProfiles overlay.
Speed/Acceleration/Agility default to 50. Per-player movement baselines are cached
at construction; restart the game after editing ratings or baseline movement settings.

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

Pursuit prediction closes space; tackle aiming uses the physical carrier body.
The flow is pursuit → preparation → contact solution → commitment → early correction
→ direction lock → contact or miss → recovery. Ready facing follows the current target.
Wraps independently test immediate scaled arm/torso reach, facing, contact height and
relative movement. Lunges search for the earliest reachable pelvis/lower-chest contact
during the actual supported animation window. See [TackleAiming.md](TackleAiming.md)
for the solver, swept impact contract and debug colours.

Recent acceleration, steering, cuts, jukes and spins lower prediction confidence;
stable observations restore it exponentially. Confidence shortens the allowed horizon
between the animation lead and maximum prediction time. Below the minimum confidence,
the defender continues containment/pursuit. OpponentLungeVelocityChangeTolerance remains
a legacy configuration field; the acceleration-based confidence replaces its binary veto.
Ready distances, scaled lunge reach, maximum reach, facing angle, animation lead and
LungeMaximumPredictionSeconds continue to bound commitment. LungeInterception.Target
is retained for legacy standalone callers; gameplay uses TackleAiming.Solve.

| Setting | Default | Valid bounds |
| --- | --- | --- |
| LungeCorrectionWindowFraction | 0.4 | 0–0.8 |
| LungeCorrectionRateDegrees | 90 degrees/s | 0–360 |
| LungeMaximumCorrectionDegrees | 18 degrees | 0–45 |
| TackleMinimumPredictionConfidence | 0.2 | 0–1 |
| TackleAccelerationPredictionLimit | 12 m/s² | 0.1–100 |
| TacklePredictionIterations | 64 | Integer 8–512 |
| DrawTackleAimingDebug | false | Boolean |

All numeric settings must be finite. Correction fades linearly to zero during the first
40% of the dive, with Agility modifying its rate only within 0.90–1.10. Setting the
window, rate or total angle to zero disables correction. The remaining direction is
locked; late evasions can miss. OpponentLungeLaunchVerticalSpeed remains 1 m/s, giving
a 5 cm hop with default gravity. Forward momentum continues until the 0.6-second
animation hands off to ragdoll physics. Continuous capsule sweeps test both players'
motion between simulation poses and supply the earliest impact to physical resolution.

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
the cone. Contact severity now decides whether the carrier stays upright or both
players enter active ragdoll; capsule overlap alone does not guarantee a takedown.

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
- levels.json owns PlayerSpawn and Defenders, using X/Z coordinates on the ground.
  Authored levels require at least one defender. The legacy C# OpponentSpawns
  property still permits empty standalone practice/test setups.
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
  each body's dimensions and masses come from its cached PlayerPhysicalAttributes.
  Ragdoll*Mass fields define relative distribution; the profile Weight is total mass.
- ContactBroadPhaseDistance and ContactSubstepDistance bound collision search and
  short motion steps. Both automatically include the maximum supported height ratio;
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

Physical tuning formulas, outcome thresholds and example profiles are documented in
[PlayerProfiles.md](PlayerProfiles.md#phase-three-physical-attributes). Strength is an
integer 1–100, with 50 neutral. Force/resistance/motor multipliers stay in 0.8–1.2;
balance/support stays in 0.9–1.1. Radius scaling is
sqrt(Height / 2) × sqrt(WidthRatio × DepthRatio), bounded to 0.90–1.05. Skeleton lengths use the full height ratio from the captured visual pose.
Profile Weight distributes over the existing 87-part mass proportions.

ContactMaximumImpulse and ContactMaximumTackleImpulse remain default-player caps:
each pair uses the smaller of clamp(Weight / 110, 0.65, 1.45). Restitution, friction,
split penetration correction, angular energy and hit-region distribution remain
the existing settings. Tackle impulses use relative closing velocity through the
contact normal, preserving unrelated motion. Strength changes transfer within
0–1.2; the energy budget is still limited to collision energy dissipated.

Weak contacts slow/deflect the animated carrier; stronger contacts select controlled
wrap, resisted ragdoll, drive or decisive knockdown. Maintained wraps dissipate
relative motion, last 0.2–1.2 seconds and break beyond reach or on ground impact.
Low dive contact emphasizes leg rotation, torso contact direct momentum, and high
contact upper-body rotation without changing head/neck limits. Active forces scale
with mass through inverse inertia and mass-independent support acceleration.
Get-up playback uses clamp(balance / (Weight / 110)^0.15, 0.85, 1.15), multiplying
the configured carrier/defender playback baseline.

The shipped light carrier has Strength 55; the power-carrier alternative in
BallCarrierProfileExamples has Strength 90. Defenders have 35/65/95/85 respectively.
Copy the power alternative into BallCarrierProfile to select it. Physical values
are cached at construction and persist after reset; configuration edits require
restart. The debug panel reports both participants' mass, momentum, contact scores,
outcome and applied impulse for tuning.
