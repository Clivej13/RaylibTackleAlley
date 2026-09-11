# Production football and minimal hand rig

This pass supersedes the ellipsoid preview and 20-joint carry assets.
No runtime attachment, physics, C#, asset registry, or configuration work is included.

## Rebuild

Run `Tools/Blender/rebuild_football_hand_assets.py` using Blender MCP's
`run_blender_script` with the repository as `project_root`.
It builds the football first, upgrades the saved approved player clips,
exports the canonical player, regenerates carries from the approved normal
actions, and validates the complete asset set. The upgrade is idempotent:
hand vertices start from Stage 6 before the fitted corrections are applied.
Original locomotion curves are retained rather than reconstructed by this pass.

For earlier-stage authoring, `rig_player.py` and all three normal locomotion
generators now call `player_hand_rig.py`; the carry generator uses the same
hand rig and real football attachment. Existing static inspection actions
remain in the saved blends. `hand_rig_baseline.json.gz` is the immutable
pre-upgrade validation baseline, not an input animation generator.

`fit_football_grip.py` is the historical authoring aid that derived
`hand_grip_fit.json` against the production shell before the inside-arm correction.
It is not the current carry-pose generator. The regular rebuild uses
the saved fit and does not run a new fit search. Keep the fitted JSON with
the source scripts. No external Python package is required beyond Blender.

## Football

- Source: `Assets/Models/football.blend`.
- Runtime: `Assets/Models/football.glb`, one independent `Football` object.
- Origin: centre of the symmetric leather shell, suitable for rotation/physics.
- Local axes: +Z long axis, +Y up/laces, +X lateral. Units are metres.
- Nominal shell: 0.280 m long, 0.164 m diameter.
- Detail-inclusive bounds: X 0.1666 m, Y 0.16922 m, Z 0.280 m.
- Identity object transform, applied geometry, no skeleton or animations.
- 1,310 source vertices, 2,560 triangles, three materials: leather, seams, laces.

The shell uses a faceted prolate profile with rounded tips, four thin seams,
and eight raised cross-laces with two longitudinal lace rails.
Every carry preview appends this exact geometry from `football.blend`.
The standalone GLB vertex positions are checked against the same source.

## Hand controls

```text
Hand.L
  Fingers.L
  Thumb.L
Hand.R
  Fingers.R
  Thumb.R
```

Both new children are deform bones. The skeleton has 24 joints; all 20
existing bone names, parents, rest matrices and endpoints are unchanged.
There are no per-finger bones or persistent IK constraints.

The original hands already contain a palm, four fingers and a thumb within
each hand mesh. Their vertex/polygon counts and topology remain intact.
Only finger/thumb rings are fitted, with a maximum 9.062 mm vertex adjustment,
mirrored to the left hand. Palms, wrists and all digit root rings are unchanged.
No geometry outside `LeftHand`/`RightHand` changes.

Palm vertices follow Hand. Digit rings blend between Hand and their grouped
Fingers/Thumb bone with articulation weights 0, 0.35, 0.80 and 1.0.
Weights are normalized; existing descriptive region groups are retained.
Normal locomotion adds a constant relaxed 12-degree finger curl and an
8-degree thumb curl, mirrored appropriately. Original animation curves remain exact.

## Carry refit

The current tuck follows `Assets/References/carrying.png`: ball inside against
the right ribs/pads, forearm outside/front, elbow down and close, hand securing
the front/top. The fitted forearm/hand/ball assembly turns inward 100 degrees
around the forearm axis, with the upper arm aimed along (-0.14, -0.225, -0.06)
in chest-deformation coordinates. Existing subtle impact variation remains.
Only `UpperArm.R`, `LowerArm.R` and `Hand.R` rotation curves change in this
correction. Finger/thumb curves and all hand geometry/weights remain exact:
`Fingers.R` curls 28 degrees; `Thumb.R` curls 23 degrees with 14-degree opposition.

All lower-body bones, Root, torso, head, clavicles and the left arm retain
their approved motion. Normal Jog/Run/Sprint retain every existing bone curve.
Key ranges are still 1-29, 1-25 and 1-21 at 30 fps, respectively, with the
last key duplicating the first. There is no root motion.

The corrected frame-1 ball centre is approximately (-0.245, 1.316, -0.194)
in chest-deformation reference coordinates. The long axis points forward/up
at about 37 degrees. Through all cycles its centre stays at least 110 mm
inside the forearm midpoint, and the nearest torso/pad surface is within 16 mm.
The original fit values in `hand_grip_fit.json` remain the grip-space calibration;
`inside_tuck` in `player_hand_rig.py` transforms that complete fitted assembly.
`FootballPreview` is bone-parented to `Hand.R` only in Blender previews.
Its hand-relative attachment matrix is recorded on the preview object.
All player GLBs explicitly exclude it. The old ellipsoid object is removed.

## Outputs and checks

Updated blends: `lowpoly_human_rigged.blend`, normal Jog/Run/Sprint blends,
and all three `football_player_carry_*.blend` files.
Updated runtime GLBs: canonical `football_player.glb`, the three normal
`lowpoly_human_*_validation.glb` files, and the three carry GLBs.
Each export has exactly the same 24-joint order and inverse bind matrices.

`Assets/Models/HandRigPreviews/validation.json` records all checks:

- Existing curves and quarter-frame matrices preserved outside the allowed refit.
- Hand topology/root positions retained; other geometry/weights unchanged.
- Both hand hierarchies and normalized exported finger/thumb influences checked.
- Carry channels outside the right arm match the corresponding normal GLB exactly.
- Frame counts, durations, loop closure and zero root motion checked.
- Helmet, facemask, visor and chinstrap remain separate and Head=1.0 skinned.
- Production football independence, identity transforms and exact geometry checked.
- C# source and root JSON hashes match the pre-task baseline, including dirty files.

Contact sampling covers vertices, edge midpoints and face centres at quarter
frames through every carry loop. Worst measured overlap is 0.163 mm on the
hand; no sampled body/ball overlap remains, below the 1/2 mm hand/body tolerances.
The maximum sampled fingertip/thumb contact gap is 4.801 mm; this clearance
keeps the broad low-poly finger faces outside the curved shell. These are
sampled contact checks, not an exhaustive collision/physics guarantee.
No visible clipping, detached grip, wrist collapse or equipment regression
was observed in the reviewed cycle phases. Loop endpoint mesh error is zero.

## Previews

`Assets/Models/HandRigPreviews/` contains `football.png`, `relaxed_R.png`,
`relaxed_L.png`, `grip_R.png`, four PNG phases per carry clip and full-cycle
`CarryJog_loop.mp4`, `CarryRun_loop.mp4`, `CarrySprint_loop.mp4`.
The regular `CarryPreviews/` phase images and videos also use the real ball.
`preview_inside_carry.py` generates `CarryJog_inside_inspection.png`,
`CarryRun_inside_inspection.png`, and `CarrySprint_inside_inspection.png` in
`CarryPreviews/`: four phases left-to-right, three-quarter above and right side
below. It also refreshes the hand-rig preview copies and gripping-hand close-up.

## Inside-carry-only rebuild

Using Blender MCP, run `create_player_carry_animations.py`,
`validate_inside_carry.py`, `validate_player_hands.py`, and
`preview_inside_carry.py`. Normal clips, canonical player, standalone football
and their source files are not rewritten by this pass.
`inside_carry_baseline.json.gz` preserves the pre-correction 24-bone assets;
never overwrite it. `CarryPreviews/inside_carry_validation.json` proves exact
mesh/weight/rest-pose preservation, unchanged non-right-arm curves, unchanged
finger/thumb curves, byte-identical normal assets and unchanged C#/root JSON.
Older preservation reports/baselines document prior passes; the hand-rig
validation report is authoritative for the current 24-joint assets.
