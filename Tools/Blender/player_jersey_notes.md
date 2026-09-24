# Upper-body uniform pass

Run `upgrade_player_jersey.py` through Blender MCP to update the four existing
rigged/locomotion sources and export their validation GLBs plus the canonical
player. Then run `validate_player_jersey.py`. `rebuild_player_uniform.py` now
includes both clothing passes and their validators for complete regeneration.

## Construction and editability

- `Undershirt`: 908 triangles. Separate fitted shells copied from the original
  torso, shoulders, neck base and upper arms with 3 mm clearance. Original deform
  weights are copied and normalized, excluding non-bone anatomical groups. A
  shortened neck binding leaves the upper neck exposed. Dark gray `Undershirt`
  material keeps this visually subordinate to the jersey.
- `Jersey`: 576 triangles. Separate low-poly torso panels follow the padded
  chest envelope and hang to the upper hips, with a loose waist and sleeves.
  The torso follows Chest over the pads, blending to Spine/Hips below them.
  Sleeve envelopes are derived from the actual shoulder cap and padding shapes,
  with clearance and a transition from Clavicle to UpperArm at the cuffs.
  Collar and cuff facings prevent viewing pads through garment openings during
  forward lean and arm swing. No cloth simulation, extra bones or subdivisions.
- `Jersey` and `Jersey_Sleeves` materials use the same muted slate default.
  They can be recolored separately for sleeve variants. There is no branding.
- Total additional triangles: 1,484. All existing mesh objects, positions,
  topology, weights, parent relationships, materials and visibility are preserved,
  including ShoulderPads, Pants, Socks, Cleat_L and Cleat_R. Original body
  proportions, bone rest matrices and existing actions remain unchanged.

## Validation

`JerseyPreviews/validation.json` records before/after preservation signatures,
normalized skin weights, finite deformation, no opposite-side arm influences,
and C# content preservation. Original body and equipment are untouched.

`JerseyPreviews/export_validation.json` records separately identifiable skinned
Undershirt/Jersey exports, 24 existing joints, and exact equality of all 75
existing animation channels against approved Jog/Run/Sprint validation GLBs.
Source timing, action keys and handles are also unchanged.

The intersecting sleeve/torso panels intentionally overlap internally. Raw
triangle-intersection counts are recorded, rather than claiming a fully unioned
cloth volume. To distinguish those hidden overlaps from visible pad exposure,
the validator uses temporary black occluders and red emissive pads and renders
rest plus every integer animation frame from front, side, rear and three-quarter
views at 320 x 360. All 304 final views contain zero exposed pad pixels at the
documented threshold. Diagnostic materials are never saved to the assets.

Standing previews and seven-frame, two-view contact sheets for each animation
were visually reviewed: no obvious pad penetration, base-layer breakthrough,
armpit gaps, sleeve collapse or hem/pants problems. This is gameplay-scale
visibility validation, not a proof about arbitrary views or fractional frames.

## Files changed by this pass

Scripts/notes under `Tools/Blender/`:

- Added `upgrade_player_jersey.py`, `validate_player_jersey.py`, this note.
- Updated `rebuild_player_uniform.py` to include the new pass.
- Updated the legacy baseline signature in `upgrade_player_uniform.py` to
  exclude the newly added upper garments when comparing with pre-clothing HEAD.
- Updated `player_uniform_notes.md` with the upper-pass reference.

Regenerated under `Assets/Models/`:

- `lowpoly_human_rigged.blend`
- `lowpoly_human_jog.blend`, `lowpoly_human_run.blend`, `lowpoly_human_sprint.blend`
- `lowpoly_human_jog_validation.glb`, `lowpoly_human_run_validation.glb`,
  `lowpoly_human_sprint_validation.glb`
- `football_player.glb`

New outputs under `Assets/Models/JerseyPreviews/`:

- `Standing_Front.png`, `Standing_Side.png`, `Standing_Back.png`
- `Jog_FrontThreeQuarter.png`, `Jog_Side.png`, `Jog_ContactSheet.png`
- `Run_FrontThreeQuarter.png`, `Run_Side.png`, `Run_ContactSheet.png`
- `Sprint_FrontThreeQuarter.png`, `Sprint_Side.png`, `Sprint_ContactSheet.png`
- `validation.json`, `export_validation.json`

The three contact sheets and `export_validation.json` in `UniformPreviews/`
were also refreshed by the existing lower-body validator. Other gameplay clip
assets were not regenerated. No C# or gameplay code was edited, no branches were
changed, and nothing was committed or pushed.
