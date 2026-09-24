# Uniform artwork template: targeted usability cleanup

All texture sources are 2048 x 2048. Every named region and its boundaries are
unchanged. Chest/back and all UVs outside the explicitly listed islands remain
exact. Left/right refer to the player's anatomy. PNG coordinates start at the top
left; atlas_regions.json and the transformation manifest use bottom-left UVs.

## Delivered files

- `uniform_template.png`: flattened labels, subtle wireframe and blue artwork guides.
- `uniform_template.ora`: editable OpenRaster source for Krita/GIMP with separate
  labels, artwork guides, wireframe and paint-base layers.
- `uniform_paint_base.png`, `uniform_uv_overlay.png`,
  `uniform_labels_overlay.png`, `uniform_artwork_guides.png`: separate layers for
  Photoshop or other editors. Import at original size and position.
- `test_uniform.png`: flat diagnostic colours, white chest/back 10s, a navy crown
  stripe, navy trouser side stripes, and white sleeve bands. No baked shading.
- `Validation/uniform_validation.blend`: editable import of the updated runtime
  GLB, test texture on the existing Uniform node, six cameras and preview lights.
- `Validation/front.png`, `back.png`, `left.png`, `right.png`,
  `front_three_quarter.png`, `rear_three_quarter.png`: regenerated validation views.
- `Validation/validation.json`: UV winding, padding, raster overlap and region checks.

The updated runtime model is `Assets/Models/football_player.glb`. The original
pre-cleanup file is preserved as
`Assets/Models/UniformAtlas/football_player_before_usability.glb`.
The runtime texture architecture and material assignments have not changed.

## Exactly which islands changed

Island IDs are deterministic connected components from the preserved input,
ordered by their original triangle indices. The two-digit IDs are printed on the
optional blue artwork-guide layer. Full primitive/triangle IDs, rotations,
translations and before/after bounds are in
`Assets/Models/UniformAtlas/usability_changes.json`.

| Region | Changed island suffixes | Purpose |
| --- | --- | --- |
| Helmet_crown | 00, 01, 02 | Align exterior rear/top, front/top and lower rear shell centerlines on one vertical paint line. |
| Helmet_crown | 03, 04, 05 | Reorient and place corresponding inner-shell panels in the adjacent column, clearing the exterior stripe column. |
| Helmet_crown | 06, 07 | Align front and lower rim centerlines with the exterior stripe column. |
| Helmet_crown | 08, 09, 10, 11, 12 | Translation only, to maintain clearance after the shell alignment. |
| Trousers_front | 00, 02, 05, 06 | Align the two upper outer-side panels and two outer leg panels for vertical stripe segments. |
| Trousers_back | 00, 02, 03, 08 | Align the matching upper-side and outer-leg panels for vertical stripe segments. |
| Sleeve_L | 00, 01, 02, 03 | Align the four sleeve wall panels so bands use horizontal segments. |
| Sleeve_R | 00, 01, 02, 03 | Align the matching four sleeve wall panels. |

29 existing islands changed. There was no global atlas repack, no island scaling,
no new unwrap, no island splitting/merging, and no vertex/index changes. Each edit
is a rigid rotation plus translation within the same named region. Crown island
13, sleeve caps 04/05, and the remaining trouser panels are unchanged. All other
regions, including chest/back and helmet sides, are unchanged.

## Painting straight graphics

The blue lines show usable stripe/band locations. Their exact coordinates and
sample widths are in `Assets/Models/UniformAtlas/artwork_guides.json`.

- Helmet: draw one vertical line centered at image X = 1754 px through the
  Helmet_crown region (the sample is 26 px wide). The exterior panels and rims
  share that centerline. The adjacent column holds the inner shell.
- Trousers: use the four vertical guide segments in each front/back region.
  They follow the outer side-seam edges; paint the same colour on corresponding
  front/back segments. The sample uses a 24 px rectangle centered on each seam,
  with approximately half its width on the island. Multiple upright segments
  are needed, rather than one line through the entire region.
- Sleeves: use four horizontal guide segments per sleeve. The sample band is
  approximately 32 mm wide on the model. Band heights differ between atlas
  panels; the guide locations make them meet around the arm. Moving a band to a
  different height requires adjusting each panel, followed by a preview check.

Paint into gutters but do not enter neighboring islands. Hide all three guide
layers before exporting a final opaque PNG. Labels cover some top-edge UV detail;
hide them when painting those areas. Load the PNG into Uniform's base-colour
texture on UV channel 0 with white tint. There is no need to re-export the model
for subsequent artwork-only edits. Earlier artwork for the changed islands must
be adapted to this updated template.

## Visual validation and limits

All six regenerated views were inspected in Blender 3.3.

- Chest/back numbers remain readable, upright, unmirrored and visibly unchanged.
- Sleeve bands now wrap around the sleeve using horizontal texture segments.
  Minor steps remain at some panel seams because their original shapes were
  preserved. They no longer require diagonal/rotated painting.
- Trouser stripes run vertically down both outer sides, using matching front/back
  segments. Small width/kink changes remain at the hip-to-leg transition of the
  existing geometry and UV shapes. No foreign region colours appear at the seams.
- The navy helmet stripe runs from the forehead over the crown, with a small
  residual change in alignment at the original shell-island join. At the rear,
  a white horizontal section interrupts it before the lower rear crown panel.
  That section belongs to Helmet_L/Helmet_R, not Helmet_crown. A complete rear
  continuation needs matching artwork in those unchanged side regions. Crown-only
  UV reorientation cannot remove this regional allocation limit. The dark stripe
  intentionally exposes the interruption rather than hiding it in white artwork.
- No visible cross-region texture bleeding in the rendered views. Edited islands
  have at least 8 px conservative bounding-box separation from other islands in
  their region; original approximately 12 px region-edge gutters are retained.
  Padding in untouched areas is unchanged. Very distant mip levels and runtime
  compression were not tested.

The atlas is more practical for straight artwork, but it is not a seamless
single-stroke garment unwrap. It is ready for chest/back graphics, guided trouser
stripes and sleeve bands. The helmet rear bridge still needs side-region artwork.
No in-game validation or animation deformation test was performed in this pass.

## Preservation and overlap verification

`Assets/Models/UniformAtlas/usability_audit.json` records a continuous triangle
intersection test over all 3,536 Uniform triangles, not just a pixel raster test:
zero overlapping triangle interiors, with a 1e-7 pixel-squared area tolerance.
The 2048-pixel raster check also finds zero overlaps; all UV windings are positive,
there are no degenerate UV triangles and no triangles cross a region boundary.

The GLB JSON is byte-for-byte preserved as part of its unchanged original header
and JSON chunk. Every binary byte outside the selected TEXCOORD_0 entries is
identical to the preserved pre-cleanup source. This verifies exact positions,
normals, indices/topology, skinning, skeleton/rig hierarchy, animations, material
assignments and all untouched UVs. Rigid-transform distance error after float32
storage is below 0.001 pixel. The region JSON was never edited.

## Reproduction

1. Run `Tools/Blender/cleanup_uniform_uv.py` via Blender MCP. It starts from the
   preserved pre-cleanup GLB, applies the local rigid transforms, patches only the
   corresponding UV bytes and regenerates the SVG, change manifest and guides.
2. Run `Tools/Blender/audit_uniform_usability.py` via Blender MCP.
3. Run `python Tools/Blender/create_uniform_textures.py` with Pillow installed.
   It uses Windows Arial Bold for labels/numbers.
4. Run `Tools/Blender/validate_uniform_textures.py` via Blender MCP to regenerate
   the test-material Blender scene and six previews.

The older `atlas_player_uniform.py` is the original atlas-generation pass and
does not include this cleanup. Do not run it alone and expect these orientations.
The delivered `Validation/uniform_validation.blend` is the current editable
preview source; older model-generation blend files were not rewritten.
Regeneration replaces the delivered templates, so save team artwork separately.
