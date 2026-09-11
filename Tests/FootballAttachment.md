# Runtime football carry

BallCarrier borrows the independent Football model from AssetManager. It changes
only a value copy's Transform when drawing; mesh/material ownership stays with
AssetManager. Opponent has no football state. Each character still has independent
FootballPlayer ModelInstance and AnimationPlayer objects.

## Assets

| Tier | Clip | Asset key | Path |
| --- | --- | --- | --- |
| 1 | CarryJog | FootballPlayerCarryJogAnimations | Assets/Models/football_player_carry_jog.glb |
| 2 | CarryRun | FootballPlayerCarryRunAnimations | Assets/Models/football_player_carry_run.glb |
| 3 | CarrySprint | FootballPlayerCarrySprintAnimations | Assets/Models/football_player_carry_sprint.glb |

Football (Model): Assets/Models/football.glb.
Original Jog/Run/Sprint registrations remain for opponents.

## Authored grip derivation

Sources: Tools/Blender/player_hand_rig.py (fit_carry, inside_tuck,
attach_football), hand_grip_fit.json, rig_player.py, and
player_football_hand_notes.md. No Blender source or output is modified.

Both exports use export_yup=False. Football geometry already has its origin at
the shell centre, +Z long axis and +Y laces. No axis conversion or extra scale
is needed.

The pre-tuck assembly in chest-deformation reference coordinates is:

- Upper-arm head U = (-.221, 1.390, .007).
- Upper-arm length = length(-.145, -.239, -.002).
- Lower-arm length = length(-.127, -.229, -.020).
- Wrist W = U + upperLength * normalize(-.079, -.265, .003)
  + lowerLength * normalize(.02, .155, -.216).
- Ball centre B = (-.349, 1.326, -.111), from hand_grip_fit.json.
- Hand orientation: tilt=-25 degrees; yaw=-17 degrees; roll=28 degrees.
  Before yaw/roll, hand X=(0,sin(tilt),-cos(tilt)),
  Y=(0,cos(tilt),sin(tilt)), Z=X cross Y.
  Apply Blender Euler XYZ (0,yaw,roll) to each basis vector.
- Ball Z=normalize(.02,.115,-.237), X=normalize(worldUp cross Z), Y=Z cross X.

For Blender column vectors, local grip = inverse(hand) * ball.
The chest deformation and subsequent inside_tuck transform premultiply both
hand and ball, so both cancel. Thus the same local grip works for all three clips.
Transposing for System.Numerics row vectors gives FootballGripLocal:

```text
-0.35795751   0.34925157  -0.86596175  0
-0.61571349   0.60892960   0.50010163  0
 0.70197102   0.71219947  -0.00293202  0
-0.12157734   0.02833468  -0.00747214  1
```

Translation is in metres; scale is (1,1,1); the upper 3x3 is the rotation.
The native asset test independently checks this against the documented corrected
centre (-.245,1.316,-.194) within 2 mm and long-axis elevation near 37 degrees
for all three actual Carry clips.

## Rendering and update order

System.Numerics row-vector composition:

```text
playerWorld = player.Model.Transform
            * Scale(visualScale)
            * RotationY(VisualYawDegrees)
            * Translation(Position + groundingOffset + jukeHop)

footballWorld = FootballGripLocal * currentAnimatedHand * playerWorld
drawTransform = footballAsset.Transform * footballWorld
```

The canonical model's base transform is included. VisualYawDegrees retains the
existing smoothed directional yaw and temporary Spin rotation. The player draw
still uses DrawModelEx with exactly those outer position/rotation/scale values.
Football draws with its complete matrix and identity DrawModelEx arguments.

Update/RunIntoEndZone first apply the animation pose, then finish movement,
yaw, spin and hop state, then query AnimationPlayer.TryGetBoneTransform("Hand.R").
Reset seeks CarryRun to time zero, clears the existing movement visual state,
then refreshes the football immediately. Draw never advances animation.
Missing Hand.R fails initialization instead of displaying an unattached ball.

## Validation

Native hidden-window tests cover all carry tiers, normalized phase transitions,
compatibility, stationary Root, changing Hand.R transforms, attachment before Draw,
directional yaw/Spin/juke composition, deterministic Reset, end-zone continuation,
independent opponent playback and unchanged game-driven movement. Asset checks
confirm the standalone football has three materials, no skeleton or animations,
and is absent from player GLBs. Existing controls, acceleration, camera and helmet
tests remain included.

Release build: zero warnings/errors. Full test suite: 88 passed, zero skipped.
The validation tool rejects the clean subcommand; dotnet build -t:Clean runs the
equivalent MSBuild Clean target successfully.

Hidden-window drawing is automated smoke coverage, not manual visual approval.
No interactive playthrough or new visual inspection was performed. Manual review
still needs to confirm laces/finger contact, torso clearance, all three speeds,
left/right running, Spin and the unchanged opponent/camera appearance.
