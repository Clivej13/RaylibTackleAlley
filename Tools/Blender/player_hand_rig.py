"""Four additional hand controls; preserve the established arm and wrist bones."""
from pathlib import Path
import math
import json
import bpy
from mathutils import Vector, Matrix, Quaternion, Euler

OUT = Path(__file__).resolve().parents[2] / 'Assets/Models'
HAND_BONES = ('Fingers.L', 'Thumb.L', 'Fingers.R', 'Thumb.R')
BALL = 'FootballPreview'
FIT = json.loads((Path(__file__).resolve().parent / 'hand_grip_fit.json').read_text())


def install(rig, fitted=True):
    active = rig.animation_data.action if rig.animation_data else None
    if 'Fingers.R' not in rig.data.bones:
        bpy.ops.object.select_all(action='DESELECT')
        rig.select_set(True)
        bpy.context.view_layer.objects.active = rig
        bpy.ops.object.mode_set(mode='EDIT')
        for side in ('L', 'R'):
            hand = rig.data.edit_bones['Hand.' + side]
            for name, head, tail in [('Fingers', (0, .080, 0), (0, .145, 0)),
                                     ('Thumb', (0, .028, -.028), (0, .088, -.062))]:
                b = rig.data.edit_bones.new(name + '.' + side)
                b.head = hand.matrix @ Vector(head)
                b.tail = hand.matrix @ Vector(tail)
                b.align_roll(hand.matrix.to_3x3() @ Vector((0, 0, 1)))
                b.parent = hand
        bpy.ops.object.mode_set(mode='OBJECT')
    for side, label in [('L', 'Left'), ('R', 'Right')]:
        obj = bpy.data.objects[label + 'Hand']
        with bpy.data.libraries.load(str(OUT / 'lowpoly_human_stage6.blend'), link=False) as (source, target):
            target.meshes = [label + 'Hand_Mesh']
        original = target.meshes[0]
        assert len(original.vertices) == len(obj.data.vertices)
        for vertex, base in zip(obj.data.vertices, original.vertices):
            vertex.co = base.co
            if fitted and str(vertex.index) in FIT['right_hand_vertex_deltas']:
                delta = Vector(FIT['right_hand_vertex_deltas'][str(vertex.index)])
                if side == 'L':
                    delta.x = -delta.x
                vertex.co += delta
        obj.data.update()
        bpy.data.meshes.remove(original)
        groups = {g.name: g for g in obj.vertex_groups}
        regions = {name: [v.index for v in obj.data.vertices if any(e.group == groups[name].index and e.weight > 0 for e in v.groups)]
                   for name in ('Index', 'Middle', 'Ring', 'Little', 'Thumb')}
        for name in ('Fingers.' + side, 'Thumb.' + side):
            if name not in groups:
                groups[name] = obj.vertex_groups.new(name=name)
        hand_group = groups['Hand.' + side]
        for name, indices in regions.items():
            target = groups[('Thumb.' if name == 'Thumb' else 'Fingers.') + side]
            # Four existing eight-vertex rings per digit. Spread bending over the
            # rings; retain palm/root support instead of collapsing a single hinge.
            assert len(indices) == 32, (obj.name, name)
            for i, index in enumerate(indices):
                weight = (0.0, .35, .80, 1.0)[i // 8]
                hand_group.remove([index])
                target.remove([index])
                if weight < 1:
                    hand_group.add([index], 1 - weight, 'REPLACE')
                if weight:
                    target.add([index], weight, 'REPLACE')
        obj['hand_articulation'] = 'Six original components; fingertip ring fitting <10 mm; grouped fingers and independent thumb'
    if active:
        rig.animation_data.action = active


def key_hand_poses(rig):
    active = rig.animation_data.action
    frame = bpy.context.scene.frame_current
    for action in bpy.data.actions:
        rig.animation_data.action = action
        start, end = map(int, action.frame_range)
        for fc in list(action.fcurves):
            if any('"' + name + '"' in fc.data_path for name in HAND_BONES):
                action.fcurves.remove(fc)
        for f in sorted({start, end}):
            bpy.context.scene.frame_set(f)
            for side in ('L', 'R'):
                for name, angle in [('Fingers', 12), ('Thumb', 8)]:
                    bone = rig.pose.bones[name + '.' + side]
                    bone.rotation_mode = 'QUATERNION'
                    # Mirrored palm normals require opposite curl signs.
                    sign = 1 if side == 'R' else -1
                    bone.rotation_quaternion = Quaternion((0, 0, 1), math.radians(sign * angle))
                    bone.location = (0, 0, 0)
                    bone.scale = (1, 1, 1)
                    for prop in ('location', 'rotation_quaternion', 'scale'):
                        bone.keyframe_insert(prop, frame=f, group=bone.name)
    rig.animation_data.action = active
    bpy.context.scene.frame_set(frame)


def aim_carry_bone(rig, name, direction, chest):
    pose = rig.pose.bones[name]
    rest = pose.bone
    turn = (rest.tail_local-rest.head_local).rotation_difference(Vector(direction))
    desired = (chest.to_3x3() @ turn.to_matrix() @ rest.matrix_local.to_3x3()).to_4x4()
    desired.translation = pose.head
    pose.matrix = desired
    bpy.context.view_layer.update()


def inside_tuck(rig, chest, bob=0, upper=(-.14, -.225, -.06), roll=100):
    """Turn the fitted grip inward as a unit, without changing finger contact."""
    lower = rig.pose.bones['LowerArm.R']
    hand = rig.pose.bones['Hand.R']
    old_lower, old_hand = lower.matrix.copy(), hand.matrix.copy()
    aim_carry_bone(rig, 'UpperArm.R', (upper[0], upper[1], upper[2]+bob), chest)
    axis = (old_lower.to_3x3() @ Vector((0, 1, 0))).normalized()
    transform = (Matrix.Translation(lower.head)
                 @ Quaternion(axis, math.radians(roll)).to_matrix().to_4x4()
                 @ Matrix.Translation(-old_lower.translation))
    lower.matrix = transform @ old_lower
    bpy.context.view_layer.update()
    hand.matrix = transform @ old_hand
    bpy.context.view_layer.update()
    return transform


def fit_carry(rig, action, inside=True):
    rig.animation_data.action = action
    start, end = map(int, action.frame_range)
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        chest = rig.pose.bones['Chest'].matrix @ rig.data.bones['Chest'].matrix_local.inverted()
        impact = {'CarryJog': .6, 'CarryRun': 1.0, 'CarrySprint': 1.4}[action.name]
        bob = impact * math.sin(4*math.pi*(frame-start)/(end-start)) * .003
        # Rebuild the established grip before rotating it inward; this also makes
        # refitting an already corrected carry idempotent.
        aim_carry_bone(rig, 'UpperArm.R', (-.079, -.265, .003+bob), chest)
        lower = rig.pose.bones['LowerArm.R']
        rest = lower.bone
        turn = (rest.tail_local-rest.head_local).rotation_difference(Vector((.02, .155+bob, -.216)))
        desired = (chest.to_3x3() @ turn.to_matrix() @ rest.matrix_local.to_3x3()).to_4x4()
        desired.translation = lower.head
        lower.matrix = desired
        lower.keyframe_insert('rotation_quaternion', frame=frame, group=lower.name)
        bpy.context.view_layer.update()
        tilt, yaw, roll, curl, thumb, oppose = FIT['wrist_fingers_thumb_degrees']
        tilt = math.radians(tilt)
        y = Vector((0, math.cos(tilt), math.sin(tilt)))
        x = Vector((0, math.sin(tilt), -math.cos(tilt)))
        orientation = chest.to_3x3() @ Euler((0, math.radians(yaw), math.radians(roll))).to_matrix() @ Matrix((x, y, x.cross(y))).transposed()
        pose = rig.pose.bones['Hand.R']
        desired = orientation.to_4x4()
        desired.translation = pose.head
        pose.matrix = desired
        pose.keyframe_insert('rotation_quaternion', frame=frame, group=pose.name)
        for name, angle in [('Fingers.R', curl), ('Thumb.R', thumb)]:
            p = rig.pose.bones[name]
            p.rotation_quaternion = Quaternion((0, 0, 1), math.radians(angle))
            if name == 'Thumb.R':
                p.rotation_quaternion = Quaternion((0, 1, 0), math.radians(oppose)) @ p.rotation_quaternion
            p.keyframe_insert('rotation_quaternion', frame=frame, group=name)
        bpy.context.view_layer.update()
        transform = inside_tuck(rig, chest, bob) if inside else Matrix.Identity(4)
        for name in ('UpperArm.R', 'LowerArm.R', 'Hand.R'):
            rig.pose.bones[name].keyframe_insert('rotation_quaternion', frame=frame, group=name)
        if frame == start:
            rig['inside_carry_ball_transform'] = [v for row in transform for v in row]
    for fc in action.fcurves:
        if any('"' + n + '"' in fc.data_path for n in ('UpperArm.R', 'LowerArm.R', 'Hand.R', 'Fingers.R', 'Thumb.R')):
            for key in fc.keyframe_points:
                key.interpolation = 'LINEAR'
    bpy.context.scene.frame_set(1)


def attach_football(rig):
    for name in ('TEMP_PreviewFootball_DO_NOT_EXPORT', BALL):
        if name in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    with bpy.data.libraries.load(str(OUT / 'football.blend'), link=False) as (source, target):
        target.objects = ['Football']
    ball = target.objects[0]
    bpy.context.scene.collection.objects.link(ball)
    ball.name = BALL
    chest = rig.pose.bones['Chest'].matrix @ rig.data.bones['Chest'].matrix_local.inverted()
    z = Vector((.02, .115, -.237)).normalized()
    # Laces face up and slightly outward for clear preview inspection.
    x = Vector((0, 1, 0)).cross(z).normalized()
    y = z.cross(x)
    world = chest @ Matrix.Translation(Vector(FIT['ball_center_chest_rest_m'])) @ Matrix((x, y, z)).transposed().to_4x4()
    if 'inside_carry_ball_transform' in rig:
        values = rig['inside_carry_ball_transform']
        world = Matrix([values[i:i+4] for i in range(0, 16, 4)]) @ world
    ball.parent = rig
    ball.parent_type = 'BONE'
    ball.parent_bone = 'Hand.R'
    bpy.context.view_layer.update()
    ball.matrix_world = world
    bpy.context.view_layer.update()
    ball['preview_only'] = True
    ball['source_asset'] = 'football.glb; exact geometry from football.blend'
    ball['hand_bone_relative_matrix'] = [v for row in (rig.pose.bones['Hand.R'].matrix.inverted() @ world) for v in row]
    return ball
