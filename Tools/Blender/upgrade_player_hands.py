"""Upgrade saved approved animations in place, without reconstructing locomotion.

Run create_football.py first; this pass is idempotent. Existing rig and animation
generators also call player_hand_rig for complete regeneration from Stage 6.
"""
from pathlib import Path
import json
import struct
import hashlib
import sys
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
PRE = OUT / 'HandRigPreviews'
sys.path.insert(0, str(Path(__file__).resolve().parent))
from player_hand_rig import install, key_hand_poses, fit_carry, attach_football, BALL
from create_football import look
from validate_player_hands import FILES


def export(scene, rig, action, path):
    for other in list(bpy.data.actions):
        if other != action:
            bpy.data.actions.remove(other)
    rig.animation_data.action = action
    scene.frame_end = int(action.frame_range[1])
    scene.frame_set(1)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in scene.objects:
        if obj.type in {'MESH', 'ARMATURE', 'EMPTY'} and obj.name != BALL:
            obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', use_selection=True,
        export_yup=False, export_animations=True, export_nla_strips=False,
        export_frame_range=True, export_force_sampling=True, export_skins=True, export_current_frame=False)
    raw = path.read_bytes()
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + size])
    assert len(doc['animations']) == 1
    doc['animations'][0]['name'] = action.name
    assert not any(n.get('name') in (BALL, 'Football') for n in doc['nodes'])
    payload = json.dumps(doc, separators=(',', ':')).encode()
    payload += b' ' * (-len(payload) % 4)
    tail = raw[20 + size:]
    path.write_bytes(struct.pack('<4sII', b'glTF', 2, 20 + len(payload) + len(tail))
                     + struct.pack('<II', len(payload), 0x4e4f534a) + payload + tail)


def preview(scene, rig, action):
    scene.render.image_settings.file_format = 'PNG'
    scene.render.resolution_percentage = 100
    scene.render.resolution_x = 640
    scene.render.resolution_y = 720
    scene.eevee.taa_render_samples = 48
    start, end = map(int, action.frame_range)
    if action.name in ('Jog', 'Run', 'Sprint'):
        name = action.name
        folder = OUT / (name + 'Previews')
        frames = {'Jog': (1, 5, 10, 15, 19, 24), 'Run': (1, 5, 9, 13, 17, 21),
                  'Sprint': (1, 4, 7, 11, 14, 17)}[name]
        labels = ('LeftContact', 'LeftPassing', 'LeftPushOff', 'RightContact', 'RightPassing', 'RightPushOff')
        views = ('Front', 'Side', 'Side', 'Front', 'FrontThreeQuarter', 'FrontThreeQuarter')
        for frame, label, view in list(zip(frames, labels, views)) + [(1, 'Stride', 'FrontThreeQuarter')]:
            scene.frame_set(frame)
            scene.camera = bpy.data.objects[view]
            scene.render.filepath = str(folder / (name + '_' + label + '_' + view + '.png'))
            bpy.ops.render.render(write_still=True)
        scene.frame_end = end-1
        scene.render.image_settings.file_format = 'FFMPEG'
        scene.render.ffmpeg.format = 'MPEG4'
        scene.render.ffmpeg.codec = 'H264'
        scene.render.ffmpeg.constant_rate_factor = 'HIGH'
        scene.render.filepath = str(folder / (name + '_loop.mp4'))
        bpy.ops.render.render(animation=True)
        scene.frame_end = end
        metadata_path = folder / 'validation.json'
        metadata = json.loads(metadata_path.read_text())
        source = OUT / ('lowpoly_human_' + {'Jog': 'rigged', 'Run': 'jog', 'Sprint': 'run'}[name] + '.blend')
        metadata['source_sha256'] = hashlib.sha256(source.read_bytes()).hexdigest()
        metadata['hand_upgrade'] = '24 joints; fitted fingers/thumbs; original locomotion unchanged'
        metadata_path.write_text(json.dumps(metadata, indent=2))
    if action.name.startswith('Carry'):
        scene.camera = bpy.data.objects['CarryRightThreeQuarter']
        for frame in (1, 1 + (end-1)//4, 1 + (end-1)//2, 1 + 3*(end-1)//4):
            scene.frame_set(frame)
            scene.render.filepath = str(PRE / (action.name + '_' + str(frame).zfill(2) + '.png'))
            bpy.ops.render.render(write_still=True)
        scene.frame_end = end-1
        scene.render.image_settings.file_format = 'FFMPEG'
        scene.render.ffmpeg.format = 'MPEG4'
        scene.render.ffmpeg.codec = 'H264'
        scene.render.ffmpeg.constant_rate_factor = 'HIGH'
        scene.render.filepath = str(PRE / (action.name + '_loop.mp4'))
        bpy.ops.render.render(animation=True)
        scene.frame_end = end
    if action.name in ('Jog', 'CarryJog'):
        scene.frame_set(1)
        camera = scene.camera.copy()
        camera.data = camera.data.copy()
        camera.name = 'HandDetailCamera'
        scene.collection.objects.link(camera)
        scene.camera = camera
        camera.data.type = 'ORTHO'
        camera.data.ortho_scale = .36 if action.name == 'CarryJog' else .24
        scene.render.resolution_x = 800
        scene.render.resolution_y = 800
        scene.render.image_settings.file_format = 'PNG'
        for side in (('R',) if action.name.startswith('Carry') else ('R', 'L')):
            hand = rig.pose.bones['Hand.' + side].matrix
            target = hand @ Vector((0, .075, 0))
            camera.location = hand @ Vector((-.40 if side == 'R' else .40, .12, .20))
            if action.name.startswith('Carry'):
                target = bpy.data.objects[BALL].matrix_world.translation
                camera.location = target + Vector((-.32, .28, -.4))
            look(camera, target)
            scene.render.filepath = str(PRE / (('grip' if action.name.startswith('Carry') else 'relaxed') + '_' + side + '.png'))
            bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(camera, do_unlink=True)


def main():
    assert (OUT / 'football.glb').exists()
    PRE.mkdir(exist_ok=True)
    for filename in FILES:
        bpy.ops.wm.open_mainfile(filepath=str(OUT / filename))
        bpy.context.preferences.filepaths.save_version = 0
        rig = bpy.data.objects['PlayerRig']
        scene = bpy.context.scene
        action = rig.animation_data.action
        install(rig)
        key_hand_poses(rig)
        if action.name.startswith('Carry'):
            fit_carry(rig, action)
            attach_football(rig)
            scene['carry_preview_ball'] = 'Exact production football.blend geometry; Hand.R preview attachment; excluded from player GLB'
        scene.frame_set(1)
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT / filename))
        preview(scene, rig, action)
        if action.name in ('Jog', 'Run', 'Sprint'):
            path = OUT / ('lowpoly_human_' + action.name.lower() + '_validation.glb')
        elif action.name.startswith('Carry'):
            path = OUT / (Path(filename).stem + '.glb')
        else:
            continue
        export(scene, rig, action, path)
        print('HAND_UPGRADE_COMPLETE', filename)


if __name__ == '__main__':
    main()
