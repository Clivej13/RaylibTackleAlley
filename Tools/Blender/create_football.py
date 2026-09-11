"""Build the standalone football first. Execute with Blender MCP."""
from pathlib import Path
import math
import json
import bpy
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
PRE = OUT / 'HandRigPreviews'


def look(camera, target):
    forward = (Vector(target) - camera.location).normalized()
    right = forward.cross(Vector((0, 1, 0))).normalized()
    camera.rotation_euler = Matrix((right, right.cross(forward), -forward)).transposed().to_euler()


def main():
    PRE.mkdir(exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    vertices, faces, slots = [], [], []

    def face(ids, slot):
        faces.append(ids)
        slots.append(slot)

    # Superelliptic prolate profile, soft tips and modest flat facets.
    def radius(z):
        return .082 * max(0, 1 - (z / .14) ** 2) ** .68

    rings, sectors = 20, 24
    for j in range(1, rings):
        theta = math.pi * j / rings
        z = .14 * math.cos(theta)
        r = radius(z)
        for i in range(sectors):
            angle = 2 * math.pi * i / sectors
            vertices.append((r * math.cos(angle), r * math.sin(angle), z))
    for j in range(rings - 2):
        for i in range(sectors):
            a = j * sectors + i
            b = j * sectors + (i + 1) % sectors
            face((a, a + sectors, b + sectors, b), 0)
    top, bottom = len(vertices), len(vertices) + 1
    vertices.extend([(0, 0, .14), (0, 0, -.14)])
    for i in range(sectors):
        face((top, i, (i + 1) % sectors), 0)
        a = (rings - 2) * sectors
        face((bottom, a + (i + 1) % sectors, a + i), 0)

    def tube(points, width, material):
        start = len(vertices)
        for i, p in enumerate(points):
            p = Vector(p)
            direction = Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(0, i - 1)])
            u = direction.normalized().cross(Vector((0, 1, 0))).normalized()
            if u.length < .1:
                u = Vector((1, 0, 0))
            v = direction.normalized().cross(u)
            for k in range(6):
                vertices.append(tuple(p + width * (math.cos(k * math.pi / 3) * u + math.sin(k * math.pi / 3) * v)))
        for i in range(len(points) - 1):
            for k in range(6):
                a = start + i * 6 + k
                b = start + i * 6 + (k + 1) % 6
                face((a, b, b + 6, a + 6), material)
        face(tuple(start + k for k in reversed(range(6))), material)
        face(tuple(start + (len(points) - 1) * 6 + k for k in range(6)), material)

    for angle in (0, math.pi / 2, math.pi, 3 * math.pi / 2):
        points = []
        for j in range(1, rings):
            z = .14 * math.cos(math.pi * j / rings)
            r = radius(z) + .0004
            points.append((r * math.cos(angle), r * math.sin(angle), z))
        tube(points, .0009, 1)
    for x in (-.012, .012):
        tube([(x, math.sqrt(radius(z)**2 - x*x) + .0015, z)
              for z in [-.049 + i * .098 / 12 for i in range(13)]], .0015, 2)
    for i in range(8):
        z = -.043 + i * .086 / 7
        tube([(x, math.sqrt(radius(z)**2 - x*x) + .0025, z)
              for x in (-.016, -.008, 0, .008, .016)], .0019, 2)
    mesh = bpy.data.meshes.new('FootballMesh')
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    ball = bpy.data.objects.new('Football', mesh)
    bpy.context.collection.objects.link(ball)
    for name, color in [('FootballLeather', (.23, .063, .023, 1)),
                        ('FootballSeams', (.045, .018, .009, 1)),
                        ('FootballLaces', (.83, .81, .71, 1))]:
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = color
        mat.use_nodes = True
        mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = color
        mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .85
        mesh.materials.append(mat)
    for polygon, slot in zip(mesh.polygons, slots):
        polygon.material_index = slot
    # Recalculate consistent outward normals, including seam/lace tubes.
    bpy.context.view_layer.objects.active = ball
    ball.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    ball['axes'] = '+Z long axis; +Y up/laces; +X lateral; metres'
    ball['origin'] = 'Geometric centre of leather shell'
    ball['nominal_dimensions_m'] = [.164, .164, .28]
    bpy.ops.export_scene.gltf(filepath=str(OUT / 'football.glb'), export_format='GLB',
                             use_selection=True, export_yup=False, export_animations=False)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.eevee.taa_render_samples = 64
    scene.render.resolution_x = 960
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('FootballStudio')
    scene.world.color = (.09, .09, .09)
    camera_data = bpy.data.cameras.new('FootballCamera')
    camera = bpy.data.objects.new('FootballCamera', camera_data)
    scene.collection.objects.link(camera)
    camera.location = (.36, .30, .32)
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = .43
    look(camera, (0, 0, 0))
    scene.camera = camera
    for name, position, power, size in [('Key', (.3, .5, .1), 30, .4), ('Fill', (-.4, .2, -.3), 15, .35)]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy = power
        data.size = size
        obj = bpy.data.objects.new(name, data)
        scene.collection.objects.link(obj)
        obj.location = position
        look(obj, (0, 0, 0))
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.exposure = -1.5
    scene.render.filepath = str(PRE / 'football.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'football.blend'))
    bpy.ops.render.render(write_still=True)
    mesh.calc_loop_triangles()
    print('FOOTBALL_COMPLETE', json.dumps({'vertices': len(mesh.vertices), 'triangles': len(mesh.loop_triangles),
                                           'dimensions_m': list(ball.dimensions)}))


if __name__ == '__main__':
    main()
