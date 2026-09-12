# CarryRun change-of-direction assets

## Outputs

- `Assets/Models/football_player_cut_right.blend` / `.glb`: `CutRight`, LEFT plant.
- `Assets/Models/football_player_cut_left.blend` / `.glb`: `CutLeft`, RIGHT plant.
- Both are one-shot clips: keys 1-23 at 60 fps, duration 22/60 = 0.366667 s.
- No duplicate loop key, root motion, horizontal Hips translation or world travel.

CutRight loads from a leftward lean and redirects right. CutLeft loads from a
rightward lean and redirects left. These are intended for SpeedTier 2 / CarryRun
after Sprint is released, not Sprint-held steering. No trigger, blending or
gameplay implementation is included.

## Authoring

The generator samples the approved `football_player_carry_run.blend` action.
Temporary Blender two-bone IK positions the legs; only rotation keys are baked
onto the existing bones. IK targets, poles and constraints do not survive into
the saved assets. A 60 Hz bake reduces between-key plant drift without changing
the 0.367-second duration. Main plant lock: frames 7-13 (0.100-0.200 seconds).

The cuts have independent plant positions, toe angles, lean/yaw/load profiles,
and free-leg drive amplitudes. Neither action is produced by mirroring the other.
New cut motion affects Hips, Spine, Chest, Head, UpperArm.L and the leg/foot bones.
All right clavicle/arm/hand and finger/thumb local pose values are sampled exactly
from CarryRun, including the approved inside-arm football grip.

The production FootballPreview object retains its existing Hand.R attachment.
It is excluded from both player GLBs. Player geometry, weights, skeleton/rest pose,
and equipment are unchanged, including separate Head=1.0 helmet pieces. Both
exports have the canonical 24-joint order and exact inverse bind matrices.

## Transition phases

Source phases below refer to CarryRun's original 30 fps timeline:

| Clip | Entry | Exit | Plant |
| --- | --- | --- | --- |
| CutRight | CarryRun frame 19 | CarryRun frame 10 | Foot.L |
| CutLeft | CarryRun frame 7 | CarryRun frame 22 | Foot.R |

Entry matches CarryRun exactly. Frames 1-13 retain the approved cut poses exactly.
Frames 14-23 refine only the post-push exit: Drive (17) opens the pelvis and turns
the chest/head toward the new direction; Recover (23) extends the leading leg
and draws the trailing plant leg underneath into a turned CarryRun stride.
The exit adds 25 degrees of pelvis turn at Drive and 38 at Recover, with another
6 degrees at Chest and 8 at Head, mirrored by cut direction. Leading-foot reach
adds 10-12.5 cm laterally and 4.5 cm forward relative to the turned original pose.
The final keys advance at the original CarryRun cadence. They retain the outgoing
heading instead of returning to the original forward pose. Phase matching and blend duration should be reviewed when
runtime integration is implemented. Source actions remain in each new blend as
reference curves; they were authored at 30 fps, while the active cut uses 60 fps.
The original source assets were not saved or changed.

## Reproduce through Blender MCP

Run these scripts with this repository as project_root:

1. `Tools/Blender/create_player_cut_animations.py`
2. `Tools/Blender/validate_player_cuts.py`
3. `Tools/Blender/preview_player_cuts.py`

Existing export/hand/helmet helpers are reused. The only shared-helper change is
an optional `require_loop=False` in grip validation, for these non-looping clips;
existing locomotion callers still require loop closure by default.

## Validation

`Assets/Models/CutPreviews/validation.json` records the saved-asset checks.
`source_preservation.json` records hashes of the pre-existing blend/GLB assets,
C# sources and root JSON configuration. All remain unchanged.

- 89 quarter-frame samples per cut; no root or horizontal Hips motion.
- No persistent IK controls or scale/stretch keys.
- Exact geometry, weights, rest bones and retained source-action curves.
- Exact right-arm/finger poses at every authored key.
- Canonical skeleton compatibility and 23 exported samples per joint channel.
- Production football excluded; separate helmet pieces weighted only to Head.
- Plant vertex drift: CutRight 1.125 mm maximum, CutLeft 1.072 mm maximum.
- Worst interpolated sole dip: 0.350 mm; planted sole is within 0.501 mm of ground.
- Plant knee flexion reaches approximately 66 degrees in both clips.
- No sampled ball/body penetration. Existing sampled hand penetration is at most
  0.163 mm, with distal contact clearance at most 4.801 mm.
- Ball remains at least 110 mm inward of the forearm midpoint; nearest torso/pad
  surface stays within 15 mm, and the hand attachment is constant throughout.

These are sampled geometric checks, not an exhaustive collision guarantee.
No obvious skating, carry separation or equipment regression was observed in
the inspected phases. The remaining review is subjective in-game transition
feel once phase-aware blending and direction changes are implemented.

## Previews

`Assets/Models/CutPreviews/` contains:

- `CutLeft_phases.png` and `CutRight_phases.png`: approach, plant, maximum load,
  push, drive, recovery; front above, rear three-quarter below.
- Individual phase PNGs, push three-quarter views and gripping-arm close-ups.
- `CutLeft_preview.mp4` / `CutRight_preview.mp4`: normal-speed one-shot previews.
- `CutLeft_slow.mp4` / `CutRight_slow.mp4`: 3x slow-motion inspection.

The floor/grid exists only in the preview-render process, not in saved player
blends or GLBs. Videos include one display interval for the final pose, so their
container duration is slightly longer than the animation's keyed 0.366667 s.
