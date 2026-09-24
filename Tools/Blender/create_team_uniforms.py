"""Run via Blender MCP. Create exact-layout team PNGs and model previews."""
from pathlib import Path
import hashlib
import json
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Textures/Uniforms'
PRE = OUT / 'TeamPreviews'
PRE.mkdir(parents=True, exist_ok=True)
N = 2048
source = ROOT / 'Assets/Models/football_player.glb'
source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
regions = json.loads((ROOT / 'Assets/Models/UniformAtlas/atlas_regions.json').read_text())
guides = json.loads((ROOT / 'Assets/Models/UniformAtlas/artwork_guides.json').read_text())
NAVY, WHITE, GOLD, RED = '#102344', '#f5f5f5', '#d9ac45', '#cc2638'

def rgba(hex_color):
    return [int(hex_color[i:i+2], 16)/255 for i in (1, 3, 5)] + [1]

def rectangle(pixels, box, color):
    x0, y0, x1, y1 = [round(v) for v in box]
    pixels[max(0,y0):min(N,y1), max(0,x0):min(N,x1)] = rgba(color)

def atlas(team, jersey, numbers, stripe, pants, helmet):
    # Pixels use Blender's bottom-left origin, matching atlas_regions.json.
    pixels = np.ones((N,N,4), dtype=np.float32)
    pixels[:] = rgba(jersey)
    for name, region in regions.items():
        color = jersey
        if name.startswith('Trousers'): color = pants
        elif name.startswith('Helmet'): color = helmet
        elif name.startswith('Sock'): color = NAVY if team == 'offense' else WHITE
        elif name == 'Undershirt': color = NAVY
        rectangle(pixels, [v*N for v in region], color)
    # A continuous cuff band uses the authored guide for every sleeve island.
    for guide in guides:
        if not guide['region'].startswith('Sleeve'): continue
        lo, hi = guide['span_px']
        center, half = guide['center_px'], guide['width_px']/2
        rectangle(pixels, (lo, center-half, hi, center+half), stripe)
    # Original block lettering, upright on both existing projected jersey panels.
    one = ['00110','01110','00110','00110','00110','00110','01111']
    zero = ['01110','11011','11011','11011','11011','11011','01110']
    cell = 46
    for name in ('Chest', 'Back'):
        x0,y0,x1,y1 = [v*N for v in regions[name]]
        left = (x0+x1)/2 - 11*cell/2
        top = (y0+y1)/2 + 7*cell/2
        for digit, pattern in enumerate((one, zero)):
            for row, line in enumerate(pattern):
                for col, bit in enumerate(line):
                    if bit == '1':
                        x = left + (digit*6+col)*cell
                        rectangle(pixels, (x,top-(row+1)*cell,x+cell,top-row*cell), numbers)
    image = bpy.data.images.new('Uniform_'+team, width=N, height=N, alpha=True)
    image.colorspace_settings.name = 'sRGB'
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw = str(OUT / (team+'_uniform.png'))
    image.file_format = 'PNG'
    image.save()
    return image

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))

def mesh_signature():
    h = hashlib.sha256()
    for ob in sorted(bpy.data.objects, key=lambda ob: ob.name):
        if ob.type != 'MESH': continue
        h.update(ob.name.encode())
        h.update(np.array([v.co[:] for v in ob.data.vertices]).tobytes())
        h.update(np.array([l.vertex_index for l in ob.data.loops]).tobytes())
        for uv in ob.data.uv_layers:
            h.update(np.array([v.uv[:] for v in uv.data]).tobytes())
    return h.hexdigest()

before = mesh_signature()
for ob in bpy.data.objects:
    if ob.type == 'ARMATURE': ob.data.pose_position = 'REST'
mat = bpy.data.materials['Uniform']
node = next(n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE')
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.eevee.taa_render_samples = 64
scene.world = bpy.data.worlds.new('TeamPreviewWorld')
scene.world.color = (.18,.18,.18)
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'Medium High Contrast'
scene.render.resolution_x = 900
scene.render.resolution_y = 1100
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
for loc in [(3,4,5),(-3,1,3),(0,-4,4)]:
    bpy.ops.object.light_add(type='AREA', location=loc)
    light = bpy.context.object
    light.data.energy = 400
    light.data.size = 5
    light.rotation_euler = (Vector((0,0,1))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add()
camera = bpy.context.object
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 2.18
scene.camera = camera
bpy.context.preferences.filepaths.save_version = 0
for team, jersey, numbers, stripe, pants, helmet in [
    ('offense',NAVY,WHITE,GOLD,WHITE,NAVY),
    ('defense',WHITE,NAVY,RED,NAVY,WHITE),
]:
    node.image = atlas(team,jersey,numbers,stripe,pants,helmet)
    for view, loc in [('front',(2.3,5,1.8)),('back',(-2.3,-5,1.8))]:
        camera.location = loc
        camera.rotation_euler = (Vector((0,0,.98))-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath = str(PRE / (team+'_'+view+'.png'))
        bpy.ops.render.render(write_still=True)
    camera.location = (2.3,5,1.8)
    camera.rotation_euler = (Vector((0,0,.98))-camera.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.wm.save_as_mainfile(filepath=str(PRE / (team+'_preview.blend')))
    node.image.filepath = '//../'+team+'_uniform.png'
    bpy.ops.wm.save_as_mainfile(filepath=str(PRE / (team+'_preview.blend')))
assert before == mesh_signature(), 'Model or UVs changed'
assert source_hash == hashlib.sha256(source.read_bytes()).hexdigest()
(PRE/'validation.json').write_text(json.dumps({
    'source':str(source.relative_to(ROOT)), 'source_sha256':source_hash,
    'source_unchanged':True, 'mesh_and_uvs_unchanged':True,
    'atlas_size':[N,N], 'number':'10',
    'offense':dict(jersey=NAVY,numbers=WHITE,sleeve_stripes=GOLD,pants=WHITE),
    'defense':dict(jersey=WHITE,numbers=NAVY,sleeve_stripes=RED,pants=NAVY),
},indent=2))
print('Team atlases and four previews complete; source model and UVs unchanged.')
