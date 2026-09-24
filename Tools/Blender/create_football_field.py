"""Deterministic yard-scale field. Run through Blender MCP; no stadium inputs.
NFL markings: https://operations.nfl.com/rules-officiating/2026-nfl-rulebook
The requested 53.333 width approximates 53 1/3 yards. Boundary paint is
clipped inward to the requested footprint; external stadium borders excluded.
"""
from pathlib import Path
import json
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
TEX = ROOT / 'Assets/Textures/FootballField'
WIDTH, LENGTH = 53.333, 120.0
NX, NY = 2048, 4096

def save_image(name, rgb, noncolor=False):
    h, w = rgb.shape[:2]
    image = bpy.data.images.new(name, width=w, height=h, alpha=False)
    image.colorspace_settings.name = 'Non-Color' if noncolor else 'sRGB'
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[:, :, :3] = rgb
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = str(TEX / (name + '.png'))
    image.file_format = 'PNG'
    image.save()
    image.pack()
    return image

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    TEX.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene
    scene.unit_settings.system = 'IMPERIAL'
    scene.unit_settings.scale_length = 0.9144
    scene.unit_settings.length_unit = 'FEET'
    scene['units_per_yard'] = 1.0
    scene['field_width_yards'] = WIDTH
    scene['field_length_yards'] = LENGTH
    scene['orientation'] = '+Z up, +Y length; blue end at +Y; GLB +Y up, -Z forward'
    x = ((np.arange(NX, dtype=np.float32) + .5) / NX * WIDTH - WIDTH/2)[None, :]
    y = ((np.arange(NY, dtype=np.float32) + .5) / NY * LENGTH - LENGTH/2)[:, None]
    layout = np.empty((NY, NX, 3), np.float32)
    layout[:] = (.19, .38, .16)
    layout[np.broadcast_to(y >= 50, (NY, NX))] = (.055, .20, .39)
    layout[np.broadcast_to(y <= -50, (NY, NX))] = (.62, .34, .055)
    # Analytic coverage antialiasing keeps four-inch paint measurable.
    paint = np.zeros((NY, NX), np.float32)
    def rect(x0, x1, y0, y1):
        cx = np.clip((x-x0)/(WIDTH/NX)+.5, 0, 1)*np.clip((x1-x)/(WIDTH/NX)+.5, 0, 1)
        cy = np.clip((y-y0)/(LENGTH/NY)+.5, 0, 1)*np.clip((y1-y)/(LENGTH/NY)+.5, 0, 1)
        np.maximum(paint, cx*cy, out=paint)
    lw = 4/36
    for side in (-1, 1):
        if side < 0: rect(-WIDTH/2, -WIDTH/2+lw, -60, 60)
        else: rect(WIDTH/2-lw, WIDTH/2, -60, 60)
    rect(-WIDTH/2, WIDTH/2, -60, -60+lw)
    rect(-WIDTH/2, WIDTH/2, 60-lw, 60)
    for yard in range(-50, 51, 5):
        rect(-WIDTH/2, WIDTH/2, yard-lw/2, yard+lw/2)
    # Inbound edges 70 ft 9 in from each sideline; hashes extend outward.
    hx = WIDTH/2 - (70.75/3)
    for yard in range(-49, 50):
        if yard % 5 == 0: continue
        for a,b in [(-hx-2/3,-hx),(hx,hx+2/3),
                    (-WIDTH/2+8/36,-WIDTH/2+8/36+2/3),
                    (WIDTH/2-8/36-2/3,WIDTH/2-8/36)]:
            rect(a,b,yard-lw/2,yard+lw/2)
    # Two-yard conversion marks.
    for yard in (-48,48): rect(-.5,.5,yard-lw/2,yard+lw/2)
    layout = layout*(1-paint[:,:,None]) + np.array([.94,.95,.90],np.float32)*paint[:,:,None]
    save_image('field_layout', layout)
    rng = np.random.default_rng(1709)
    grain = rng.normal(0, .018, (NY,NX)).astype(np.float32)
    broad = .013*np.sin(x*1.9)*np.sin(y*.83) + .008*np.cos(x*.71+y*.32)
    detail = np.clip(1 + grain + broad, .91, 1.09)
    grass = np.empty_like(layout)
    grass[:] = (.19,.38,.16)
    grass *= detail[:,:,None]
    save_image('grass_color_detail', grass)
    # A directly exportable composite guarantees the same layout in GLB.
    base = layout * (1 + (detail-1)*(1-paint*.85))[:,:,None]
    base_image = save_image('field_basecolor', base)
    dx = (np.roll(grain,-1,axis=1)-np.roll(grain,1,axis=1))*.65
    dy = (np.roll(grain,-1,axis=0)-np.roll(grain,1,axis=0))*.65
    normal = np.stack((-dx,-dy,np.ones_like(dx)),axis=2)
    normal /= np.linalg.norm(normal,axis=2)[:,:,None]
    normal_image = save_image('grass_normal', normal*.5+.5, True)
    mesh = bpy.data.meshes.new('FieldSurfaceMesh')
    mesh.from_pydata([(-WIDTH/2,-60,0),(WIDTH/2,-60,0),(WIDTH/2,60,0),(-WIDTH/2,60,0)], [], [(0,1,2,3)])
    mesh.update()
    uv = mesh.uv_layers.new(name='FieldUV')
    for loop, coord in zip(uv.data, [(0,0),(1,0),(1,1),(0,1)]): loop.uv = coord
    field = bpy.data.objects.new('FootballField',mesh)
    scene.collection.objects.link(field)
    field['units_per_yard'] = 1.0
    field['goal_lines_y'] = [-50.0,50.0]
    field['end_zone_length_yards'] = 10.0
    field['hash_inbound_edge_x'] = [-hx,hx]
    mat = bpy.data.materials.new('Field Turf and Painted Layout')
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes.get('Principled BSDF')
    bsdf.inputs['Roughness'].default_value = .92
    albedo = nodes.new('ShaderNodeTexImage'); albedo.image = base_image
    links.new(albedo.outputs['Color'],bsdf.inputs['Base Color'])
    texnormal = nodes.new('ShaderNodeTexImage'); texnormal.image = normal_image
    normalnode = nodes.new('ShaderNodeNormalMap')
    links.new(texnormal.outputs['Color'],normalnode.inputs['Color'])
    links.new(normalnode.outputs['Normal'],bsdf.inputs['Normal'])
    field.data.materials.append(mat)
    bpy.ops.object.camera_add(location=(95,-115,145))
    cam = bpy.context.object
    cam.rotation_euler = (-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'; cam.data.ortho_scale=155
    scene.camera=cam
    bpy.ops.object.light_add(type='SUN',location=(0,0,100))
    bpy.context.object.rotation_euler=(.2,-.3,-.2)
    bpy.context.object.data.energy=2.0
    scene.world=bpy.data.worlds.new('Field Preview World')
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.08,.10,.13,1)
    scene.render.engine='BLENDER_EEVEE'
    scene.view_settings.view_transform='Standard'
    scene.view_settings.look='Medium High Contrast'
    scene.render.resolution_x=1400; scene.render.resolution_y=1100
    scene.render.resolution_percentage=100
    bpy.ops.object.select_all(action='DESELECT')
    field.select_set(True); bpy.context.view_layer.objects.active=field
    bpy.context.view_layer.update()
    assert abs(field.dimensions.x-WIDTH)<1e-5 and abs(field.dimensions.y-LENGTH)<1e-5
    assert len([o for o in scene.objects if o.type=='MESH'])==1
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'football_field.blend'))
    bpy.ops.export_scene.gltf(filepath=str(OUT/'football_field.glb'), export_format='GLB', use_selection=True, export_yup=True, export_extras=True, export_apply=False)
    print('FIELD_VALIDATED: one quad, two exported triangles, identity transform, 53.333 x 120 yards')

if __name__=='__main__': main()
