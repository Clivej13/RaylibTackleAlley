# Modern helmet refinement

Execute `Tools/Blender/refine_player_helmet.py` followed by
`Tools/Blender/validate_player_helmet_refinement.py` through Blender MCP.
This is an incremental pass over the existing uniform-refined saved sources;
it reuses `upgrade_player_uniform.export` and `export_football_player.py`.
It does not run the legacy Stage 5 player generator. If regenerating upstream
assets from scratch, apply this pass after the uniform/silhouette passes.

Original coordinates/topology are retained as JSON custom properties on each
existing helmet object. Repeated execution produces identical geometry. Source
and GLB baselines are preserved in `Assets/Models/HelmetRefinementPreviews/Baseline/`.

## Geometry

- Helmet: existing 596-triangle shell reshaped, crown lowered by up to 24 mm,
  front/rear depth extended, temple width increased subtly, lower side edge
  lowered and lower rear pulled inward for neck clearance. Flat polygon normals
  and existing shell thickness/topology retained.
- Facemask: retain and reshape the original cage and mounts. Slightly more
  forward projection; lower side rails rise toward shell mounts to clear the
  strap. Add a continuous brow rail and two lower vertical supports, all simple
  six-sided tubes within the existing Facemask object. 440 -> 560 triangles.
- Visor: shorten height from 74 to 52 mm, narrow by 9%, tuck behind the cage.
  Original material, thickness and 104-triangle topology retained.
- ChinStrap: bring the original cup forward slightly; route the existing broad
  bands behind the cage and move the side anchors onto the shell. Original
  cup/band/snap structure and 184-triangle topology retained.
- No new objects or bones, logos, decals, hardware detail or smooth shading.

## Preservation

Helmet, Facemask, Visor and ChinStrap remain separate named meshes parented to
PlayerRig using the existing linear ARMATURE modifier. Every vertex remains
100% weighted to Head, including the added cage vertices. Existing weights are
unchanged in value; the added vertices receive the same Head weight.

Skeleton/rest matrices, actions/keys/handles, source timing/FPS, object transforms,
attachment settings, player body, uniform, lower body and equipment materials
are preserved. The canonical and three validation GLBs retain 24 joints and
exactly match all 75 pre-pass animation channels. C# content hashes are unchanged.
No branches, commits, pushes or gameplay code changes.

## Validation

`validation.json` records rest plus 57 Jog, 49 Run and 41 Sprint samples, covering
each action's complete frame range at half-frame intervals (148 total poses).
All samples have zero triangle intersections between helmet parts and the head
or neck. Maximum Head-relative attachment deviation is below 0.000001 m.

There are zero shell/visor, cage/visor or cage/chinstrap intersections. The cage
mounts and strap anchors intentionally intersect the shell to form attached
junctions; these are retained and reported rather than described as zero mesh
intersections. No loose or visibly floating parts were found in the close-up
front, side and three-quarter review. Full-player views and Jog/Run/Sprint
contact sheets were reviewed for proportions, collar clearance and attachment.
These are geometric/sample checks, not a guarantee for arbitrary untested poses.

Full exported player: **8,290 -> 8,410 triangles (+120, about 1.45%)**.
Combined helmet parts: **1,324 -> 1,444 triangles (+120)**.

## Files changed in this pass

New scripts/documentation:

- `Tools/Blender/refine_player_helmet.py`
- `Tools/Blender/validate_player_helmet_refinement.py`
- `Tools/Blender/player_helmet_refinement_notes.md`

Updated under `Assets/Models/`:

- `lowpoly_human_rigged.blend`
- `lowpoly_human_jog.blend`
- `lowpoly_human_run.blend`
- `lowpoly_human_sprint.blend`
- `football_player.glb`
- `lowpoly_human_jog_validation.glb`
- `lowpoly_human_run_validation.glb`
- `lowpoly_human_sprint_validation.glb`

New under `Assets/Models/HelmetRefinementPreviews/`:

- `Helmet_Front.png`, `Helmet_Side.png`, `Helmet_ThreeQuarter.png`
- `Player_Front.png`, `Player_Side.png`, `Player_FrontThreeQuarter.png`
- Matching `Before_Helmet_*.png` and `Before_Player_*.png` baselines
- `Before_ThreeQuarter.png` (initial inspection close-up)
- `Comparison_Front.png`, `Comparison_Side.png`, `Comparison_ThreeQuarter.png`
  (before left, after right, same camera framing)
- `Jog_ContactSheet.png`, `Run_ContactSheet.png`, `Sprint_ContactSheet.png`
  (same seven-frame, two-view layout as the previous uniform previews)
- `preservation.json`, `validation.json`
- `Baseline/`: four pre-pass .blend files and four pre-pass GLBs

Other saved gameplay clips and previous-stage preview folders are not updated
by this scoped helmet pass. Existing unrelated workspace changes are retained.
