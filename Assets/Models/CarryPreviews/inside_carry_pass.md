# Inside-arm carry correction

Reference: `Assets/References/carrying.png`.

## Changed in this pass

- `Tools/Blender/player_hand_rig.py`: inward forearm/hand/ball transform and upper-arm tuck; repeatable refit.
- `Tools/Blender/create_player_carry_animations.py`: reference/pose metadata; regenerated all three carries using the shared refit.
- `Tools/Blender/validate_player_hands.py`: allow the intended right upper-arm rotation change in the historical pre-hand-rig comparison.
- `Tools/Blender/player_football_hand_notes.md`: current pose, checks and rebuild instructions.
- Added `Tools/Blender/validate_inside_carry.py` and immutable `inside_carry_baseline.json.gz`.
- Added `Tools/Blender/preview_inside_carry.py`.
- Regenerated `Assets/Models/football_player_carry_jog.blend` and `.glb`.
- Regenerated `Assets/Models/football_player_carry_run.blend` and `.glb`.
- Regenerated `Assets/Models/football_player_carry_sprint.blend` and `.glb`.
- Refreshed the 12 carry phase PNGs and three loop MP4s in both `CarryPreviews/` and `HandRigPreviews/`.
- Refreshed `HandRigPreviews/grip_R.png` and `grip_fit.png`.
- Added three `Carry*_inside_inspection.png` sheets in `CarryPreviews/`.
- Updated `CarryPreviews/validation.json`, `HandRigPreviews/validation.json`, and `helmet_skinning_validation.json`.
- Added `CarryPreviews/inside_carry_validation.json`, this report and `inside_carry_git_status.txt`.

## Result

Only rotation curves for UpperArm.R, LowerArm.R and Hand.R changed.
Finger/thumb curves, all player geometry and weights, skeleton/rest pose,
lower-body motion, torso/left-arm motion, timing and frame counts are exact.
Normal Jog/Run/Sprint files, canonical player and standalone football are
byte-identical to the start of this correction. No root motion was introduced.
All seven player GLBs have identical 24-joint order and inverse bind matrices.
Helmet pieces remain separate, Head=1.0, with no equipment regression.

The ball remains an independent production mesh, attached to Hand.R only
for Blender previews and excluded from player exports. The corrected tuck
keeps it at least 110 mm inward of the forearm midpoint, about 37 degrees
upward, and within 16 mm of the nearest torso/pad surface throughout each loop.

Quarter-frame checks cover 113 Jog, 97 Run and 81 Sprint samples.
No sampled ball/body penetration; maximum sampled hand penetration 0.163 mm,
maximum distal contact clearance 4.801 mm. These are sampled geometric checks,
not an exhaustive collision guarantee. The hand attachment stays constant,
and loop endpoint mesh error is zero. Four phases from two viewpoints per
clip and the close-up grip were visually reviewed without an obvious issue.

All C# and root JSON hashes match the start of the correction. The pre-existing
dirty worktree is preserved. Nothing was committed or pushed.
