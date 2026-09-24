# Stadium refactor

Run `Tools/Blender/refactor_stadium.py` through Blender MCP. The existing `create_stadium.py` entry point delegates to it. Input is the archived, unmodified original `Tools/Blender/Source/stadium_original.glb`; generation never uses the new output as input.

## Existing asset

54 meshes, flat hierarchy, bounds X -52..52, Y -84..84.75, Z approximately 0..25. Field reference is 53.333 x 120 yards, centred at (0,0,0), Z=0. Sixteen original objects retained: four concourse foundations, scoreboard panel/face/two supports, four lighting masts and heads. World-space vertex comparison after GLB export found maximum error 0.00000191 units. Thirty-eight old seating strips, rear walls, piers, barriers and tunnel shapes replaced. Original simple concrete, dark structure and emissive scoreboard/light materials retained with those objects.

## New structure

STADIUM contains Stadium_Base, Lower_Bowl (four named stand subcollections), Upper_Bowl (two named sideline subcollections), Aisles, Tunnels, Railings, Structural, Stadium_Details. Matching empty nodes preserve category hierarchy in GLB. FIELD is an imported preview reference outside STADIUM. FIELD_EQUIPMENT is separate and empty: no goalposts or pylons existed in the source stadium. No field or equipment is included in stadium.glb.

Lower rows: west/east/north 12, south 10. Row depth 0.9 yards, rise 0.5 yards, approximately 29-degree rake. First seating tread Z=2.5; final tread Z=8 (south 7). Upper tiers: west 9 rows, east 7; depth 0.85, rise 0.6, approximately 35-degree rake. Seating tops approximately 17.36 and 16.16 yards. Concourse separation and visible support ribs remain below upper seating. Four deliberately open corner plazas replace touching stand ends. No individual seats or crowds.

Sixteen lower stair channels: five per sideline at Y=-48,-24,0,24,48; three per end at X=-18,0,18. Ten matching upper stair channels. Central channels 3.2 yards wide, others 2.0. Six framed, dark shallow vomitory portals at the rear of the lower bowl: two per sideline at Y=+/-24 and one centred at each end. No hidden tunnel interiors.

Outer footprint 110 x 180 yards, centred exactly on the field; compatible with current StadiumAssetWidth/Length so runtime horizontal scaling is 1:1. Structure up to 18.3 yards; original lights to 25 yards. Small apron substrate extends to Z=-0.24. Front walls are 5.8935 yards outside sidelines and 6.56 outside end lines. Entry stairs project inward: minimum clearance 2.7335 yards at sidelines, 3.4 behind end lines. No stadium geometry extends into the field rectangle. Field remains scale anchor.

## Performance and materials

139 mesh objects, 20,040 exported triangles. Repeated solids batched by section and material; no subdivisions or seat/fan meshes. Nine used materials: Bowl Concrete, Bowl Muted Blue, Bowl Rail Metal, Bowl Structural Trim, Tunnel Shadow; retained Concrete Grey, Dark Structure, Floodlight White, Scoreboard Accent. No stadium textures added.

## Validation and previews

GLB round-trip verified dimensions, mesh/triangle counts, positive unit transform determinants and exclusion of football/goalpost objects. Generator checks unit scale and non-degenerate polygons. All non-stadium Assets files are hashed before/after and verified unchanged, including field GLB, blend, textures, football and player assets. Overview and field-level renders visually inspected for tier separation, stepped rake, access breaks, portal readability and obvious floating/z-fighting issues. This is asset/render validation; the game was not launched and runtime performance was not benchmarked.

Previews:
- Assets/Previews/stadium_before.png
- Assets/Previews/stadium_preview.png (isometric overview; saved blend camera)
- Assets/Previews/stadium_field_level.png (2-yard eye height)
- Assets/Previews/stadium_gameplay_preview.png (55-degree vertical FOV, 5.5-yard camera height, 6.75-yard trailing distance, 5.5-yard lookahead, 1.1-yard target height; player position mapped into asset coordinates)

Outputs: Assets/Models/stadium.blend and stadium.glb, these previews, Tools/Blender/stadium_refactor_validation.json. Reproduction files: refactor_stadium.py, create_stadium.py, Source/stadium_original.glb, this document. No C# code, configuration, field, goalpost, pylon, player, gameplay or camera source files were modified.
