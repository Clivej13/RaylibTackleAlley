"""Copy approved locomotion and key a right-arm tuck. Run through Blender MCP.

The production football is referenced only in preview blends, never exported with the player.
"""
from pathlib import Path
import hashlib
import json
import math
import struct
import sys
import bpy
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
PRE = OUT / 'CarryPreviews'
MODIFIED = ('UpperArm.R', 'LowerArm.R', 'Hand.R')
PROXY = 'FootballPreview'
sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_helmet_skinning import validate_glb
from player_hand_rig import attach_football, fit_carry


def aim(rig, name, direction, chest_transform):
    bone = rig.data.bones[name]
    pose = rig.pose.bones[name]
    turn = (bone.tail_local - bone.head_local).rotation_difference(Vector(direction))
    desired = chest_transform.to_3x3() @ turn.to_matrix() @ bone.matrix_local.to_3x3()
    matrix = desired.to_4x4()
    matrix.translation = pose.head
    pose.matrix = matrix
    bpy.context.view_layer.update()


def preview_ball(rig):
    return attach_football(rig)


def export_clip(scene, rig, action, name, end):
    for other in list(bpy.data.actions):
        if other != action:
            bpy.data.actions.remove(other)
    rig.animation_data.action = action
    scene.frame_end = end
    bpy.ops.object.select_all(action='DESELECT')
    for obj in scene.objects:
        if obj.type in {'MESH', 'ARMATURE', 'EMPTY'} and obj.name != PROXY:
            obj.select_set(True)
    path = OUT / ('football_player_carry_' + name.lower() + '.glb')
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format='GLB', use_selection=True,
        export_yup=False, export_animations=True, export_nla_strips=False,
        export_frame_range=True, export_force_sampling=True, export_skins=True,
        export_current_frame=False)
    raw = path.read_bytes()
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + size])
    assert len(doc['animations']) == 1
    doc['animations'][0]['name'] = 'Carry' + name
    assert not any(n.get('name') == PROXY for n in doc['nodes'])
    payload = json.dumps(doc, separators=(',', ':')).encode()
    payload += b' ' * (-len(payload) % 4)
    tail = raw[20 + size:]
    path.write_bytes(struct.pack('<4sII', b'glTF', 2, 20 + len(payload) + len(tail))
                     + struct.pack('<II', len(payload), 0x4e4f534a) + payload + tail)
    validate_glb(path)


def main():
    PRE.mkdir(exist_ok=True)
    for name, impact in [('Jog', .6), ('Run', 1.0), ('Sprint', 1.4)]:
        source = OUT / ('lowpoly_human_' + name.lower() + '.blend')
        source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
        bpy.ops.wm.open_mainfile(filepath=str(source))
        bpy.context.preferences.filepaths.save_version = 0
        scene = bpy.context.scene
        rig = bpy.data.objects['PlayerRig']
        original = bpy.data.actions[name]
        action = original.copy()
        action.name = 'Carry' + name
        action.use_fake_user = True
        action['description'] = 'Approved ' + name + ' with inside right-arm tuck, based on Assets/References/carrying.png'
        rig.animation_data.action = action
        start, end = map(int, original.frame_range)
        for frame in range(start, end + 1):
            scene.frame_set(frame)
            phase = 2 * math.pi * (frame - start) / (end - start)
            chest = rig.pose.bones['Chest'].matrix @ rig.data.bones['Chest'].matrix_local.inverted()
            # Small impact response keeps the tucked arm alive without a running swing.
            bob = impact * math.sin(2 * phase) * .003
            aim(rig, 'UpperArm.R', (-.079, -.265, .003 + bob), chest)
            aim(rig, 'LowerArm.R', (.02, .115 + bob, -.237), chest)
            aim(rig, 'Hand.R', (.035, .055, .060), chest)
            for bone in MODIFIED:
                rig.pose.bones[bone].keyframe_insert('rotation_quaternion', frame=frame, group=bone)
        for fc in action.fcurves:
            if fc.data_path in {'pose.bones["' + n + '"].rotation_quaternion' for n in MODIFIED}:
                for key in fc.keyframe_points:
                    key.interpolation = 'LINEAR'
        fit_carry(rig, action)
        scene.frame_set(1)
        preview_ball(rig)
        # Show the carrying side; original cameras remain available for comparison.
        camera = bpy.data.objects['FrontThreeQuarter'].copy()
        camera.data = camera.data.copy()
        camera.data.ortho_scale = 2.3
        camera.name = 'CarryRightThreeQuarter'
        scene.collection.objects.link(camera)
        camera.location = (-3.2, 2.15, -4.3)
        forward = (Vector((0, 1.0, 0)) - camera.location).normalized()
        right = forward.cross(Vector((0, 1, 0))).normalized()
        up = right.cross(forward)
        camera.rotation_euler = Matrix((right, up, -forward)).transposed().to_euler()
        scene.camera = camera
        scene.render.engine = 'BLENDER_EEVEE'
        scene.eevee.taa_render_samples = 48
        scene.render.resolution_x = 640
        scene.render.resolution_y = 720
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = 'PNG'
        scene['carry_source_sha256'] = source_hash
        scene['carry_preview_ball'] = 'Production football.blend geometry, Hand.R preview attachment; excluded from player GLB'
        scene['carry_pose_reference'] = 'Assets/References/carrying.png: ball inside against ribs; forearm outside/front'
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT / ('football_player_carry_' + name.lower() + '.blend')))
        for frame in (1, 1 + (end - 1) // 4, 1 + (end - 1) // 2, 1 + 3 * (end - 1) // 4):
            scene.frame_set(frame)
            scene.render.filepath = str(PRE / ('Carry' + name + '_' + str(frame).zfill(2) + '.png'))
            bpy.ops.render.render(write_still=True)
        scene.frame_start = 1
        scene.frame_end = end - 1
        scene.render.image_settings.file_format = 'FFMPEG'
        scene.render.ffmpeg.format = 'MPEG4'
        scene.render.ffmpeg.codec = 'H264'
        scene.render.ffmpeg.constant_rate_factor = 'HIGH'
        scene.render.filepath = str(PRE / ('Carry' + name + '_loop.mp4'))
        bpy.ops.render.render(animation=True)
        scene.frame_set(1)
        export_clip(scene, rig, action, name, end)
        assert hashlib.sha256(source.read_bytes()).hexdigest() == source_hash
        print('CARRY_COMPLETE', name)


if __name__ == '__main__':
    main()
