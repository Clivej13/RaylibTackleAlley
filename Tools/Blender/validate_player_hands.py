"""Capture approved content before hand upgrade, then verify the saved results."""
from pathlib import Path
import gzip
import hashlib
import json
import sys
import struct
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
PRE = OUT / 'HandRigPreviews'
BASE = ROOT / 'Tools/Blender/hand_rig_baseline.json.gz'
sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_helmet_preservation import properties, matrix, digest
from player_hand_rig import HAND_BONES, BALL
from validate_helmet_skinning import head_relative_vertices, validate_glb

FILES = ['lowpoly_human_' + n + '.blend' for n in ('rigged', 'jog', 'run', 'sprint')]
FILES += ['football_player_carry_' + n + '.blend' for n in ('jog', 'run', 'sprint')]


def snapshot():
    rig = bpy.data.objects['PlayerRig']
    scene = bpy.context.scene
    actions = {}
    for a in bpy.data.actions:
        actions[a.name] = {f.data_path + ':' + str(f.array_index): digest((f.extrapolation,
            [properties(k) for k in f.keyframe_points], [properties(m) for m in f.modifiers])) for f in a.fcurves}
    meshes = {}
    for o in scene.objects:
        if o.type != 'MESH' or o.name in (BALL, 'TEMP_PreviewFootball_DO_NOT_EXPORT'):
            continue
        meshes[o.name] = {
            'geometry': digest(([list(v.co) for v in o.data.vertices],
                [(list(p.vertices), p.material_index, p.use_smooth) for p in o.data.polygons],
                [m.name for m in o.data.materials])),
            'rigging': digest(([[ (o.vertex_groups[g.group].name, g.weight) for g in v.groups if g.weight]
                for v in o.data.vertices], o.parent.name if o.parent else None, o.parent_type, o.parent_bone,
                matrix(o.matrix_basis), matrix(o.matrix_parent_inverse), [properties(m) for m in o.modifiers]))}
    bones = {b.name: (b.parent.name if b.parent else None, matrix(b.matrix_local),
                     list(b.head_local), list(b.tail_local), b.use_deform) for b in rig.data.bones}
    active = rig.animation_data.action
    start, end = map(int, active.frame_range)
    samples = []
    for tick in range(start * 4, end * 4 + 1):
        scene.frame_set(tick // 4, subframe=tick % 4 / 4)
        samples.append({b.name: matrix(b.matrix) for b in rig.pose.bones})
    return {'actions': actions, 'meshes': meshes, 'bones': bones, 'samples': samples,
            'active': active.name, 'range': [start, end],
            'timing': [scene.frame_start, scene.frame_end, scene.render.fps, scene.render.fps_base,
                       [(m.name, m.frame) for m in scene.timeline_markers]]}


def code_hashes():
    paths = [p for p in ROOT.rglob('*.cs') if not {'obj', 'bin'} & set(p.parts)]
    paths += list(ROOT.glob('*.json'))
    return {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}


def capture():
    assert not BASE.exists(), 'Keep the pre-upgrade baseline; do not overwrite it'
    result = {'code': code_hashes(), 'assets': {}}
    for name in FILES:
        bpy.ops.wm.open_mainfile(filepath=str(OUT / name))
        result['assets'][name] = snapshot()
    BASE.write_bytes(gzip.compress(json.dumps(result).encode(), mtime=0))
    print('HAND_BASELINE_CAPTURED')


def glb(path):
    raw = path.read_bytes()
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + size])
    binary = raw[28 + size:]
    def read(index):
        a = doc['accessors'][index]
        v = doc['bufferViews'][a['bufferView']]
        code = {5121: 'B', 5123: 'H', 5125: 'I', 5126: 'f'}[a['componentType']]
        width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
        fmt = '<' + code * width
        offset = a.get('byteOffset', 0) + v.get('byteOffset', 0)
        stride = v.get('byteStride', struct.calcsize(fmt))
        return [struct.unpack_from(fmt, binary, offset + stride*i) for i in range(a['count'])]
    return doc, read


def validate_exports(football_positions):
    filenames = ['football_player.glb']
    filenames += ['lowpoly_human_' + n + '_validation.glb' for n in ('jog', 'run', 'sprint')]
    filenames += ['football_player_carry_' + n + '.glb' for n in ('jog', 'run', 'sprint')]
    skeleton = None
    clips = {}
    counts = {}
    for name in filenames:
        doc, read = glb(OUT / name)
        assert len(doc['skins']) == 1
        skin = doc['skins'][0]
        joints = skin['joints']
        names = [doc['nodes'][i]['name'] for i in joints]
        assert len(names) == 24 and set(HAND_BONES) <= set(names)
        parents = {child: i for i, node in enumerate(doc['nodes']) for child in node.get('children', [])}
        signature = (names, [doc['nodes'][parents[i]].get('name') if i in parents else None for i in joints],
                     read(skin['inverseBindMatrices']))
        if skeleton is None:
            skeleton = signature
        assert signature == skeleton, (name, 'incompatible exported skeleton')
        for n in HAND_BONES:
            assert doc['nodes'][parents[joints[names.index(n)]]]['name'] == 'Hand.' + n[-1]
        assert not any(n.get('name') in ('Football', BALL, 'TEMP_PreviewFootball_DO_NOT_EXPORT') for n in doc['nodes'])
        counts[name] = validate_glb(OUT / name)
        for side, label in [('L', 'Left'), ('R', 'Right')]:
            node = next(n for n in doc['nodes'] if n.get('name') == label + 'Hand')
            found = set()
            for primitive in doc['meshes'][node['mesh']]['primitives']:
                attrs = primitive['attributes']
                for js, ws in zip(read(attrs['JOINTS_0']), read(attrs['WEIGHTS_0'])):
                    assert abs(sum(ws)-1) < 1e-6
                    influence = {names[j] for j, w in zip(js, ws) if w > 0}
                    assert influence <= {'Hand.' + side, 'Fingers.' + side, 'Thumb.' + side}
                    found |= influence
            assert found == {'Hand.' + side, 'Fingers.' + side, 'Thumb.' + side}
        assert len(doc['animations']) == 1
        clip = doc['animations'][0]
        channels = {}
        for c in clip['channels']:
            sampler = clip['samplers'][c['sampler']]
            key = (doc['nodes'][c['target']['node']]['name'], c['target']['path'])
            channels[key] = (read(sampler['input']), read(sampler['output']))
        clips[name] = channels
        times = [row[0] for sampler in clip['samplers'] for row in read(sampler['input'])]
        duration = max(times) - min(times)
        intervals = 28 if 'jog' in name or name == 'football_player.glb' else 24 if 'run' in name else 20
        assert abs(duration - intervals/30) < 1e-6
    for name in ('jog', 'run', 'sprint'):
        normal = clips['lowpoly_human_' + name + '_validation.glb']
        carry = clips['football_player_carry_' + name + '.glb']
        for key, value in normal.items():
            if key[0] not in ('UpperArm.R', 'LowerArm.R', 'Hand.R', 'Fingers.R', 'Thumb.R'):
                assert carry[key] == value, (name, key, 'carry changed other exported motion')
    assert clips['football_player.glb'] == clips['lowpoly_human_jog_validation.glb']
    ball_doc, ball_read = glb(OUT / 'football.glb')
    assert not ball_doc.get('skins') and not ball_doc.get('animations')
    assert len(ball_doc['nodes']) == 1 and ball_doc['nodes'][0]['name'] == 'Football'
    node = ball_doc['nodes'][0]
    assert node.get('translation', [0, 0, 0]) == [0, 0, 0]
    assert node.get('rotation', [0, 0, 0, 1]) == [0, 0, 0, 1]
    assert node.get('scale', [1, 1, 1]) == [1, 1, 1]
    exported_positions = {tuple(p) for primitive in ball_doc['meshes'][node['mesh']]['primitives']
                          for p in ball_read(primitive['attributes']['POSITION'])}
    assert exported_positions == football_positions, 'Standalone football export geometry differs'
    return {'consistent_joint_count': 24, 'joint_order_and_inverse_bind_matrices_exact': True,
            'all_seven_player_exports_checked': filenames, 'helmet_head_only_vertices': counts,
            'carry_non_right_arm_export_channels_exact': True, 'football_independent_identity_transform': True}


def mesh_geometry(obj):
    return digest(([list(v.co) for v in obj.data.vertices],
                   [(list(p.vertices), p.material_index) for p in obj.data.polygons]))


def grip_metrics(rig, require_loop=True):
    scene = bpy.context.scene
    ball = bpy.data.objects[BALL]
    obj = bpy.data.objects['RightHand']
    parts = {n: [v.index for v in obj.data.vertices if any(g.group == obj.vertex_groups[n].index for g in v.groups)]
             for n in ('Palm', 'Index', 'Middle', 'Ring', 'Little', 'Thumb')}
    gaps = {n: 0.0 for n in parts}
    penetration = 0.0
    body_penetration = 0.0
    ball_positions = []
    reference = None
    attachment_error = 0.0
    start, end = map(int, rig.animation_data.action.frame_range)
    for tick in range(start*4, end*4+1):
        scene.frame_set(tick//4, subframe=tick%4/4)
        dg = bpy.context.evaluated_depsgraph_get()
        hand = obj.evaluated_get(dg)
        ball_vertices = [ball.matrix_world @ v.co for v in ball.data.vertices]
        tree = BVHTree.FromPolygons(ball_vertices,
            [list(p.vertices) for p in ball.data.polygons if p.material_index == 0])
        for name, ids in parts.items():
            values = []
            for index in ids:
                p = hand.matrix_world @ hand.data.vertices[index].co
                q, normal, _, distance = tree.find_nearest(p)
                value = distance if (p-q).dot(normal) >= 0 else -distance
                values.append(value)
                penetration = max(penetration, -value)
            gaps[name] = max(gaps[name], min(abs(v) for v in (values if name == 'Palm' else values[-8:])))
        points = [(hand.data.vertices[e.vertices[0]].co + hand.data.vertices[e.vertices[1]].co)*.5 for e in hand.data.edges]
        points += [p.center for p in hand.data.polygons]
        for v in points:
            p = hand.matrix_world @ v
            q, normal, _, distance = tree.find_nearest(p)
            if (p-q).dot(normal) < 0:
                penetration = max(penetration, distance)
        # The leather shell is closed and convex, so nearest outward normals give
        # a reliable signed distance for body vertices against the actual facets.
        for name in ('RightForearm', 'RightUpperArm', 'RightChest', 'Belly', 'ShoulderPads'):
            body = bpy.data.objects[name].evaluated_get(dg)
            points = [v.co for v in body.data.vertices]
            points += [(body.data.vertices[e.vertices[0]].co + body.data.vertices[e.vertices[1]].co)*.5 for e in body.data.edges]
            points += [p.center for p in body.data.polygons]
            for v in points:
                p = body.matrix_world @ v
                q, normal, _, distance = tree.find_nearest(p)
                if (p-q).dot(normal) < 0:
                    body_penetration = max(body_penetration, distance)
        relative = rig.pose.bones['Hand.R'].matrix.inverted() @ ball.matrix_world
        if reference is None:
            reference = relative.copy()
        attachment_error = max(attachment_error, max(abs(relative[i][j]-reference[i][j]) for i in range(4) for j in range(4)))
        ball_positions.append(rig.pose.bones['Chest'].matrix.inverted() @ ball.matrix_world.translation)
        assert rig.pose.bones['Root'].matrix_basis == Matrix.Identity(4)
        head_relative_vertices(rig, dg)
    assert penetration < .001, penetration
    assert max(gaps.values()) < .006, gaps
    assert body_penetration < .002, body_penetration
    assert attachment_error < 1e-5
    def vertices(frame):
        scene.frame_set(frame)
        dg = bpy.context.evaluated_depsgraph_get()
        return [o.matrix_world @ v.co for src in scene.objects if src.type == 'MESH'
                for o in [src.evaluated_get(dg)] for v in o.data.vertices]
    loop_error = max((a-b).length for a, b in zip(vertices(start), vertices(end))) if require_loop else None
    if require_loop:
        assert loop_error < 1e-5
    return {'max_hand_vertex_penetration_m': penetration, 'max_body_vertex_penetration_m': body_penetration,
            'surface_samples': 'vertices, edge midpoints, face centres at quarter frames',
            'maximum_distal_contact_gap_m': gaps, 'attachment_matrix_error': attachment_error,
            'ball_chest_relative_excursion_m': max((a-b).length for a in ball_positions for b in ball_positions),
            'loop_all_mesh_vertex_error_m': loop_error}


def main():
    before = json.loads(gzip.decompress(BASE.read_bytes()))
    assert code_hashes() == before['code'], 'C# or root JSON changed'
    report = {'code_and_root_json_unchanged': True, 'assets': {}}
    bpy.ops.wm.open_mainfile(filepath=str(OUT / 'football.blend'))
    football = bpy.data.objects['Football']
    football_geometry = mesh_geometry(football)
    assert football.parent is None and football.matrix_world == Matrix.Identity(4)
    shell = [football.data.vertices[i].co for p in football.data.polygons if p.material_index == 0 for i in p.vertices]
    for axis in range(3):
        assert abs(min(v[axis] for v in shell) + max(v[axis] for v in shell)) < 1e-7
    report['football_dimensions_m'] = list(football.dimensions)
    football_positions = {tuple(v.co) for v in football.data.vertices}
    hand_geometry = None
    for name in FILES:
        bpy.ops.wm.open_mainfile(filepath=str(OUT / name))
        new = snapshot()
        old = before['assets'][name]
        assert digest(new['timing']) == digest(old['timing']) and new['range'] == old['range']
        assert set(new['bones']) == set(old['bones']) | set(HAND_BONES)
        for bone, value in old['bones'].items():
            assert digest(new['bones'][bone]) == digest(value), (name, bone, 'rest')
        for bone in HAND_BONES:
            assert new['bones'][bone][0] == 'Hand.' + bone[-1]
        for mesh, value in old['meshes'].items():
            if mesh not in ('RightHand', 'LeftHand'):
                assert new['meshes'][mesh]['geometry'] == value['geometry'], (name, mesh, 'geometry')
                assert new['meshes'][mesh]['rigging'] == value['rigging'], (name, mesh, 'weights')
        for action, curves in old['actions'].items():
            for key, value in curves.items():
                if action.startswith('Carry') and any('"' + n + '"' in key for n in ('UpperArm.R','LowerArm.R','Hand.R')) and 'rotation_quaternion' in key:
                    continue
                assert new['actions'][action][key] == value, (name, action, key)
        for a, b in zip(old['samples'], new['samples']):
            for bone, value in a.items():
                if old['active'].startswith('Carry') and bone in ('UpperArm.R', 'LowerArm.R', 'Hand.R'):
                    continue
                assert b[bone] == value, (name, bone, 'animated pose')
        rig = bpy.data.objects['PlayerRig']
        current_hands = {}
        for side, label in [('L', 'Left'), ('R', 'Right')]:
            obj = bpy.data.objects[label + 'Hand']
            with bpy.data.libraries.load(str(OUT / 'lowpoly_human_stage6.blend'), link=False) as (source, target):
                target.meshes = [label + 'Hand_Mesh']
            original = target.meshes[0]
            assert [tuple(p.vertices) for p in original.polygons] == [tuple(p.vertices) for p in obj.data.polygons]
            maximum = 0.0
            for old_v, v in zip(original.vertices, obj.data.vertices):
                delta = (old_v.co-v.co).length
                maximum = max(maximum, delta)
                if v.index < 32 or v.index % 32 < 8:
                    assert delta == 0, 'Palm or digit root changed'
                weights = [(obj.vertex_groups[g.group].name, g.weight) for g in v.groups if obj.vertex_groups[g.group].name in rig.data.bones]
                assert all(n in ('Hand.'+side, 'Fingers.'+side, 'Thumb.'+side) for n, w in weights)
                assert abs(sum(w for n, w in weights)-1) < 1e-6
            assert maximum < .01
            bpy.data.meshes.remove(original)
            current_hands[side] = mesh_geometry(obj)
        if hand_geometry is None:
            hand_geometry = current_hands
        assert current_hands == hand_geometry, 'Hand geometry differs between clips'
        head_relative_vertices(rig, bpy.context.evaluated_depsgraph_get())
        grip = None
        if old['active'].startswith('Carry'):
            assert mesh_geometry(bpy.data.objects[BALL]) == football_geometry, 'Preview football geometry differs'
            grip = grip_metrics(rig)
        report['assets'][name] = {'existing_motion_preserved_except_carry_right_arm_rotations': True, 'non_hand_geometry_preserved': True,
            'non_hand_weights_equipment_preserved': True, 'skeleton_bones': len(new['bones']),
            'keyed_range': new['range'], 'fps': new['timing'][2], 'grip': grip}
    report['exports'] = validate_exports(football_positions)
    (PRE / 'validation.json').write_text(json.dumps(report, indent=2))
    carry_report = {'validation_source': '../HandRigPreviews/validation.json',
                    'production_football': 'football.glb', 'skeleton_bones': 24,
                    'assets': {k: v for k, v in report['assets'].items() if 'carry' in k}}
    (OUT / 'CarryPreviews/validation.json').write_text(json.dumps(carry_report, indent=2))
    helmet_report = {'validation_source': 'HandRigPreviews/validation.json',
                     'equipment_geometry_and_weights_unchanged': True,
                     'head_only_export_vertices': report['exports']['helmet_head_only_vertices']}
    (OUT / 'helmet_skinning_validation.json').write_text(json.dumps(helmet_report, indent=2))
    print('HAND_VALIDATION_PASSED', json.dumps(report))


if __name__ == '__main__':
    if globals().get('MODE') == 'capture':
        capture()
    else:
        main()
