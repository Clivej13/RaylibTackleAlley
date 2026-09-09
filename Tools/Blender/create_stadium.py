"""Create a deterministic low-poly stadium environment for RaylibTackleAlley.

The stadium is a separate asset from the football field. It uses the same
authored asset axes: +Z is up and +Y is gameplay forward. The game importer
converts that contract to its Y-up, -Z-forward world space.
"""

import bpy
import math
from mathutils import Matrix, Vector
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = PROJECT_ROOT / "Assets" / "Models"
BLEND_PATH = OUTPUT_DIR / "stadium.blend"

FIELD_WIDTH = 53.333
FIELD_LENGTH = 120.0
STADIUM_WIDTH = 104.0
STADIUM_LENGTH = 176.0


def log(message):
    print(f"[stadium] {message}", flush=True)


def make_material(name, color, roughness=0.85, emission=None):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    if emission:
        bsdf.inputs["Emission"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 1.8
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
        bevel_mod = obj.modifiers.new("Low-poly softened edges", "BEVEL")
        bevel_mod.width = bevel
        bevel_mod.segments = 1
        obj.data.use_auto_smooth = True
        obj.modifiers.new("Weighted normals", "WEIGHTED_NORMAL")
    for old_collection in list(obj.users_collection):
        old_collection.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def aim_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def main():
    log("starting deterministic stadium build")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)

    stadium = bpy.data.collections.new("Stadium")
    bpy.context.scene.collection.children.link(stadium)

    concrete = make_material("Concrete Grey", (0.24, 0.27, 0.29))
    concrete_light = make_material("Upper Concrete", (0.36, 0.39, 0.40))
    structure = make_material("Dark Structure", (0.055, 0.07, 0.08))
    seat_blue = make_material("Muted Blue Seating", (0.08, 0.22, 0.34))
    seat_red = make_material("Muted Red Seating", (0.38, 0.10, 0.09))
    seat_gold = make_material("Muted Gold Seating", (0.64, 0.34, 0.06))
    accent = make_material("Scoreboard Accent", (0.05, 0.55, 0.70), emission=(0.04, 0.24, 0.32))
    light_material = make_material("Floodlight White", (0.85, 0.92, 1.0), emission=(0.7, 0.85, 1.0))

    # Low, continuous concourse/platform around the field footprint.
    add_box(stadium, "North Concourse", (0, 0.45, -78.0), (STADIUM_WIDTH, 0.9, 12.0), concrete, 0.5)
    add_box(stadium, "South Concourse", (0, 0.45, 78.0), (STADIUM_WIDTH, 0.9, 12.0), concrete, 0.5)
    add_box(stadium, "West Concourse", (-46.0, 0.45, 0), (12.0, 0.9, 145.0), concrete, 0.5)
    add_box(stadium, "East Concourse", (46.0, 0.45, 0), (12.0, 0.9, 145.0), concrete, 0.5)

    # Side stands: large stepped bands, intentionally not individual seats.
    side_levels = 5
    for side, sign in (("West", -1), ("East", 1)):
        for level in range(side_levels):
            x = sign * (31.5 + level * 3.0)
            y = 1.4 + level * 1.45
            material = (seat_blue, seat_red, seat_gold)[level % 3]
            add_box(stadium, f"{side} Stand Tier {level + 1}", (x, y, 0), (2.8, 1.25, 137.0), material, 0.18)
            # Rear riser gives the side stand a solid arcade silhouette.
            if level == side_levels - 1:
                add_box(stadium, f"{side} Stand Rear Wall", (sign * 48.0, 5.3, 0), (1.2, 8.0, 143.0), concrete, 0.3)
        # Regular structural piers keep the stands visually modular.
        for z in (-60.0, -30.0, 0.0, 30.0, 60.0):
            add_box(stadium, f"{side} Support {z:+.0f}", (sign * 29.6, 3.0, z), (1.0, 6.0, 1.0), structure, 0.1)

    # End stands use shorter stepped blocks so the field remains visually open.
    for end, sign in (("North", -1), ("South", 1)):
        for level in range(3):
            z = sign * (67.0 + level * 3.0)
            y = 1.4 + level * 1.45
            material = (seat_red, seat_gold, seat_blue)[level]
            add_box(stadium, f"{end} Stand Tier {level + 1}", (0, y, z), (72.0, 1.25, 2.8), material, 0.18)
        add_box(stadium, f"{end} Stand Rear Wall", (0, 4.8, sign * 78.0), (76.0, 7.0, 1.2), concrete, 0.3)

    # Simple perimeter barriers leave the field unobstructed but frame the action.
    barrier_mat = structure
    add_box(stadium, "Barrier West", (-28.8, 1.15, 0), (0.45, 1.8, 128.0), barrier_mat, 0.08)
    add_box(stadium, "Barrier East", (28.8, 1.15, 0), (0.45, 1.8, 128.0), barrier_mat, 0.08)
    add_box(stadium, "Barrier North", (0, 1.15, -64.0), (58.0, 1.8, 0.45), barrier_mat, 0.08)
    add_box(stadium, "Barrier South", (0, 1.15, 64.0), (58.0, 1.8, 0.45), barrier_mat, 0.08)

    # Dark tunnel/entry openings, represented as bold inset shapes at each end.
    add_box(stadium, "North Tunnel Housing", (0, 3.1, -78.5), (18.0, 5.0, 2.2), structure, 0.25)
    add_box(stadium, "North Tunnel Opening", (0, 2.7, -77.25), (8.0, 3.8, 0.18), structure, 0.08)
    add_box(stadium, "South Tunnel Housing", (0, 3.1, 78.5), (18.0, 5.0, 2.2), structure, 0.25)
    add_box(stadium, "South Tunnel Opening", (0, 2.7, 77.25), (8.0, 3.8, 0.18), structure, 0.08)

    # Scoreboard beyond the north end: simple readable slab and two supports.
    add_box(stadium, "Scoreboard Panel", (0, 17.0, -84.0), (24.0, 8.0, 1.5), structure, 0.3)
    add_box(stadium, "Scoreboard Face", (0, 17.0, -83.15), (20.0, 5.2, 0.18), accent, 0.12)
    add_box(stadium, "Scoreboard Support Left", (-8.0, 10.0, -84.0), (1.2, 14.0, 1.2), structure, 0.1)
    add_box(stadium, "Scoreboard Support Right", (8.0, 10.0, -84.0), (1.2, 14.0, 1.2), structure, 0.1)

    # Four simple light towers outside the corners, with small bright light heads.
    for index, (x, z) in enumerate(((-49.0, -78.0), (49.0, -78.0), (-49.0, 78.0), (49.0, 78.0)), 1):
        add_box(stadium, f"Light Tower {index} Mast", (x, 12.0, z), (1.3, 24.0, 1.3), structure, 0.1)
        add_box(stadium, f"Light Tower {index} Head", (x, 24.5, z), (5.0, 1.0, 3.0), light_material, 0.15)

    stadium["asset_type"] = "low_poly_modular_stadium"
    stadium["dimensions_m"] = (STADIUM_WIDTH, 31.0, STADIUM_LENGTH)
    stadium["field_clearance_m"] = (STADIUM_WIDTH - FIELD_WIDTH) / 2
    # Bake the same authored asset contract as the field: +Z up and +Y
    # gameplay forward. The game applies the inverse axis conversion on import.
    asset_rotation = Matrix.Rotation(math.radians(90.0), 4, "X")
    for obj in stadium.objects:
        if obj.type == "MESH":
            obj.matrix_world = asset_rotation @ obj.matrix_world

    stadium["gameplay_forward"] = "+Y"
    stadium["up_axis"] = "+Z"
    stadium["origin"] = "centered to football field origin"
    log(f"created stadium shell: {STADIUM_WIDTH:.1f}m x {STADIUM_LENGTH:.1f}m")
    log(f"field clearance each side: {(STADIUM_WIDTH - FIELD_WIDTH) / 2:.2f}m")

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("Stadium Preview World")
        scene.world.use_nodes = True
    scene.world.color = (0.035, 0.045, 0.055)
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.035, 0.045, 0.055, 1.0)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.75
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "Medium High Contrast"
    scene.view_settings.exposure = 0.7

    bpy.ops.object.camera_add(location=(125.0, 108.0, 145.0))
    camera = bpy.context.object
    camera.name = "Preview Camera"
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 195.0
    aim_at(camera, (0, 3.0, 0))
    scene.camera = camera

    bpy.ops.object.light_add(type="AREA", location=(0, 110, 0))
    key = bpy.context.object
    key.name = "Preview Stadium Key"
    key.data.energy = 9500
    key.data.size = 105
    aim_at(key, (0, 0, 0))
    bpy.ops.object.light_add(type="AREA", location=(-80, 55, 70))
    fill = bpy.context.object
    fill.name = "Preview Stadium Fill"
    fill.data.energy = 4200
    fill.data.size = 70
    aim_at(fill, (0, 3, 0))

    # Only stadium meshes are selected so export_glb can omit preview helpers.
    bpy.ops.object.select_all(action="DESELECT")
    for obj in stadium.objects:
        if obj.type == "MESH":
            obj.select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects.get("North Concourse")
    scene["stadium_width_m"] = STADIUM_WIDTH
    scene["stadium_length_m"] = STADIUM_LENGTH
    scene["orientation"] = "+Z up, +Y gameplay forward; importer maps to world -Z"
    scene["asset_notes"] = "Standalone surrounding stadium; field remains a separate asset; baked asset rotation"
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    log(f"saved blend: {BLEND_PATH}")
    log("stadium build complete")


if __name__ == "__main__":
    main()
