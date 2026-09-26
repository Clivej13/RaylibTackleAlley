# Physical tackle aiming

Decision planning and behavior-profile tuning now live in the shared stages described
in [DefenderAI.md](DefenderAI.md). This solver and the swept-contact path retain their
physical reachability contract. Profile correction limits use the same correction code.

The AI follows pursuit → tackle preparation → contact solution → commitment →
early correction → direction lock → contact or miss → recovery. These aiming phases
sit within the existing Locomotion, TackleReady, SetWrap and LungeTackle animation
states; ragdoll and recovery ownership remain unchanged.

Pursuit still uses CarrierPursuitPrediction. Preparation keeps observing the carrier
and turning the ready stance toward the body. Wrap and lunge opportunities are tested
independently. A wrap requires immediate reach, controlled defender speed, facing,
compatible contact height and relative movement that will not escape the arms.
The wrap contact point is on the waist capsule surface.

## Contact solution

TackleAiming.Target uses scaled animated pelvis/chest capsule positions. Headless
decision tests use the same cached physical height and radii. Standard tackles aim
30% of the way from pelvis to chest. Waist, thigh/hip and torso targets are also
supported. Build affects contact radii through the bounded physical radius scale;
Height scales lengths and reach. Actual collision regions can differ from intended aim.

Solve uses carrier-minus-defender relative position and velocity to calculate closing
speed and the global launch envelope. Each candidate predicts target position using
velocity plus half the bounded observed acceleration times time squared. Acceleration
contribution is further scaled by confidence. Candidate times begin at remaining
animation lead and end at the lesser of supported clip duration and the
confidence-adjusted maximum prediction horizon.

Reach integrates current speed toward the launch-speed ceiling using the individual
defender's Acceleration. Preparation acceleration over the animation lead supplies a
bounded initial speed estimate, never exceeding the actual launch speed. Actual launch
speed remains max(current speed, individual JogSpeed); there is no extra chase-speed
boost. A candidate must fit the facing cone and the travel-plus-combined-radius
envelope, including vertical target separation beyond capsule reach.

Candidates are visited chronologically. The first valid interval is bisected to
refine contact time. Sampling precision is controlled by TacklePredictionIterations;
times are earliest to that sampling/refinement precision, rather than an unbounded
analytic intercept. ContactSolution returns reachable, physical surface point,
time in seconds, launch direction, intended region and confidence. Failed solutions
cannot start a lunge.

TackleMotionObservation measures acceleration from consecutive nonzero-time velocity
samples. Sudden acceleration or steering immediately reduces confidence; explicit
cut/juke/spin state also caps it. Stable motion restores confidence with a 6/s response.
Below TackleMinimumPredictionConfidence no new lunge is permitted. Otherwise confidence
interpolates the available horizon from animation lead to maximum prediction time.
Zero-time intent changes do not corrupt movement history.

## Commitment, correction and lock

The defender stores InitialContactSolution, its time/region/confidence,
InitialLaunchDirection, LaunchSpeed and the actual committed duration.
CurrentContactSolution updates independently, preserving the initial snapshot.

During the first LungeCorrectionWindowFraction of the dive, a reachable revised
solution can rotate travel direction. The maximum rate is LungeCorrectionRateDegrees
times the Agility factor (0.90 at 1, 1 at 50, 1.10 at 100). The linear fade is integrated
over elapsed time, so angular allowance does not depend on frame partition. Total
rotation from the original direction cannot exceed LungeMaximumCorrectionDegrees.
Travel integrates in steps no larger than 1/240 second during the lunge.

At the end of the correction window, direction remains fixed. Unreachable revised
targets also leave direction unchanged. Cuts after lock can evade the dive; existing
miss landing, ragdoll handoff and recovery finish normally.

## Swept contact and outcomes

Before each gameplay simulation step, immutable endpoint/radius/body snapshots are
captured for both players. After movement and animation, new snapshots are captured.
Committed wrap/lunge capsules are swept against the carrier's moving capsules across
the same interval. Head capsules remain excluded, matching the existing contact set.

SweptTackleContact searches each pair chronologically using a conservative bound on
endpoint displacement to reject empty time intervals. This detects passes where both
rendered endpoints are separated, including moving or changing capsule orientations.
The earliest pair wins, with deterministic body-order tie breaking. Endpoints interpolate
linearly within a simulation step; existing nearby gameplay substeps bound nonlinear
animation motion. Spatial tolerance is 0.00001 m, with a 24-level refinement limit.

SweptContact carries normalized frame time (0–1), separate surface points, normal
from defender to carrier, both body indices/regions and relative point velocity at
impact (including animated endpoint motion). No history persists across reset,
recovery or a new frame without valid poses.

LungeTackleOutcome uses this contact instead of re-selecting a nearest end-frame pair.
Facing uses impact geometry, and contact height/region and normal closing velocity
feed TackleImpact alongside each player's momentum, Strength, mass and resistance.
The same point, normal and body indices reach RagdollContact.Resolve for impulse
application and regional active drive. End-frame poses own ragdoll handoff; angular
energy and equal/opposite impulse limits still apply. Glancing contacts remain upright;
direct impacts can reach the full physical tackle outcome. Ordinary discrete contact
and zero-time handoff tests retain the existing overlap path.

## Debugging and validation

Set DrawTackleAimingDebug to true in config.json and restart. It draws:

- Sky blue: observed velocity, predicted carrier path and carrier capsules.
- Red: rejected candidates; yellow: valid candidate and original launch direction.
- Gold: selected physical target region; green: selected contact surface point.
- Lime: corrected direction and current defender capsules.
- Orange: original-direction correction limits, previous capsules and swept paths.
- Magenta: detected impact point and normal.

The text overlay reports player, target region, expected time in seconds, confidence,
lock/state and most recent normalized time of impact. LastSweptContact persists for
inspection until reset or the next hit; it is never reused for collision resolution.
The debug renderer does not affect collision or solver decisions.

Run dotnet test Tests/Controls.Tests.csproj -c Release. TackleAimingTests and
SweptTackleContactTests cover earliest reach, leading motion, unreachable targets,
confidence, scaled reach/regions, correction budgets and lock, moving capsule sweeps,
earliest impact, late evasion, glancing/direct outcomes and frame partitions.
The existing native animation, physical attributes, ragdoll and recovery suites remain
part of validation. Visually review debug paths and tackle transitions in a playable
session after tuning; hidden-window tests do not replace that review.
