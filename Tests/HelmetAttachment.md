# Helmet attachment blocked by runtime asset representation

ThreeD is upgraded to 0.1.1. No opponent rendering or attachment code is changed.

Inspection of football_player.glb's JSON chunk confirms:
- Head -> HelmetAssembly -> ChinStrap, Facemask, Helmet, Visor.
- All four equipment nodes reference unskinned meshes (no skin, JOINTS_0 or WEIGHTS_0).
- Their child node transforms are identity; HelmetAssembly carries the authored offset.
- A separate HelmetAssemblyRoot node has no children; it is not the equipment parent.
- ShoulderPads references skin 0 and both its primitives have JOINTS_0/WEIGHTS_0.
  It does not have the helmet's unskinned rigid-node representation.

assets.json loads one complete FootballPlayer model. Jog comes from that GLB;
Run and Sprint come from their validation GLBs as animation assets only.
Each opponent owns a ModelInstance and three AnimationPlayers sharing only that
opponent's instance; its selected player applies the pose before Draw.
Opponent.Draw calls DrawModelEx on the complete model, including equipment.

The installed ModelInstance exposes Model only. Raylib Model/Mesh expose flat
mesh/material arrays without exported mesh names, node hierarchy, or node-local
transforms. There is no named HelmetAssembly selection/exclusion API. Mapping
GLB mesh/node identity onto native array slots would rely on importer ordering.
Per the task's stop condition, no mesh-index/material-name workaround is added.

## Required export handoff (not performed)

1. Export an opponent body GLB excluding Helmet, Facemask, Visor and ChinStrap.
   Preserve the existing skin, skeleton, bind transforms, body geometry and clip
   compatibility; leave ShoulderPads unchanged.
2. Export those four equipment objects together as a separate rigid helmet GLB.
   Preserve their geometry, materials and relative transforms. Bake their static
   child transforms into a documented assembly-local coordinate system so runtime
   drawing does not depend on preservation of the glTF node hierarchy.
   Include no armature skin or animated bone-parent channels in this asset.
3. Supply attachment metadata from the same source reference pose and coordinate
   basis: the assembly model-space transform H and Head model-space transform B,
   or their precomputed row-vector offset O = H * inverse(B). Include any basis
   conversion in both consistently. Do not substitute an animation frame for
   this authoring reference or use the unrelated HelmetAssemblyRoot empty.
4. Keep the original complete model available for other consumers; map only
   opponent visuals to the body-only and separate helmet assets in a follow-up.

With that handoff, the intended row-vector attachment is
O * animatedHeadModel * opponentWorld, querying the active opponent's own
AnimationPlayer.TryGetBoneTransform("Head", ...). Opponent world must match
DrawModelEx, including model.Transform, scale, yaw and grounded translation.
No attachment matrix is implemented or claimed validated in this change.
A later attachment can retain its last world matrix for detachment; no equipment
has been skinned, merged, or otherwise made non-detachable here.

The existing complete draw remains the sole rendering path, so no duplicate is
introduced, but the reported helmet alignment defect remains unresolved.
AnimatedHeadTests verifies the 0.1.1 assembly and changing Head transforms without
playback restart for Jog, Run and Sprint. It is API/asset readiness coverage,
not an attachment or visual correctness test.

After the export handoff and implementation, manually check Jog/Run/Sprint,
four independent opponents (Jog/Run/Sprint/Run), pace transitions, no helmet lag,
clipping or duplicate draw, all four equipment pieces aligned, and unchanged
movement, root motion and tackling. No interactive visual validation was performed.
