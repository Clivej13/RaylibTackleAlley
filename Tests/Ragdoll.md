# Ragdoll tackles and recovery (Parts 1–4)

## Manual harness

Run `dotnet run -c Release -- --ragdoll-debug`.

- R: nearest eligible defender, current pose and velocity.
- Shift+R: also add 180 N s defender-forward and 120 N s upward at chest centre.
- One activation per key press. Keys are disabled without the switch.
- Add `--ragdoll-bodies` for capsule wireframes/joint markers over the real mesh.
- Recovering defenders are excluded from manual selection. Reset clears all ownership.

## Physics and skeleton

11 capsules: pelvis/Hips, Chest, Head, UpperArm.L/R, LowerArm.L/R,
UpperLeg.L/R, LowerLeg.L/R. 10 bounded joints. Root follows pelvis with its own
offset; Spine follows Hips; Neck/clavicles follow Chest; hands follow forearms;
fingers/thumbs follow hands; feet follow shins.

87 kg total: pelvis 18, chest 25, head 6, each arm 3+2, each leg 9+5.
Fixed 120 Hz, maximum .25-second catch-up, 24 constraint iterations followed by
24 attachment/ground iterations. Gravity 9.81 m/s², linear/angular damping
.35/2.5 per second, ground damping 8 per second, angular cap 12 rad/s.
Settling requires all bodies below .12 m/s and .25 rad/s with ground contact
for .8 seconds. No body/body collision world or fumble simulation is introduced.

RagdollPose captures every current absolute animated model-space bone and bind pose.
Given captured model-world W, body B0 and animated bone A0:
O = A0 * W * inverse(B0); current A = O * B(t) * inverse(W).
Helpers preserve their captured parent-local transform. Native skinning receives
absolute model-space TRS via temporary identical frames, without editing borrowed
clips. The draw-local model receives transpose(W) once; native model matrices
are transposed when read into System.Numerics row-vector calculations.

## Missed and successful lunges

The shoulder-led LungeTackle animation controls launch until its final authored
frame, then a miss snapshots that exact pose and inherits locked direction *
CurrentSpeed plus current VerticalVelocity. Remaining frame time advances physics.
No additional missed-lunge impulse is applied.

The existing IsTouching distance result confirms success; no new detection geometry
or swept collision is added. For a LungeTackle hit, both current character poses and
movement velocities are captured immediately. Carrier velocity is measured from its
actual clamped movement, including lateral/evasive motion.

LungeTackleOutcome adds J = normalised committed horizontal velocity *
min(72, 8 * horizontal speed), in N s. Carrier receives +J and defender receives -J;
60% goes to chest and 40% to pelvis, with no upward boost. Existing velocities remain
the base motion. No repeat confirmation or repeated impulse while a tackle is pending.
A lunge completing on the contact update remains eligible for the same outcome.
SetWrap and other existing non-lunge outcomes retain immediate game over.

## Game Over timing

Contact sets TacklePendingGroundImpact, not GameOver. While pending, carrier inputs,
new tackles and normal AI stop; owned physics/committed lunges continue. No touchdown
or out-of-bounds outcome supersedes the confirmed tackle's fall.

Ragdoll checks meaningful ground contact every fixed substep:
1. Pelvis OR chest capsule bottom is within .005 m of the ground.
2. Chest centre is at most 3.5 chest radii above ground.
3. The contacting torso body's pre-solver vertical speed is at most -.5 m/s,
   OR the above down/contact condition persists for .08 seconds.

Hands, feet and head grazes cannot meet this rule alone. Contact is latched so a
brief impact cannot disappear between render frames. BallCarrier retains the latch
through recovery. On the first pending update observing it, GameOver is set once,
the pending flag clears, and EndStateElapsed starts at zero. There is no settling
or recovery delay. Existing UI and automatic/manual restart behaviour are unchanged.
Reset clears pending state, impact latches, both ragdolls and recovery timers.

The football uses the ragdoll/recovery Hand.R world transform with its existing grip.
No fumble or independent football physics.

## Recovery

Both characters use RagdollActive -> RagdollSettling -> Down -> GetUp -> Locomotion.
When settled, RagdollRecovery captures the final world pose, aligns the animated
Hips X/Z to the settled Hips, places the root on the ground, and chooses yaw by
matching the settled pelvis-to-chest ground direction to Down's authored direction
(previous facing is the fallback for degenerate projections).

The first Down pose is exactly the settled world pose. Parent-local translation,
quaternion and scale blend with smoothstep into the looping Down clip over
RagdollDownBlendDuration (default .6 s). This avoids teleporting a limb/root on
ownership handoff. The model retains its visual scale and ground-offset convention.
The root is stationary; no input/AI can steer during Down or GetUp.

RagdollDownDuration (default 2 s, inclusive of the blend) is configured in config.json
and must be at least the positive blend duration. GetUp runs once to its final frame;
then locomotion owns the character again. During the existing Game Over screen,
normal movement stays paused. Recovery continues underneath until the usual restart.
Reset explicitly reapplies the selected animation even if its playback frame is cached.

A headless lunge has no current skeleton to capture and waits at its final commitment
until visuals exist; no default ragdoll pose is fabricated.

## Validation

`dotnet build -c Release`
`dotnet test Tests/Controls.Tests.csproj -c Release`

RagdollTests covers the solver. RagdollRigTests/RagdollSkeletonTests isolate the
physics/skeleton bridge, including activation render preservation and helmet motion.
DefenderRecoveryTests covers lunge handoff and missed-lunge physics.
TackleOutcomeRecoveryTests uses real animated assets and the game update path for:
two-character snapshots, conserved contact impulse, no immediate lunge game over,
impact-only one-shot outcome, boundary contact, input blocking, football attachment,
miss/SetWrap behaviour, reset, exact settled-to-Down world pose continuity, grounded
Down geometry, stationary recovery, configurable Down duration and GetUp completion.
