"""Validate saved carry blends/GLBs against approved sources via Blender MCP."""
from pathlib import Path
import hashlib
import json
import math
import struct
import sys
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
PRE = OUT / 'CarryPreviews'
sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_player_carry_animations import MODIFIED, PROXY
from validate_helmet_preservation import properties, matrix, digest
from validate_helmet_skinning import head_relative_vertices, validate_glb


def curves(action):
    return {(f.data_path, f.array_index): digest((f.extrapolation,
            [properties(k) for k in f.keyframe_points],
            [properties(m) for m in f.modifiers])) for f in action.fcurves}


def rig_signature():
    rig = bpy.data.objects['PlayerRig']
    meshes = {}
    for o in bpy.context.scene.objects:
        if o.type != 'MESH' or o.name == PROXY:
            continue
        meshes[o.name] = digest((
            [list(v.co) for v in o.data.vertices],
            [(list(p.vertices), p.material_index, p.use_smooth) for p in o.data.polygons],
            [m.name if m else None for m in o.data.materials],
            [g.name for g in o.vertex_groups],
            [[(g.group, g.weight) for g in v.groups] for v in o.data.vertices],
            o.parent.name if o.parent else None, o.parent_type, o.parent_bone,
            matrix(o.matrix_basis), matrix(o.matrix_parent_inverse),
            [properties(m) for m in o.modifiers]))
    bones = [(b.name, b.parent.name if b.parent else None, matrix(b.matrix_local),
              list(b.head_local), list(b.tail_local), b.use_deform, b.use_connect,
              b.inherit_scale, b.use_inherit_rotation) for b in rig.data.bones]
    return digest((meshes, bones))


def timing():
    s = bpy.context.scene
    return (s.frame_start, s.frame_end, s.render.fps, s.render.fps_base,
            [(m.name, m.frame) for m in s.timeline_markers])


def sample_matrices(action):
    rig = bpy.data.objects['PlayerRig']
    rig.animation_data.action = action
    start, end = map(int, action.frame_range)
    result = []
    for tick in range(start * 4, end * 4 + 1):
        bpy.context.scene.frame_set(tick // 4, subframe=(tick % 4) / 4)
        result.append({p.name: matrix(p.matrix) for p in rig.pose.bones if p.name not in MODIFIED})
    return result


def glb_channels(path):
    raw = path.read_bytes()
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + size])
    binary = raw[28 + size:]

    def read(index):
        a = doc['accessors'][index]
        v = doc['bufferViews'][a['bufferView']]
        assert a['componentType'] == 5126
        width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[a['type']]
        offset = v.get('byteOffset', 0) + a.get('byteOffset', 0)
        stride = v.get('byteStride', width * 4)
        return [struct.unpack_from('<' + 'f' * width, binary, offset + i * stride) for i in range(a['count'])]

    assert len(doc['animations']) == 1
    clip = doc['animations'][0]
    channels = {}
    for c in clip['channels']:
        sampler = clip['samplers'][c['sampler']]
        name = doc['nodes'][c['target']['node']]['name']
        channels[name, c['target']['path']] = (sampler.get('interpolation', 'LINEAR'),
                                              read(sampler['input']), read(sampler['output']))
    return doc, channels


def contacts(rig, end):
    scene = bpy.context.scene
    ball = bpy.data.objects[PROXY]
    first = None
    drift = 0.0
    centers = []
    penetration = 0.0
    hand_gap = 0.0
    elbow_clearance = float('inf')
    torso = ('RightChest', 'LeftChest', 'Belly', 'LowerBack', 'UpperBack', 'ShoulderPads')
    for tick in range(4, end * 4 + 1):
        scene.frame_set(tick // 4, subframe=(tick % 4) / 4)
        dg = bpy.context.evaluated_depsgraph_get()
        head_relative_vertices(rig, dg)
        assert max(abs(rig.pose.bones['Root'].matrix_basis[i][j] - Matrix.Identity(4)[i][j])
                   for i in range(4) for j in range(4)) < 1e-8
        rel = rig.pose.bones['Hand.R'].matrix.inverted() @ ball.matrix_world
        if first is None:
            first = rel.copy()
        drift = max(drift, max(abs(rel[i][j] - first[i][j]) for i in range(4) for j in range(4)))
        centers.append(rig.pose.bones['Chest'].matrix.inverted() @ ball.matrix_world.translation)
        vertices = [ball.matrix_world @ v.co for v in ball.data.vertices]
        hand = bpy.data.objects['RightHand'].evaluated_get(dg)
        ball_tree = BVHTree.FromPolygons(vertices, [list(p.vertices) for p in ball.data.polygons])
        hand_gap = max(hand_gap, min(ball_tree.find_nearest(hand.matrix_world @ v.co)[3] for v in hand.data.vertices))
        elbow = rig.pose.bones['LowerArm.R'].head
        for name in torso:
            obj = bpy.data.objects[name].evaluated_get(dg)
            tree = BVHTree.FromPolygons([obj.matrix_world @ v.co for v in obj.data.vertices],
                                       [list(p.vertices) for p in obj.data.polygons])
            elbow_clearance = min(elbow_clearance, tree.find_nearest(elbow)[3])
            def inside(vertex):
                # Ray parity avoids treating the back of an open surface as a volume.
                for direction in (Vector((1, .371, .529)).normalized(), Vector((-1, -.217, .613)).normalized()):
                    origin = vertex.copy()
                    hits = 0
                    for _ in range(100):
                        point, normal, index, distance = tree.ray_cast(origin, direction)
                        if point is None:
                            break
                        hits += 1
                        origin = point + direction * 1e-6
                    if hits % 2 == 0:
                        return False
                return True
            for vertex in vertices:
                point, normal, index, distance = tree.find_nearest(vertex)
                if inside(vertex):
                    penetration = max(penetration, distance)
    excursion = max((a - b).length for a in centers for b in centers)
    assert drift < 1e-5
    assert excursion < .03, excursion
    assert penetration < .002, penetration
    assert hand_gap < .005, hand_gap
    assert elbow_clearance > .05, elbow_clearance
    def endpoint(frame):
        scene.frame_set(frame)
        dg = bpy.context.evaluated_depsgraph_get()
        return [obj.matrix_world @ v.co for src in scene.objects if src.type == 'MESH'
                for obj in [src.evaluated_get(dg)] for v in obj.data.vertices]
    loop_error = max((a-b).length for a, b in zip(endpoint(1), endpoint(end)))
    assert loop_error < 1e-5, loop_error
    return {'hand_attachment_matrix_error': drift, 'ball_chest_relative_excursion_m': excursion,
            'loop_endpoint_all_mesh_vertex_error_m': loop_error,
            'max_proxy_vertex_torso_penetration_m': penetration,
            'max_nearest_hand_surface_gap_m': hand_gap,
            'minimum_elbow_center_torso_surface_distance_m': elbow_clearance}


def main():
    if (ROOT / 'Tools/Blender/hand_rig_baseline.json.gz').exists():
        from validate_player_hands import main as validate_hands
        return validate_hands()
    report = {}
    allowed = {'pose.bones["' + b + '"].rotation_quaternion' for b in MODIFIED}
    for name in ('Jog', 'Run', 'Sprint'):
        source = OUT / ('lowpoly_human_' + name.lower() + '.blend')
        bpy.ops.wm.open_mainfile(filepath=str(source))
        signature = rig_signature()
        source_timing = timing()
        originals = {a.name: curves(a) for a in bpy.data.actions}
        original_range = list(bpy.data.actions[name].frame_range)
        samples = sample_matrices(bpy.data.actions[name])
        bpy.ops.wm.open_mainfile(filepath=str(OUT / ('football_player_carry_' + name.lower() + '.blend')))
        assert rig_signature() == signature, name + ': geometry/weights/skeleton changed'
        assert timing() == source_timing, name + ': timing changed'
        assert set(bpy.data.actions.keys()) == set(originals) | {'Carry' + name}
        for key, value in originals.items():
            assert curves(bpy.data.actions[key]) == value, key + ': original action changed'
        action = bpy.data.actions['Carry' + name]
        assert list(action.frame_range) == original_range
        new = curves(action)
        assert new.keys() == originals[name].keys()
        changed = [key for key in new if new[key] != originals[name][key]]
        assert changed and all(key[0] in allowed for key in changed), changed
        actual = sample_matrices(action)
        assert actual == samples, name + ': non-carry pose matrices changed'
        rig = bpy.data.objects['PlayerRig']
        metrics = contacts(rig, int(original_range[1]))
        path = OUT / ('football_player_carry_' + name.lower() + '.glb')
        counts = validate_glb(path)
        doc, channels = glb_channels(path)
        _, source_channels = glb_channels(OUT / ('lowpoly_human_' + name.lower() + '_validation.glb'))
        assert doc['animations'][0]['name'] == 'Carry' + name
        assert not any(n.get('name') == PROXY for n in doc['nodes'])
        assert channels.keys() == source_channels.keys()
        export_roundoff = 0.0
        for key in channels:
            if key[0] not in MODIFIED:
                assert channels[key] == source_channels[key], (name, 'GLB changed', key)
            elif key[1] != 'rotation':
                a, b = channels[key], source_channels[key]
                assert a[:2] == b[:2] and len(a[2]) == len(b[2])
                error = max(abs(x-y) for va, vb in zip(a[2], b[2]) for x, y in zip(va, vb))
                export_roundoff = max(export_roundoff, error)
                assert error < 1e-6, (name, key, error)
        report['Carry' + name] = {
            'modified_bones': list(MODIFIED), 'modified_quaternion_channels': len(changed),
            'keyed_frame_range': original_range, 'fps': source_timing[2],
            'duration_seconds': (original_range[1] - original_range[0]) / source_timing[2],
            'all_other_curves_exact': True, 'all_other_pose_matrices_exact_at_quarter_frames': True,
            'original_actions_unchanged': True, 'geometry_weights_equipment_skeleton_rest_pose_unchanged': True,
            'root_motion': False, 'exported_non_carry_channels_exact': True,
            'right_arm_export_translation_scale_roundoff': export_roundoff,
            'helmet_head_skinned_export_vertices': counts, 'preview_proxy_excluded_from_glb': True,
            **metrics}
    baseline = json.loads((PRE / 'source_preservation.json').read_text())
    changed = [p for p, h in baseline.items() if hashlib.sha256((ROOT / p).read_bytes()).hexdigest() != h]
    assert not changed, changed
    report['existing_files_unchanged'] = len(baseline)
    (PRE / 'validation.json').write_text(json.dumps(report, indent=2))
    print('CARRY_VALIDATION_PASSED', json.dumps(report))


if __name__ == '__main__':
    main()
