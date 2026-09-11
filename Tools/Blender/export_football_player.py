"""Export the canonical Stage 6 rig with the generated Jog action via Blender MCP.

Run after rig_player.py and create_player_jog_animation.py. Source blends stay intact.
"""
from pathlib import Path
import bpy
import json
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))

OUT = Path(__file__).resolve().parents[2] / 'Assets/Models'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'lowpoly_human_rigged.blend'))
with bpy.data.libraries.load(str(OUT / 'lowpoly_human_jog.blend'), link=False) as (source, target):
    assert 'Jog' in source.actions
    target.actions = ['Jog']
rig = bpy.data.objects['PlayerRig']
action = bpy.data.actions['Jog']
rig.animation_data_create()
rig.animation_data.action = action
for other in list(bpy.data.actions):
    if other != action:
        bpy.data.actions.remove(other)
scene = bpy.context.scene
scene.render.fps = 30
scene.render.fps_base = 1
scene.frame_start = 1
scene.frame_end = 29
scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
for obj in scene.objects:
    if obj.type in {'MESH', 'ARMATURE', 'EMPTY'}:
        obj.select_set(True)
expected_meshes = {o.name for o in scene.objects if o.type == 'MESH'}
path = OUT / 'football_player.glb'
bpy.ops.export_scene.gltf(
    filepath=str(path), export_format='GLB', use_selection=True,
    export_yup=False, export_animations=True, export_nla_strips=False,
    export_frame_range=True, export_force_sampling=True, export_skins=True,
    export_current_frame=False,
)
# Blender 3.3 merges rigid equipment channels under the name Animation.
raw = path.read_bytes()
size = struct.unpack_from('<I', raw, 12)[0]
doc = json.loads(raw[20:20 + size])
assert len(doc['animations']) == 1
doc['animations'][0]['name'] = 'Jog'
nodes = {n['name']: n for n in doc['nodes']}
assert expected_meshes <= {n['name'] for n in doc['nodes'] if 'mesh' in n}
assert {'Helmet', 'Facemask', 'Visor', 'ChinStrap', 'ShoulderPads'} <= nodes.keys()
from validate_helmet_skinning import validate_glb
validate_glb(path)
assert 'skin' in nodes['ShoulderPads']
assert len(doc['skins'][0]['joints']) == 24
payload = json.dumps(doc, separators=(',', ':')).encode()
payload += b' ' * (-len(payload) % 4)
tail = raw[20 + size:]
path.write_bytes(struct.pack('<4sII', b'glTF', 2, 20 + len(payload) + len(tail))
                 + struct.pack('<II', len(payload), 0x4e4f534a) + payload + tail)
print('CANONICAL_EXPORT_COMPLETE', len(expected_meshes), 'meshes, Jog, 24 joints')
