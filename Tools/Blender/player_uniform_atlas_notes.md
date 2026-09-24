# Runtime uniform atlas

`Assets/Models/football_player.glb` now assigns one material named exactly
`Uniform` to Jersey (including both sleeves), Undershirt, Pants, Socks,
the four existing calf/ankle underlayers and the Helmet shell.
Other material definitions and their names remain unchanged.

Replace Uniform's base-colour/albedo texture at runtime with a PNG painted
against `Assets/Models/UniformAtlas/uniform_uv_layout.svg`. UV channel 0 is
used. Base-colour factor is white, metallic is zero, and there are no vertex
colours or team graphics tinting the replacement texture. The embedded
`uniform_reference.png` is a temporary labeled checker, not a team uniform.
PNG replacements using this layout need no Blender re-export. Square 2048px
textures match the reference; other square resolutions use the same UVs.

The layout SVG uses the same top-left image origin as the PNG. The companion
`atlas_regions.json` lists normalized rectangles in Blender UV coordinates
(bottom-left origin). Chest and back are upright, continuous projected panels.
Sleeves, helmet sides/crown, trousers front/back and left/right socks each have
separate reserved regions. Side panels/collar facings and undershirt have their
own regions. Left/right refer to the player's anatomy. Paint using the actual
island outlines, allowing colour bleed beyond edges within the gutters.
Curved surfaces have multiple islands; the checker previews show their seams.

Run `Tools/Blender/atlas_player_uniform.py` using Blender MCP to reproduce the
asset, editable `football_player_uniform.blend`, atlas, previews and validation
report. Reruns use the preserved `UniformAtlas/football_player_before_atlas.glb`
when the current GLB already contains Uniform. To atlas a newer source, first
restore/export that new source without Uniform, then run the script. Existing
upstream model-generation scripts do not automatically invoke this pass.

Blender performs the UV operations on disposable welded meshes. The script's
GLB writer then copies original vertex attributes exactly, splitting vertices
only as needed for UV seams. Triangle positions/order, normals, joint indices,
weights, node transforms, hierarchy, skin bindings and animation data are
preserved. The original Jog animation retains all 75 channels and exact binary
samples; it is not resampled by a Blender animation export. The saved blend is
an editable import of the delivered GLB; use the script for an exact-data build.

Validation asserts exact per-triangle-corner attribute equality and exact
node/skin/animation JSON plus original binary preservation. At 2048px, a
triangle-interior raster check found zero overlapping UV pixels. Front, back
and side reference renders of the reimported GLB were inspected; chest/back
labels are upright and continuous. This validates asset loading and texture
mapping in Blender; no C# runtime changes or in-game tests were performed.
