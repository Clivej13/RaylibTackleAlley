"""Preservation baseline and regression checks for the inside-arm carry correction."""
from pathlib import Path
import gzip
import hashlib
import json
import math
import sys
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_player_hands import snapshot, code_hashes, OUT, digest

BASE = Path(__file__).with_name('inside_carry_baseline.json.gz')
NAMES = ('jog', 'run', 'sprint')


def protected_hashes():
    names = ['football.blend', 'football.glb', 'football_player.glb', 'lowpoly_human_rigged.blend']
    names += ['lowpoly_human_' + n + suffix for n in NAMES for suffix in ('.blend', '_validation.glb')]
    return {n: hashlib.sha256((OUT / n).read_bytes()).hexdigest() for n in names}


def capture():
    assert not BASE.exists(), 'Do not replace the pre-correction baseline'
    result = {'code': code_hashes(), 'protected_files': protected_hashes(), 'assets': {}}
    for name in NAMES:
        bpy.ops.wm.open_mainfile(filepath=str(OUT / ('football_player_carry_' + name + '.blend')))
        result['assets'][name] = snapshot()
    BASE.write_bytes(gzip.compress(json.dumps(result).encode(), mtime=0))
    print('INSIDE_CARRY_BASELINE_CAPTURED')


def relationship():
    rig = bpy.data.objects['PlayerRig']
    ball = bpy.data.objects['FootballPreview']
    scene = bpy.context.scene
    start, end = map(int, rig.animation_data.action.frame_range)
    samples = []
    for tick in range(start*4, end*4+1):
        scene.frame_set(tick//4, subframe=tick%4/4)
        inv = (rig.pose.bones['Chest'].matrix @ rig.data.bones['Chest'].matrix_local.inverted()).inverted()
        center = inv @ ball.matrix_world.translation
        lower = rig.pose.bones['LowerArm.R']
        arm = inv @ ((lower.head+lower.tail)*.5)
        axis = inv.to_3x3() @ ball.matrix_world.to_3x3() @ Vector((0, 0, 1))
        if axis.z > 0:
            axis = -axis
        tilt = math.degrees(math.atan2(axis.y, math.hypot(axis.x, axis.z)))
        tree = BVHTree.FromPolygons([ball.matrix_world @ v.co for v in ball.data.vertices],
            [list(p.vertices) for p in ball.data.polygons if p.material_index == 0])
        dg = bpy.context.evaluated_depsgraph_get()
        gap = float('inf')
        for name in ('RightChest', 'ShoulderPads'):
            obj = bpy.data.objects[name].evaluated_get(dg)
            points = [v.co for v in obj.data.vertices] + [p.center for p in obj.data.polygons]
            points += [(obj.data.vertices[e.vertices[0]].co + obj.data.vertices[e.vertices[1]].co)*.5 for e in obj.data.edges]
            gap = min(gap, min(tree.find_nearest(obj.matrix_world @ p)[3] for p in points))
        assert center.x-arm.x > .075, ('ball not inside forearm', tick, center.x-arm.x)
        assert 1.28 < center.y < 1.36, ('ball not high against ribs', tick, center.y)
        assert 15 < tilt < 50, ('long axis not slightly upward', tick, tilt)
        assert gap < .025, ('excessive torso gap', tick, gap)
        samples.append((center.x-arm.x, center.y, tilt, gap))
    return {'quarter_frame_samples': len(samples),
        'minimum_ball_inward_of_forearm_m': min(p[0] for p in samples),
        'ball_center_chest_rest_height_m': [min(p[1] for p in samples), max(p[1] for p in samples)],
        'football_upward_tilt_degrees': [min(p[2] for p in samples), max(p[2] for p in samples)],
        'maximum_torso_pad_surface_gap_m': max(p[3] for p in samples)}


def main():
    before = json.loads(gzip.decompress(BASE.read_bytes()))
    assert code_hashes() == before['code'], 'Code or root JSON changed during correction'
    assert protected_hashes() == before['protected_files'], 'A normal/player/football asset changed'
    report = {'code_unchanged': True, 'normal_and_standalone_assets_byte_identical': True, 'clips': {}}
    for name in NAMES:
        bpy.ops.wm.open_mainfile(filepath=str(OUT / ('football_player_carry_' + name + '.blend')))
        old = before['assets'][name]
        new = snapshot()
        for key in ('meshes', 'bones', 'range', 'timing'):
            assert digest(new[key]) == digest(old[key]), (name, key)
        changed = []
        for action, curves in old['actions'].items():
            assert set(new['actions'][action]) == set(curves)
            for key, value in curves.items():
                if new['actions'][action][key] != value:
                    assert action.startswith('Carry') and 'rotation_quaternion' in key
                    assert any('"' + n + '"' in key for n in ('UpperArm.R', 'LowerArm.R', 'Hand.R')), key
                    changed.append(key)
        for a, b in zip(old['samples'], new['samples']):
            for bone, value in a.items():
                if bone not in ('UpperArm.R', 'LowerArm.R', 'Hand.R', 'Fingers.R', 'Thumb.R'):
                    assert b[bone] == value, (name, bone, 'pose')
        report['clips'][name] = {'changed_curves': changed, 'geometry_weights_rest_pose_exact': True,
            'other_curves_and_lower_body_exact': True, 'range': new['range'], 'timing': new['timing'],
            'inside_relationship': relationship()}
    (OUT / 'CarryPreviews/inside_carry_validation.json').write_text(json.dumps(report, indent=2))
    print('INSIDE_CARRY_PRESERVATION_PASSED')


if __name__ == '__main__':
    capture() if globals().get('MODE') == 'capture' else main()
