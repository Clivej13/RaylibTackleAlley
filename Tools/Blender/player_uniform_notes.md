# Lower-body uniform refactor

The full rebuild now also includes the upper-body clothing pass described in
`player_jersey_notes.md`; this note documents the preserved lower-body work.

Run `Tools/Blender/rebuild_player_uniform.py` through Blender MCP after upstream
body or animation generation. The pass updates saved sources; it does not rebuild
the player, rig, or approved actions. No manual weight painting is required.

## Construction

- `Pants`: 12-sided waist and leg shells, 336 triangles, subtle padded thigh/knee
  volume. Uses the original anatomical smooth weight ramps on Hips and each
  corresponding UpperLeg/LowerLeg. Neutral gray `Pants` material.
- `Socks`: thin shells derived from the existing calf/ankle surfaces with their
  exact original weights. Off-white `Socks` material, independently recolorable.
- `Cleat_L`, `Cleat_R`: faceted toe/upper, defined sole, raised heel profile and
  six hexagonal studs each. Black `Cleats` and gray `Cleats_Sole` materials.
  Each vertex has weight 1 on the matching existing Foot bone.
- Original pelvis/groin/glutes/thigh/knee/foot meshes retain their names, vertex
  positions and weights as hidden editable references. `uniform_reference`
  excludes them from these exports. Toggle visibility to inspect the originals.
  Calf/ankle surfaces remain as sock-colored underlayers.
- 1,280 new triangles; replacing hidden reference surfaces yields only 92 net
  additional exported triangles. No new bones.

## Validation

`validate_player_uniform.py` compares all four saved sources against their Git
HEAD baselines: original mesh positions/topology/weights, skeleton/rest matrices,
action keys and handles, frame ranges/FPS and helmet/pad materials are unchanged.
The upgrade also checks normalized weights, no opposite-leg influences, finite
deformation, and exact foot attachment on every frame of the retained approved
Jog, Run and Sprint actions. Maximum attachment error is below 1e-5 scene units.

The three exported validation clips retain all 75 existing animation channels
with zero numerical difference against their approved HEAD validation GLBs.
The canonical export also matches approved Jog exactly. The former canonical
HEAD GLB differed from approved Jog; the approved Jog validation asset is used
as the motion authority, consistent with the existing canonical export script.

Front, side and rear standing previews plus seven frames per clip in two views
were visually inspected. No obvious gaps, clothing penetration, opposite-leg
pulling or detached shoes were observed. This is sampled visual clipping review,
not a proof of zero triangle intersections at every fractional frame. Hip/leg
shell overlap is intentional. Approved animation poses were not adjusted.

## Outputs

Under `Assets/Models/`:

- `lowpoly_human_rigged.blend`
- `lowpoly_human_jog.blend`, `lowpoly_human_run.blend`, `lowpoly_human_sprint.blend`
- `lowpoly_human_jog_validation.glb`, `lowpoly_human_run_validation.glb`,
  `lowpoly_human_sprint_validation.glb`
- `football_player.glb`

Under `Assets/Models/UniformPreviews/`:

- `Standing_Front.png`, `Standing_Side.png`, `Standing_Back.png`
- `Jog_FrontThreeQuarter.png`, `Jog_Side.png`
- `Run_FrontThreeQuarter.png`, `Run_Side.png`
- `Sprint_FrontThreeQuarter.png`, `Sprint_Side.png`
- `Jog_ContactSheet.png`, `Run_ContactSheet.png`, `Sprint_ContactSheet.png`
- `validation.json`, `export_validation.json`

Other saved gameplay animation assets were not regenerated in this scoped pass.
No C# or gameplay code was edited; pre-existing workspace changes remain intact.
