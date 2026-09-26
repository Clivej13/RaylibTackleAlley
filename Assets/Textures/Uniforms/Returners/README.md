# Returner uniforms

Run `Tools/Blender/create_returner_uniforms.py` through Blender MCP from this
repository. It reads the roster numbers, runtime block-digit alphabet, current
football_player.glb, atlas_regions.json and artwork_guides.json. It produces five
2048px opaque sRGB atlas PNGs here, plus front/rear renders, editable preview
.blend files and a preservation report in Previews/. It never exports over a
player model or animation. No GLB re-export is necessary for these texture swaps.

| Returner | Number | Asset key | Look |
| --- | --- | --- | --- |
| Marcus Reed | 11 | ReturnerMarcusUniform | Navy, single gold sleeve band, light numbers and pants |
| Eli Brooks | 22 | ReturnerEliUniform | Teal, twin light sleeve bands, light numbers and pants |
| Jalen Price | 7 | ReturnerJalenUniform | Light torso, navy shoulders/pants, gold/light/gold sleeve bands, navy number |
| Darius Stone | 34 | ReturnerDariusUniform | Burgundy, navy sleeve band with gold centre, light numbers, navy pants |
| Noah Grant | 26 | ReturnerNoahUniform | Gold torso, navy shoulders, light-edged gold sleeve bands, navy numbers, light pants |

All share navy helmets, trim, undershirts and socks; guided outer trouser stripes;
a small gold/navy bar below each number; and the existing block alphabet. No names,
logos, baked shading, extra geometry, material slots, UV changes or rig changes.
The front/back artwork uses the existing projected panels. Minor band steps and
trouser stripe width changes at original UV/geometry joins remain as documented
in the parent atlas README. Solid helmets avoid the known crown/rear stripe gap.

The optional `Uniform` field beside each returner's `Id` and `Profile` in
returners.json references a Texture key registered in assets.json. Startup requires
all roster textures through AssetManager; menu highlighting and gameplay selection
both use that key. Omitted/null keys, unregistered assets and missing export files
fall back to config.OffenseUniform before manifest validation/loading. Blank keys
are rejected. Textures remain loaded until shutdown. The existing owned numbered
texture lifecycle, reset behavior and cleanup are retained.

The exported PNGs already contain the roster number on chest and back. Runtime
PaintNumber redraws those same patches using the current profile number and
contrasting light/navy ink. Keep decoration outside that reserved rectangle.
Changing a roster number works immediately at runtime after restart; rerun this
script to update the source PNG and Blender preview too.

Validation: all ten Blender front/rear renders inspected; preservation SHA-256s
record existing top-level model/animation GLBs, blends and offense/defense textures.
Runtime tests compare sampled pixels across the whole selected atlas (including
numbers) in all five menu previews and carriers, alongside selection, cleanup,
reset and existing material swap checks.

Manual review remaining: normal gameplay camera distance and fullscreen lighting,
number readability in motion, fine sleeve stripes under distant mipmapping, and
seams through sprint/cut/tackle poses on the five profile body proportions. Blender
previews show the shared reference body in rest pose; game profiles apply their
usual size scaling.
