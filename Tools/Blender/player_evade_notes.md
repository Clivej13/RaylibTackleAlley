# Ball-carrier Juke and Spin

## Juke rework (2026-09-14)

The juke measurements below describe the original version; this section supersedes
them. Run `rework_player_jukes.py` through Blender MCP to rebuild and validate only
the jukes. Run it with `--preview` for phase sheets, then run
`preview_reworked_jukes.py` for rear/side playback with slow motion and scrubbing.
Current review: `Assets/Models/JukePreviews/playback.html`.

Both jukes now last 0.35 seconds (43 frames at 120 fps). JukeLeft brakes on
Foot.R, pushes sideways onto Foot.L, compresses again, then extends and pitches
forward into the running handoff. JukeRight reverses those contacts. The first
compression adds 43 cm of hip lowering, the second 40 cm, with a 36.5 cm lateral
weight transfer. Plant anchors are 108 cm apart laterally (previously 67 cm).
The free foot opens to a low, wide position instead of tucking behind the body.
First-plant knee flexion reaches 103.7 degrees; the receiving plant reaches
110.6 degrees. Maximum planted-foot drift is 1.127 mm. Quarter-frame floor
penetration stays within the 3 mm validation tolerance (2.4 mm worst case).
Grip, original geometry, skeleton,
source actions and run endpoints pass the existing asset validator.

The saved `.blend` and `.glb` files retain their existing names. These are in-place
asset poses; gameplay travel/speed and its existing procedural juke hop are not
changed by this asset rework. The rig's existing L/R bone convention is retained.
Spin assets and C# files were verified byte-identical during generation.

## Original evade authoring record

Run through Blender MCP in this order: `create_player_evades.py`,
`validate_player_evades.py`, `preview_player_evades.py` (all in this directory).
Source: `Assets/Models/football_player_carry_run.blend`.

JukeLeft/JukeRight are 0.50 seconds, frames 1–61 at 120 fps.
SpinLeft/SpinRight are 0.80 seconds, frames 1–97 at 120 fps.
Each has a separate `Assets/Models/football_player_{juke|spin}_{left|right}`
`.blend` and `.glb`. The blends retain the source actions; GLBs contain only
the named new clip and use the existing 24-joint skin and export axis convention.

Directions follow the existing rig's anatomical bone labels, also used for
the right-hand football: L is +X and R is -X, with forward -Z and up +Y.
This is different from the rear-screen naming convention documented for Cut.
Left evades plant Foot.R; right evades plant Foot.L. The right carry arm is
never mirrored. No integration, action registration or C# changes are included.

Jukes load the opposite leg with approximately 58–69 degrees knee flexion,
briefly shift the hips 5.5 cm toward the plant, then burst 21 cm toward the
evade side: 26.5 cm total lateral excursion. The opposite foot remains anchored
through the load/push, then the evade-side foot catches the body. Pelvis fake
and redirect yaw stays within 12 degrees of the authored heading, with small
inherited gait twist. The shoulders sell the fake and the free arm balances.

Spins load the opposite leg with approximately 66–69 degrees knee flexion,
then rotate the hips through a full 360 degrees with torso/head following.
Left is -360 around Y, right +360. Three staged contacts alternate the feet;
the swing leg steps around and contacts are released between rotational drives.
Compression reaches 12 cm, with an 8 cm total lateral weight shift.

Root and armature object transforms remain identity at every sample. Local
hip excursion is transient and returns to zero. Forward travel is supplied
by the game; these are in-place animations with continued running leg action.
All four end in the incoming forward heading. JukeLeft enters CarryRun phase 7
and exits phase 1; JukeRight enters 19 and exits 13. SpinLeft enters 7 and exits
13; SpinRight enters 19 and exits 1. Endpoint matrices match those source poses
to below 0.000001. Blend the outgoing run at the corresponding phase.

The original Hand.R football attachment and all right-arm local poses remain
unchanged. FootballPreview stays in the blends for inspection and is excluded
from GLB, matching the existing separate football attachment workflow.

Validation: saved blends and GLBs checked, including quarter-frame samples
(480 Hz), exact meshes/weights/rest skeleton/source actions, source asset and
C# hashes, helmet skinning, fixed root, foot contact slip/clearance, grip contact,
ball/body penetration, tucked-ball relationship and CarryRun endpoints.
Worst planted-foot vertex drift: 0.602 mm juke, 0.778 mm spin. Maximum measured
hand/ball penetration: 0.163 mm; sampled body/ball penetration: zero. Ball hand
attachment matrix error stays below 0.000001. Minimum sole height: -0.066 mm.
Front/rear/side/three-quarter phase review shows distinct lateral juke and
stepping spin poses, protected ball carry, and no major visible arm/body/ball
clipping. These checks validate the assets, not runtime animation blending.

`Assets/Models/EvadePreviews/index.html` labels all 96 phase images by view,
phase and time. Four combined sheets are named `{Clip}_phases.png`.
`phase_index.json`, `validation.json` and `source_preservation.json` record
preview timing, quantitative results and hashes respectively.
