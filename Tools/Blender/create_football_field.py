"""Create a deterministic low-poly football field for RaylibTackleAlley.

Run from the repository root with Blender's Python API. The field is centered
at the origin. The authored asset uses +Z as up, +Y as gameplay forward, and
is converted to the game's Y-up, -Z-forward space by the game importer.
"""

import bpy
import math
from mathutils import Matrix, Vector
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = PROJECT_ROOT / "Assets" / "Models"
BLEND_PATH = OUTPUT_DIR / "football_field.blend"

FIELD_WIDTH = 53.333
FIELD_LENGTH = 120.0
PLAYING_LENGTH = 100.0
END_ZONE_LENGTH = 10.0
SURFACE_THICKNESS = 0.30
PAINT_HEIGHT = 0.035


def log(message):
    print(f"[football-field] {message}", flush=True)


def make_material(name, color, roughness=0.82):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    return material


def add_box(collection, name, location, dimensions, material, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if material:
        obj.data.materials.append(material)
    if bevel > 0:
        modifier = obj.modifiers.new("Tiny edge softening", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        obj.modifiers.new("Weighted normals", "WEIGHTED_NORMAL")
    for old_collection in list(obj.users_collection):
        old_collection.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def aim_at(camera, target):
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def main():
    log("starting deterministic scene build")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    field_collection = bpy.data.collections.new("FootballField")
    bpy.context.scene.collection.children.link(field_collection)

    # Keep the material values explicit and Blender-version independent.
    turf = make_material("Turf Green", (0.075, 0.38, 0.12))
    end_zone_a = make_material("End Zone Blue", (0.055, 0.22, 0.48))
    end_zone_b = make_material("End Zone Gold", (0.78, 0.42, 0.045))
    paint = make_material("White Paint", (0.98, 0.99, 0.94), 0.7)
    sideline = make_material("Sideline Border", (0.12, 0.16, 0.13), 0.9)

    # Main slab plus two thin end-zone overlays. The visible grass surface is y=0.
    add_box(field_collection, "Turf Base", (0, -SURFACE_THICKNESS / 2, 0),
            (FIELD_WIDTH, SURFACE_THICKNESS, FIELD_LENGTH), turf)
    add_box(field_collection, "End Zone North", (0, PAINT_HEIGHT / 2, -(PLAYING_LENGTH / 2 + END_ZONE_LENGTH / 2)),
            (FIELD_WIDTH, PAINT_HEIGHT, END_ZONE_LENGTH), end_zone_a)
    add_box(field_collection, "End Zone South", (0, PAINT_HEIGHT / 2, (PLAYING_LENGTH / 2 + END_ZONE_LENGTH / 2)),
            (FIELD_WIDTH, PAINT_HEIGHT, END_ZONE_LENGTH), end_zone_b)

    line_width = 0.18
    line_depth = 0.10
    # Outside border, with a subtle darker strip beyond it for a clean silhouette.
    border = 0.65
    add_box(field_collection, "Sideline Left", (-(FIELD_WIDTH / 2 + border / 2), 0.02, 0), (border, 0.06, FIELD_LENGTH), sideline)
    add_box(field_collection, "Sideline Right", ((FIELD_WIDTH / 2 + border / 2), 0.02, 0), (border, 0.06, FIELD_LENGTH), sideline)
    add_box(field_collection, "End Border North", (0, 0.02, -FIELD_LENGTH / 2 - border / 2), (FIELD_WIDTH + border * 2, 0.06, border), sideline)
    add_box(field_collection, "End Border South", (0, 0.02, FIELD_LENGTH / 2 + border / 2), (FIELD_WIDTH + border * 2, 0.06, border), sideline)

    # Boundary and goal lines sit above the colored surface.
    add_box(field_collection, "Boundary Left", (-FIELD_WIDTH / 2 + line_width / 2, PAINT_HEIGHT + 0.02, 0), (line_width, line_depth, FIELD_LENGTH - 1.0), paint)
    add_box(field_collection, "Boundary Right", (FIELD_WIDTH / 2 - line_width / 2, PAINT_HEIGHT + 0.02, 0), (line_width, line_depth, FIELD_LENGTH - 1.0), paint)
    for label, z in (("Goal Line North", -PLAYING_LENGTH / 2), ("Goal Line South", PLAYING_LENGTH / 2)):
        add_box(field_collection, label, (0, PAINT_HEIGHT + 0.025, z), (FIELD_WIDTH, line_depth, line_width * 1.5), paint)

    # Yard lines every ten yards, including a strong midfield stripe.
    for index, z in enumerate(range(-40, 41, 10)):
        name = "Midfield Line" if z == 0 else f"Yard Line {abs(z):02d}"
        add_box(field_collection, name, (0, PAINT_HEIGHT + 0.025, z), (FIELD_WIDTH - 0.5, line_depth, line_width), paint)

    # Compact hash marks: two short marks per yard line, aligned for gameplay readability.
    hash_length = 1.25
    for z in range(-40, 41, 10):
        for x in (-FIELD_WIDTH * 0.22, FIELD_WIDTH * 0.22):
            add_box(field_collection, f"Hash {z:+03d} {x:+.1f}", (x, PAINT_HEIGHT + 0.027, z), (hash_length, line_depth, line_width), paint)

    # End-zone emphasis is intentionally simple: one inner stripe at each goal line.
    for label, z in (("End Zone Stripe North", -PLAYING_LENGTH / 2 - END_ZONE_LENGTH + 0.8),
                     ("End Zone Stripe South", PLAYING_LENGTH / 2 + END_ZONE_LENGTH - 0.8)):
        add_box(field_collection, label, (0, PAINT_HEIGHT + 0.027, z), (FIELD_WIDTH - 1.1, line_depth, line_width), paint)

    # Stable metadata for importers and future tooling.
    field_collection["asset_type"] = "arcade_football_field"
    field_collection["dimensions_m"] = (FIELD_WIDTH + border * 2, SURFACE_THICKNESS + PAINT_HEIGHT, FIELD_LENGTH + border * 2)
    field_collection["gameplay_forward"] = "+Y"
    field_collection["up_axis"] = "+Z"
    field_collection["origin"] = "geometric center of field"

    log(f"created field geometry: {FIELD_WIDTH:.3f}m x {FIELD_LENGTH:.3f}m")

    # Camera and simple Eevee lighting for the repository preview.
    scene = bpy.context.scene
    # Blender 3.x uses BLENDER_EEVEE; newer versions renamed this to EEVEE_NEXT.
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("Field Preview World")
        scene.world.use_nodes = True
    scene.world.color = (0.035, 0.045, 0.055)
    world_nodes = scene.world.node_tree.nodes
    world_nodes["Background"].inputs["Color"].default_value = (0.035, 0.045, 0.055, 1.0)
    world_nodes["Background"].inputs["Strength"].default_value = 0.8
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "Medium High Contrast"
    scene.view_settings.exposure = 1.0

    bpy.ops.object.camera_add(location=(86, 112, 118))
    camera = bpy.context.object
    camera.name = "Preview Camera"
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 143
    aim_at(camera, (0, 0, 0))
    scene.camera = camera

    bpy.ops.object.light_add(type="AREA", location=(20, 45, 35))
    key = bpy.context.object
    key.name = "Preview Key Light"
    key.data.energy = 5200
    key.data.shape = "DISK"
    key.data.size = 70
    aim_at(key, (0, 0, 0))
    bpy.ops.object.light_add(type="AREA", location=(-45, 20, -40))
    fill = bpy.context.object
    fill.name = "Preview Fill Light"
    fill.data.energy = 2200
    fill.data.size = 55
    aim_at(fill, (0, 0, 0))
    bpy.ops.object.light_add(type="AREA", location=(0, 95, 0))
    top = bpy.context.object
    top.name = "Preview Top Light"
    top.data.energy = 4200
    top.data.size = 90
    aim_at(top, (0, 0, 0))

    # Select only the asset collection's meshes for convenient inspection/export.
    # The construction above is convenient in Blender's Y-up layout. Bake the
    # asset contract expected by the importer: +Z up and +Y forward. This is a
    # +90 degree X rotation, which also keeps the blue north end at +Y.
    asset_rotation = Matrix.Rotation(math.radians(90.0), 4, "X")
    for obj in field_collection.objects:
        if obj.type == "MESH":
            obj.matrix_world = asset_rotation @ obj.matrix_world

    bpy.ops.object.select_all(action="DESELECT")
    for obj in field_collection.objects:
        if obj.type == "MESH":
            obj.select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects.get("Turf Base")

    scene["field_width_m"] = FIELD_WIDTH
    scene["field_length_m"] = FIELD_LENGTH
    scene["orientation"] = "+Z up, +Y gameplay forward; importer maps to world -Z"
    scene["asset_notes"] = "Low-poly standalone tackle-alley football field; no stadium; baked asset rotation"
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    log(f"saved blend: {BLEND_PATH}")
    log("scene build complete")


if __name__ == "__main__":
    main()
