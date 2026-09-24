# Football silhouette refinement

Run `refine_player_silhouette.py`, then `validate_player_silhouette.py` through
Blender MCP. The existing `rebuild_player_uniform.py` now runs both at the end.
This pass edits existing meshes in place. Original garment coordinates are saved
as mesh custom properties so repeated runs are deterministic. Pre-pass sources
are retained under `Assets/Models/SilhouettePreviews/Baseline/`.

## Changes

- Jersey: preserve padded chest width, reduce waist half-width from .202 to .189,
  retain a slightly wider hanging hem, and lower the hem by .028 scene units.
  Add subtle convexity to front/back panels. Reduce sleeve-cap envelope from
  1.28 to 1.14 times the underlying cap and reduce cuff radius by 10%. Tighten
  the existing collar opening to cover chest edges during Sprint's forward lean.
- Undershirt: retain fitted body and sleeves; slightly raise/widen existing neck
  binding. Lighter charcoal and smooth normals clarify it as a separate layer.
- Pants: smooth normals only; positions, topology, fitted volume, leg cutoffs,
  object structure and weights remain unchanged.
- Smooth garment normals replace slab-like facet shading without subdivision.
- ShoulderPads, socks, cleats, anatomical body, helmet, facemask, visor and
  chinstrap geometry remain unchanged. Helmet fit was inspected and retained;
  any larger shape redesign is deferred.

## Preservation and validation

All four saved sources preserve skeleton/rest matrices, existing actions and
keyframe handles, timing/FPS, every skinning weight, every mesh topology and all
non-garment geometry. Canonical and validation GLBs retain 24 joints and all 75
animation channels match the pre-pass source exports exactly. C# hashes are
unchanged by the refinement script. No commits, pushes or branch changes.

Triangle count: **8,290 before and after; delta 0**, including the existing
exported body underlayers and excluding hidden uniform reference meshes.

Rest plus every integer Jog (29), Run (25) and Sprint (21) frame were checked in
four camera views at 320 x 360: 304 pad-visibility views and 304 torso-coverage
views. Both final checks report zero exposed target pixels. The collar gap
detected during development was fixed in geometry without altering weights.
Finite garment deformation and normalized weights are checked by the existing
uniform validator functions. Seven frames per animation, in front-three-quarter
and side views, were reviewed for sleeve/armpit shape, waist overlap, hip/thigh
deformation, sock/cleat transitions and helmet attachment. No obvious visible
clipping was found in those sampled views. Internal overlapping panels remain;
these checks do not prove zero intersections at arbitrary fractional frames or
camera angles. Diagnostic materials are not saved to sources.

## Files changed by this pass

Scripts:

- `Tools/Blender/refine_player_silhouette.py` (new)
- `Tools/Blender/validate_player_silhouette.py` (new)
- `Tools/Blender/rebuild_player_uniform.py` (updated workflow)
- `Tools/Blender/player_silhouette_notes.md` (new)

Updated assets under `Assets/Models/`:

- `lowpoly_human_rigged.blend`
- `lowpoly_human_jog.blend`
- `lowpoly_human_run.blend`
- `lowpoly_human_sprint.blend`
- `football_player.glb`
- `lowpoly_human_jog_validation.glb`
- `lowpoly_human_run_validation.glb`
- `lowpoly_human_sprint_validation.glb`

New outputs under `Assets/Models/SilhouettePreviews/`:

- `Standing_Front.png`, `Standing_Side.png`, `Standing_Back.png`
- `Standing_BeforeAfter.png` (before left, after right)
- `Jog_FrontThreeQuarter.png`, `Jog_Side.png`, `Jog_ContactSheet.png`
- `Run_FrontThreeQuarter.png`, `Run_Side.png`, `Run_ContactSheet.png`
- `Sprint_FrontThreeQuarter.png`, `Sprint_Side.png`, `Sprint_ContactSheet.png`
- `Jog_BeforeAfter.png`, `Run_BeforeAfter.png`, `Sprint_BeforeAfter.png`
  (original two-view sheet above, updated sheet below; identical framing/frames)
- `validation.json`, `export_validation.json`, `torso_coverage.json`
- `Baseline/`: four original source blends and three original validation exports.

Existing JerseyPreviews and UniformPreviews remain as earlier-stage comparisons.
Other saved gameplay clip assets are outside this Jog/Run/Sprint refinement and
were not regenerated. Pre-existing unrelated workspace changes were retained.
