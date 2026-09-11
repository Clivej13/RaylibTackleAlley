# Skinned helmet validation

The regenerated football_player.glb is loaded through the existing FootballPlayer
asset mapping. Helmet, Facemask, Visor and ChinStrap are skinned entirely to Head.
No separate attachment, per-frame offsets or duplicate equipment draw are needed.

Opponent still uses one ModelInstance and independent AnimationPlayers per opponent,
followed by DrawModelEx of the complete model. RaylibGameFramework.ThreeD stays at
0.1.1; TryGetBoneTransform remains available and covered for future detachment work.

SkinnedHelmetTests checks the named equipment nodes' JOINTS_0/WEIGHTS_0 data,
then loads the native model and runs Jog, Run, Sprint and a second Run instance.
Across twelve updates per instance, rigid Head-weighted vertices remain constant
in animated Head-local space within 0.0002 model units. Updating one instance leaves
the next instance's playback time and pose unchanged. The test also exercises
DrawModelEx in a hidden window. AnimatedHeadTests retains the bone-query coverage.

These automated checks pass with the regenerated asset. They verify rigid skinning
and runtime deformation, but are not an interactive visual inspection.

Manual checks remaining:
- Watch Jog, Run and Sprint at gameplay distance: all four equipment pieces should
  remain aligned with no lag, clipping or duplicate draw.
- Watch multiple opponents and pace transitions for independent animation and
  continuous stride phase.
- Confirm unchanged movement speeds and tackle feel at the canonical 1.4 radius.
