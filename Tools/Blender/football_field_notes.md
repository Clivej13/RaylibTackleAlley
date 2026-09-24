# Football field asset contract

- 1 Blender coordinate unit = 1 yard. Blender display scale: 0.9144 metres per unit; no geometry scaling on export.
- Width X = 53.333, length Y = 120, surface Z = 0. Origin (0,0,0); identity mesh transform.
- GLB conversion: X width, Y up, Z length; blue end at negative Z. The original orientation and surface centre are retained.
- Goal lines Y = +/-50; end lines +/-60; sidelines X = +/-26.6665. Each end zone is 10 yards.
- NFL layout: five-yard lines, one-yard hashes, four-inch paint, two-foot hashes. Inbound hash edges 70 feet 9 inches from sidelines. Width uses the explicitly requested decimal approximation of 53 1/3 yards.
- Boundary paint is clipped inward to the exact footprint. No external apron, six-foot perimeter border, stadium, numbers or logos are included.
- One UV-mapped quad, exported as two triangles, one material. Textures are 2048 x 4096 PNGs under Assets/Textures/FootballField. field_layout and grass_color_detail are separate source textures; field_basecolor combines them for portable GLB rendering. grass_normal is a subtle tangent-space normal map. GLB embeds base colour and normal textures.
- Rebuild with Tools/Blender/create_football_field.py through Blender MCP. Fixed random seed 1709. The generator never reads or writes stadium assets.

## Inspection before replacement

Imported football_field.glb surface: 120 x 53.333 units. Including old exterior strips: 121.300 x 54.633, height about 0.412. Proposed surface scale relative to original turf: 1.000 in both horizontal axes. Removing the border reduces total bounds by 1.07% length and 2.38% width.

Imported football.glb world bounds in Blender: X 0.166600, Y 0.280000, Z 0.169217 units. At the canonical yard scale, long axis is 10.08 inches, cross-section about 6.00 x 6.09 inches. The standalone asset is not oversized; its length is below NFL 11 to 11.25 inches. No football files were changed. This measures the asset, not animation-dependent runtime transforms.

Source: https://operations.nfl.com/rules-officiating/2026-nfl-rulebook

## Validation

GLB round trip through Blender: dimensions (53.333000183,120,0), one mesh, two triangles, one UV set, embedded base colour and normal maps. Preview rendered and inspected. Current config already uses FieldAssetWidth=53.333 and FieldAssetLength=120, so existing FitBounds yields unit horizontal scale. No gameplay code or configuration was changed; existing gameplay lane limits remain independently configured. Runtime normal-map shading depends on the game shader and was not tested in-game.

Stadium SHA256 before and after: 85DD953ECD693616336F8F93215238D0CBD4027E04323927A2F4C2AA02DB6B4B.
