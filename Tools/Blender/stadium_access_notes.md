# Stadium field-edge and access refactor

Run Tools/Blender/refactor_stadium_access.py through Blender MCP. create_stadium.py delegates to this current stage. Source snapshot: Tools/Blender/Source/stadium_before_access.glb. This preserves the prior stadium rather than rebuilding its seating.

## Retained and replaced

124 mesh objects retain their world-space vertices, including lower/upper seating, bowl stairs, structural supports, six spectator concourse portals, scoreboard, towers and most foundations. Two corner plaza meshes are locally cut for ramps. Thirteen objects replaced: four front-wall batches, four front-rail batches, four projecting entry-stair batches and the old runoff apron. No field assets are altered.

Inspection found the old stadium.blend preview reference enlarged to 58.326 x 131.234 due to glTF import applying scene-unit conversion. Both actual football_field.blend and football_field.glb remained 53.333 x 120. The new assembly appends the authored field object directly without conversion. Its origin, vertices, UVs and material remain authored, with identity scale. No field source files were resized or written.

## Field boundary and operations

Field X +/-26.6665, Y +/-60, Z=0; one unit = yard. Stadium bounds remain X +/-55, Y +/-90; lights reach Z=25. New retaining walls are 2 yards high at X +/-32.72 and Y +/-66.72, with blue padding and a 1.09-yard rail above. Wall inward face is 5.7735 yards beyond sidelines, 6.44 behind end lines. The paving is a separate architectural apron outside the field rectangle.

No stairs descend from the stands onto the field or apron. Continuous retaining walls and guardrails close all former stair access openings. Seating-bowl aisles remain behind this boundary; field access is via the two player-tunnel ramps.

Two new player entrances are centred at (33.5,65.5) and (-33.5,-65.5): northeast and southwest corners. Concrete portals with simple dark recesses use the existing plaza elevation of 0.7 yards. Each ramp is 5.4 yards wide, 5.6 yards long and rises 0.7 yards (1:8). Return walls tie the entrance architecture into the end stands. Six original spectator portals remain at rear lower-bowl concourses. The two ramps are the only major field-edge access routes.

Two team zones run Y -20..20, one on each sideline. Each has three six-yard placeholder benches, two simple equipment boxes, end barriers and an ochre staff-lane edge. Pads begin 2.3335 yards beyond sidelines. Benches and boxes stay behind a clear field-facing runoff strip. Rear bench-to-padding clearance is approximately 2 yards. Props have no detailed fixtures.

Two new goalposts were added because no posts existed in the source. They are FIELD_EQUIPMENT, centred at X=0 on Y +/-60 end-line planes. Crossbar top is 10 feet (3.3333 yards), clear upright gap 18 ft 6 in (6.1667 yards), uprights extend 35 feet above crossbar. Support bases sit two yards behind end lines at Y +/-62, with protective padding. No nets, fans, crowd or extra decoration.
Source: https://operations.nfl.com/rules-officiating/nfl-football-basics/football-terms

## Hierarchy and exports

FIELD: authored field reference, not exported with the stadium.
FIELD_EQUIPMENT: Benches, Sideline_Props, Endzone_Equipment (GoalPost_Home/Away and base pads).
STADIUM: preserved bowl/structural/details groups plus Field_Boundary, Player_Tunnels, Sideline_Zones; existing Railings and Tunnels remain.

Assets/Models/stadium.glb is the game assembly with independent STADIUM and FIELD_EQUIPMENT root nodes. Benches and goalposts are separate named meshes, never merged into the stadium structure. Including both roots in the existing game asset makes them available without C# or asset-registry changes. Assets/Models/field_equipment.glb is an equipment-only reusable export; do not load it alongside the assembly or equipment will appear twice.

Stadium structure: 164 meshes / 21,272 triangles. Equipment: 26 meshes / 640 triangles. Game assembly: 190 meshes / 21,912 triangles. Flat, low-poly materials; no new textures. Added materials: Edge Warm Concrete, Apron Graphite, Team Zone Slate, Boundary Blue Padding, Access Safety Ochre, Bench Pale Grey, Goalpost Yellow. Existing bowl, rail metal and tunnel-shadow materials reused.

## Validation

Export round trip confirms root separation, field exclusion, 110 x 180 footprint, counts and unit transform determinants. 124 retained meshes match archived world-space geometry within 0.0001 yards. All generated polygons have nonzero area. Ramp surface ray checks at three points verify the expected slope and no surviving plaza above it. Field reference verified at 53.333 x 120, origin zero and scale one. All pre-existing non-stadium assets except the designated equipment output are SHA256 checked unchanged. Details are in stadium_access_validation.json.

Overview, field-level/tunnel and sideline renders inspected for boundary readability, access continuity, props, tier separation and obvious intersections/floating/z-fighting. This is Blender/export validation, not an in-game performance test. Gameplay collision code and camera code remain unchanged; the new architecture is visual geometry only.

## Files for this pass

- Tools/Blender/refactor_stadium_access.py (new)
- Tools/Blender/create_stadium.py (updated current entry point)
- Tools/Blender/Source/stadium_before_access.glb (new reproducible input)
- Tools/Blender/stadium_access_validation.json (new)
- Tools/Blender/stadium_access_notes.md (this file)
- Assets/Models/stadium.blend (updated; overview camera saved)
- Assets/Models/stadium.glb (updated game assembly)
- Assets/Models/field_equipment.glb (new separate equipment export)
- Assets/Previews/stadium_preview.png (updated overview)
- Assets/Previews/stadium_field_level.png (updated tunnel/retaining-edge view)
- Assets/Previews/stadium_gameplay_preview.png (updated gameplay-style view)
- Assets/Previews/stadium_sideline_preview.png (new bench/team-area view)

No C#, framework packages, player assets, field files, gameplay configuration, Git branches, commits or pushes changed.
