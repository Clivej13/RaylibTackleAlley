# Relative 90-degree CarryRun cuts

CutLeft turns +90 degrees around local Y; CutRight turns -90 degrees.
Directions are defined from behind the player, initially facing local -Z.
Both begin at zero local heading. Runtime supplies the incoming world yaw.

## Turn and mechanics

Root carries only progressive local yaw, with no translation or scale change.
The head leads, chest/shoulders follow, and the pelvis inherits the base turn.
Hips retain compression/drop and lateral lean. Baked leg IK preserves the
outside plant anchor through frames 7-13 while the body rotates, then releases
into the outgoing stride. All controls are discarded before saving/exporting.
The right carry arm/hand local poses remain exact CarryRun samples.
Skeleton, weights, geometry, helmet and hand rig are unchanged.

Both clips are frames 1-23 at 60 fps: 22/60 = 0.366667 seconds.
Base yaw magnitude at preview frames 1,7,9,13,17,23: 0,15,25,50,76,90 degrees.
Final pelvis yaw is approximately +/-87.879, chest +/-92.174 and head +/-89.948
because the original CarryRun gait includes small torso counter-rotations.

## Handoff contract

CutLeft exits at CarryRun source frame 10; CutRight at source frame 22.
The final deformed pose is exactly the respective CarryRun pose rotated 90 degrees.
At completion, runtime replaces incoming world yaw plus cut local yaw with outgoing
world yaw plus CarryRun. Do not retain the cut's 90-degree local yaw on top of the
outgoing world yaw. No runtime or C# code is changed by this asset revision.

## Outputs and reproduction

Assets/Models/football_player_cut_left.blend and .glb contain CutLeft.
Assets/Models/football_player_cut_right.blend and .glb contain CutRight.
Run through Blender MCP in order:
1. Tools/Blender/create_player_cut_animations.py
2. Tools/Blender/validate_player_cuts.py
3. Tools/Blender/preview_player_cuts.py

Assets/Models/CutPreviews contains both phase sheets, individual phase images,
normal-speed preview videos and 3x slow videos. Phase sheets place the straight
rear view above the front view; videos use the straight rear view.
Preview floors/grids are excluded from the player files.

validation.json records sampled yaw, progressive turn, leading head/chest,
CarryRun handoff matrix error, plant slip, foot clearance, carry attachment,
skeleton/mesh preservation and GLB compatibility. source_preservation.json
records untouched non-cut assets and C#/root JSON hashes.
