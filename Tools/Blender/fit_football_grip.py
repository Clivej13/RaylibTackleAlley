"""Deterministic grip fitting aid against production shell; does not save assets."""
from pathlib import Path
import sys
import math
import json
import bpy
from mathutils import Vector, Matrix, Quaternion, Euler
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_football import look
from create_player_carry_animations import aim
from player_hand_rig import install


def main():
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'Assets/Models/football_player_carry_jog.blend'))
    scene = bpy.context.scene
    rig = bpy.data.objects['PlayerRig']
    install(rig, fitted=False)
    scene.frame_set(1)
    ball = bpy.data.objects['FootballPreview']
    world = ball.matrix_world.copy()
    chest = rig.pose.bones['Chest'].matrix @ rig.data.bones['Chest'].matrix_local.inverted()
    aim(rig, 'LowerArm.R', (.02, .155, -.216), chest)
    local_tree = BVHTree.FromPolygons([v.co for v in ball.data.vertices],
                                     [list(p.vertices) for p in ball.data.polygons if p.material_index == 0])
    body_samples = {}
    dg = bpy.context.evaluated_depsgraph_get()
    for name in ('RightForearm', 'RightUpperArm', 'RightChest', 'Belly', 'ShoulderPads'):
        obj = bpy.data.objects[name].evaluated_get(dg)
        pts = [v.co for v in obj.data.vertices]
        pts += [(obj.data.vertices[e.vertices[0]].co + obj.data.vertices[e.vertices[1]].co)*.5 for e in obj.data.edges]
        body_samples[name] = [obj.matrix_world @ p for p in pts]
    def ball_score(center, detail=False):
        m = world.copy()
        m.translation = chest @ Vector(center)
        inv = m.inverted()
        loss = 0.0
        result = {}
        for name, pts in body_samples.items():
            values = []
            for p in pts:
                p = inv @ p
                q, n, _, d = local_tree.find_nearest(p)
                values.append(d if (p-q).dot(n) >= 0 else -d)
            loss += 2000 * sum(min(0, v-.004)**2 for v in values)
            if name in ('RightForearm', 'ShoulderPads'):
                loss += 100 * min(abs(v-.004) for v in values)**2
            result[name] = min(values)
        loss += .5 * (Vector(center)-Vector((-.29, 1.27, -.12))).length_squared
        return (loss, result) if detail else loss
    best_center = (float('inf'), None)
    for cx in (-.285, -.31, -.335):
        for cy in (1.26, 1.285, 1.31):
            for cz in (-.12, -.145, -.17):
                center = [cx, cy, cz]
                score = ball_score(center)
                if score < best_center[0]:
                    best_center = score, center
    for step in (.012, .006, .003, .001):
        for _ in range(8):
            changed = False
            for axis in range(3):
                for sign in (-1, 1):
                    center = list(best_center[1])
                    center[axis] += sign * step
                    score = ball_score(center)
                    if score < best_center[0]:
                        best_center = score, center
                        changed = True
            if not changed:
                break
    world.translation = chest @ Vector(best_center[1])
    print('BALL_FIT', best_center[1], ball_score(best_center[1], True))
    ball.parent = None
    ball.matrix_world = world
    tree = BVHTree.FromPolygons([world @ v.co for v in ball.data.vertices],
                               [list(p.vertices) for p in ball.data.polygons if p.material_index == 0])
    obj = bpy.data.objects['RightHand']
    parts = {n: [v.index for v in obj.data.vertices if any(g.group == obj.vertex_groups[n].index for g in v.groups)]
             for n in ('Palm', 'Index', 'Middle', 'Ring', 'Little', 'Thumb')}
    chest = rig.pose.bones['Chest'].matrix @ rig.data.bones['Chest'].matrix_local.inverted()
    head = rig.pose.bones['Hand.R'].head.copy()

    def evaluate(params, detail=False):
        tilt, yaw, roll, fingers, thumb, oppose = params
        y = Vector((0, math.cos(math.radians(tilt)), math.sin(math.radians(tilt))))
        x = Vector((0, math.sin(math.radians(tilt)), -math.cos(math.radians(tilt))))
        orientation = chest.to_3x3() @ Euler((0, math.radians(yaw), math.radians(roll))).to_matrix() @ Matrix((x, y, x.cross(y))).transposed()
        desired = orientation.to_4x4()
        desired.translation = head
        rig.pose.bones['Hand.R'].matrix = desired
        rig.pose.bones['Fingers.R'].rotation_quaternion = Quaternion((0, 0, 1), math.radians(fingers))
        rig.pose.bones['Thumb.R'].rotation_quaternion = Quaternion((0, 1, 0), math.radians(oppose)) @ Quaternion((0, 0, 1), math.radians(thumb))
        bpy.context.view_layer.update()
        ev = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        result = {}
        score = 0.0
        for name, ids in parts.items():
            values = []
            for i in ids:
                p = ev.matrix_world @ ev.data.vertices[i].co
                q, n, _, d = tree.find_nearest(p)
                values.append(d if (p-q).dot(n) >= 0 else -d)
            penetration = max(0, -min(values))
            gap = min(abs(v) for v in (values if name == 'Palm' else values[-8:]))
            # Contact on each digit, with strong rejection of any penetration.
            score += 100 * sum(min(0, v-.0007)**2 for v in values)
            score += (10 if name != 'Palm' else 3) * gap**2
            result[name] = [min(values), max(values), gap]
        score += .00000005 * ((tilt-10)**2 + yaw*yaw + roll*roll)
        if detail:
            return score, result
        return score

    best = (float('inf'), None)
    for tilt in (-50, -35, -20, 0, 20, 35):
        for curl in (25, 45, 65, 85):
            p = [tilt, 0, 0, curl, 30, 0]
            val = evaluate(p)
            if val < best[0]:
                best = val, p
    p = best[1]
    limits = [(-60, 45), (-60, 60), (-60, 60), (15, 100), (-30, 80), (-65, 65)]
    for step in (12, 6, 3, 1):
        for _ in range(12):
            changed = False
            for axis in range(6):
                for sign in (-1, 1):
                    candidate = list(p)
                    candidate[axis] = max(limits[axis][0], min(limits[axis][1], p[axis] + step * sign))
                    value = evaluate(candidate)
                    if value < best[0]:
                        best = value, candidate
                        p = candidate
                        changed = True
            if not changed:
                break
    p = [-25, -17, 28, 28, 23, 14]
    score, contacts = evaluate(p, True)
    # Small per-ring translations fit the shared finger control without changing
    # topology, palm, wrist, or adding individual finger bones.
    original = [v.co.copy() for v in obj.data.vertices]
    for _ in range(3):
        bpy.context.view_layer.update()
        ev = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        adjustments = []
        for name, indices in parts.items():
            if name == 'Palm':
                continue
            for ring in (1, 2, 3):
                ids = indices[ring*8:(ring+1)*8]
                closest = None
                for index in ids:
                    v = ev.matrix_world @ ev.data.vertices[index].co
                    q, normal, _, distance = tree.find_nearest(v)
                    signed = distance if (v-q).dot(normal) >= 0 else -distance
                    if closest is None or signed < closest[0]:
                        closest = signed, normal
                distance, normal = closest
                if ring < 3 and distance > .0048:
                    continue
                delta = normal * (.0048-distance)
                for index in ids:
                    skin = Matrix(((0, 0, 0, 0),)*4)
                    for group in obj.data.vertices[index].groups:
                        group_name = obj.vertex_groups[group.group].name
                        if group_name in rig.data.bones:
                            skin += (rig.pose.bones[group_name].matrix @ rig.data.bones[group_name].matrix_local.inverted()) * group.weight
                    adjustments.append((index, skin.to_3x3().inverted() @ delta))
        for index, delta in adjustments:
            obj.data.vertices[index].co += delta
        obj.data.update()
    corrections = {str(i): list(v.co-original[i]) for i, v in enumerate(obj.data.vertices) if (v.co-original[i]).length > 1e-8}
    result = {'ball_center_chest_rest_m': best_center[1], 'wrist_fingers_thumb_degrees': p,
              'right_hand_vertex_deltas': corrections,
              'maximum_vertex_delta_m': max(Vector(v).length for v in corrections.values())}
    (ROOT / 'Tools/Blender/hand_grip_fit.json').write_text(json.dumps(result, indent=2))
    score, contacts = evaluate(p, True)
    for n in ('LowerArm.R', 'Hand.R', 'Fingers.R', 'Thumb.R'):
        rig.pose.bones[n].keyframe_insert('rotation_quaternion', frame=1, group=n)
    print('GRIP_FIT', json.dumps({'parameters': p, 'score': score, 'contacts': contacts}))
    cam = scene.camera
    cam.location = ball.matrix_world.translation + Vector((-.32, .28, -.4))
    look(cam, ball.matrix_world.translation)
    cam.data.ortho_scale = .36
    scene.render.resolution_x = scene.render.resolution_y = 800
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(ROOT / 'Assets/Models/HandRigPreviews/grip_fit.png')
    bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    main()
