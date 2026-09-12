"""Validate the saved one-shot cuts against CarryRun and the canonical player."""
from pathlib import Path
import json
import sys
sys.dont_write_bytecode = True
import bpy
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_player_cut_animations import CONFIG, OUT, PRE, SOURCE, protected_hashes, source_phases
from validate_player_hands import snapshot, digest, code_hashes, glb, grip_metrics, mesh_geometry
from validate_helmet_skinning import validate_glb
from validate_inside_carry import relationship


def skeleton(doc, read):
    assert len(doc['skins']) == 1
    skin = doc['skins'][0]
    parents = {c:i for i,n in enumerate(doc['nodes']) for c in n.get('children', [])}
    return ([doc['nodes'][i]['name'] for i in skin['joints']],
            [doc['nodes'][parents[i]]['name'] for i in skin['joints']], read(skin['inverseBindMatrices']))


def pose(rig):
    return {p.name: [list(p.location), list(p.rotation_quaternion), list(p.scale)] for p in rig.pose.bones}


def main():
    before = json.loads((PRE / 'source_preservation.json').read_text())
    assert protected_hashes() == before['files'], 'An approved asset changed'
    assert code_hashes() == before['code'], 'C# or root JSON changed'
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    reference = snapshot()
    rig = bpy.data.objects['PlayerRig']
    source_poses = {}
    for f in sorted({p for cfg in CONFIG.values() for p in source_phases(cfg)}):
        bpy.context.scene.frame_set(int(f), subframe=f-int(f))
        source_poses[f] = pose(rig)
    ball_geometry = mesh_geometry(bpy.data.objects['FootballPreview'])
    canonical = skeleton(*glb(OUT / 'football_player.glb'))
    report = {'approved_assets_byte_identical': True, 'code_and_root_json_unchanged': True, 'clips': {}}
    for name, cfg in CONFIG.items():
        path = OUT / ('football_player_' + name.replace('Cut','cut_').lower())
        bpy.ops.wm.open_mainfile(filepath=str(path.with_suffix('.blend')))
        rig = bpy.data.objects['PlayerRig']
        scene = bpy.context.scene
        current = snapshot()
        assert digest(current['bones']) == digest(reference['bones'])
        assert current['meshes'] == reference['meshes'], 'Geometry, weights or equipment changed'
        for action, curves in reference['actions'].items():
            assert current['actions'][action] == curves, ('Original action changed', action)
        assert set(current['actions']) == set(reference['actions']) | {name}
        assert current['active'] == name and current['range'] == [1,23]
        assert scene.render.fps == 60 and scene.render.fps_base == 1
        assert not any(p.constraints for p in rig.pose.bones)
        assert not any(o.name.startswith('_Cut') for o in scene.objects)
        assert mesh_geometry(bpy.data.objects['FootballPreview']) == ball_geometry
        max_endpoint_error = 0.0
        phases = source_phases(cfg)
        for f in (1,):
            scene.frame_set(f)
            expected = source_poses[phases[f-1]]
            max_endpoint_error = max(max_endpoint_error, max(abs(a-b) for n,v in pose(rig).items()
                for xs,ys in zip(v,expected[n]) for a,b in zip(xs,ys)))
        assert max_endpoint_error < 1e-6, ('CarryRun entry pose changed', max_endpoint_error)
        baseline = json.loads((PRE / 'exit_refinement_before.json').read_text())[name.replace('Cut','').lower()]
        for f in range(1,14):
            scene.frame_set(f)
            assert pose(rig) == baseline[str(f)], ('Early/push pose changed', name, f)
        for f, phase in enumerate(phases, 1):
            scene.frame_set(f)
            current_pose = pose(rig)
            for bone in ('Clavicle.R','UpperArm.R','LowerArm.R','Hand.R','Fingers.R','Thumb.R','Fingers.L','Thumb.L'):
                assert current_pose[bone] == source_poses[phase][bone], (name, bone, 'grip pose changed')
        foot_name = 'LeftFoot' if cfg['plant'] == 'L' else 'RightFoot'
        locked = None
        slip = 0.0
        ground_min, ground_max = float('inf'), -float('inf')
        knee_angles = []
        for tick in range(4,93):
            scene.frame_set(tick//4, subframe=tick%4/4)
            assert rig.matrix_world == Matrix.Identity(4)
            assert rig.pose.bones['Root'].matrix_basis == Matrix.Identity(4)
            hips = rig.pose.bones['Hips']
            assert abs(hips.location.x)+abs(hips.location.z) < 1e-8
            for p in rig.pose.bones:
                assert max(abs(v-1) for v in p.scale) < 1e-6
                if p.name != 'Hips':
                    assert p.location.length < 1e-6
            dg = bpy.context.evaluated_depsgraph_get()
            feet = {n: [o.matrix_world @ v.co for o in [bpy.data.objects[n].evaluated_get(dg)] for v in o.data.vertices]
                    for n in ('LeftFoot','RightFoot')}
            ground_min = min(ground_min, min(v.y for vs in feet.values() for v in vs))
            if 28 <= tick <= 52:
                points = feet[foot_name]
                if locked is None:
                    locked = [v.copy() for v in points]
                slip = max(slip, max((a-b).length for a,b in zip(locked,points)))
                ground_max = max(ground_max, min(v.y for v in points))
                upper, lower = [rig.pose.bones[n+'.'+cfg['plant']] for n in ('UpperLeg','LowerLeg')]
                knee_angles.append((upper.tail-upper.head).angle(lower.tail-lower.head)*180/3.141592653589793)
        assert slip < .0015, ('Plant skate', name, slip)
        assert ground_min > -.002, ('Foot penetration', name, ground_min)
        assert ground_max < .003, ('Plant hovering', name, ground_max)
        assert max(knee_angles) > 45, ('Insufficient load flexion', knee_angles)
        grip = grip_metrics(rig, require_loop=False)
        inside = relationship()
        doc, read = glb(path.with_suffix('.glb'))
        assert skeleton(doc, read) == canonical
        assert len(doc['animations']) == 1 and doc['animations'][0]['name'] == name
        assert not any(n.get('name') in ('Football','FootballPreview','TEMP_PreviewFootball_DO_NOT_EXPORT') or n.get('name','').startswith('_Cut') for n in doc['nodes'])
        times = [v[0] for s in doc['animations'][0]['samplers'] for v in read(s['input'])]
        assert abs(max(times)-min(times)-11/30) < 1e-6
        for channel in doc['animations'][0]['channels']:
            target = channel['target']
            bone = doc['nodes'][target['node']]['name']
            sampler = doc['animations'][0]['samplers'][channel['sampler']]
            values = read(sampler['output'])
            if bone in canonical[0]:
                assert len(read(sampler['input'])) == 23, ('Missing 60 Hz bake keys', bone)
            if bone == 'Root':
                assert all(v == values[0] for v in values), ('Exported root motion', target['path'])
            if bone == 'Hips' and target['path'] == 'translation':
                assert all(v[0] == values[0][0] and v[2] == values[0][2] for v in values)
        helmet = validate_glb(path.with_suffix('.glb'))
        report['clips'][name] = {'duration_seconds': 11/30, 'keyed_frames': [1,23], 'fps':60,
            'main_plant_foot': 'Foot.'+cfg['plant'], 'locked_plant_frames':[7,13],
            'maximum_plant_vertex_drift_m':slip, 'minimum_foot_height_m':ground_min,
            'maximum_planted_sole_height_m':ground_max, 'plant_knee_flexion_degrees': [min(knee_angles),max(knee_angles)],
            'source_phases':phases, 'entry_pose_error':max_endpoint_error,
            'frames_1_through_13_unchanged':True, 'exit_preview_frames':[17,23],
            'right_arm_and_finger_pose_data_exact':True, 'geometry_weights_rest_skeleton_exact':True,
            'canonical_joint_count':len(canonical[0]), 'no_root_or_horizontal_hips_motion':True,
            'helmet_head_only_export_vertices':helmet, 'grip':grip, 'inside_relationship':inside}
    (PRE / 'validation.json').write_text(json.dumps(report, indent=2))
    print('CUT_VALIDATION_PASSED', json.dumps(report))


if __name__ == '__main__':
    main()
