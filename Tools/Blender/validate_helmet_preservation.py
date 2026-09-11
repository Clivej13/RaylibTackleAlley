"""Capture before rebuilding, then compare after rebuilding via Blender MCP.

runpy.run_path(..., init_globals={'MODE': 'capture'}) captures the approved files.
Normal execution compares against that captured baseline. No blend is modified.
"""
from pathlib import Path
import bpy
import gzip
import hashlib
import json
import sys
from mathutils import Vector
sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_helmet_skinning import EQUIPMENT, head_relative_vertices, validate_glb

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
BASE = ROOT / 'Tools/Blender/helmet_preservation_baseline.json.gz'


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def matrix(m):
    return [list(row) for row in m]


def properties(item):
    result = {}
    for prop in item.bl_rna.properties:
        if prop.identifier in {'rna_type', 'execution_time'} or prop.type in {'POINTER', 'COLLECTION'}:
            continue
        value = getattr(item, prop.identifier)
        result[prop.identifier] = list(value) if getattr(prop, 'is_array', False) else value
    return result


def snapshot():
    rig = bpy.data.objects['PlayerRig']
    scene = bpy.context.scene
    meshes = {}
    for o in scene.objects:
        if o.type != 'MESH':
            continue
        geometry = ([list(v.co) for v in o.data.vertices],
                    [list(p.vertices) for p in o.data.polygons],
                    [(p.material_index, p.use_smooth) for p in o.data.polygons],
                    [m.name if m else None for m in o.data.materials])
        entry = {'geometry': digest(geometry)}
        if o.name not in EQUIPMENT:
            entry['rigging'] = digest((
                [g.name for g in o.vertex_groups],
                [[(g.group, g.weight) for g in v.groups] for v in o.data.vertices],
                o.parent.name if o.parent else None, o.parent_type, o.parent_bone,
                matrix(o.matrix_basis), matrix(o.matrix_parent_inverse),
                [properties(m) for m in o.modifiers]))
        meshes[o.name] = entry
    bones = [(b.name, b.parent.name if b.parent else None, list(b.head_local),
              list(b.tail_local), matrix(b.matrix_local), b.use_deform, b.use_connect)
             for b in rig.data.bones]
    actions = {}
    for a in bpy.data.actions:
        actions[a.name] = digest((list(a.frame_range), [
            (f.data_path, f.array_index, f.extrapolation,
             [properties(k) for k in f.keyframe_points],
             [properties(m) for m in f.modifiers]) for f in a.fcurves]))
    timing = (scene.render.fps, scene.render.fps_base, scene.frame_start, scene.frame_end,
              [(m.name, m.frame) for m in scene.timeline_markers])
    positions = {}
    active = rig.animation_data.action
    # All inspection poses and quarter-frame locomotion samples, including endpoints.
    selected = [active] if active.name in {'Jog', 'Run', 'Sprint'} else list(bpy.data.actions)
    for a in selected:
        rig.animation_data.action = a
        samples = {}
        start, end = map(int, a.frame_range)
        for tick in range(start * 4, end * 4 + 1):
            scene.frame_set(tick // 4, subframe=(tick % 4) / 4)
            dg = bpy.context.evaluated_depsgraph_get()
            if globals().get('MODE') != 'capture':
                head_relative_vertices(rig, dg)
            samples[str(tick)] = {
                name: [list(o.matrix_world @ v.co) for v in o.data.vertices]
                for name in EQUIPMENT
                for o in [bpy.data.objects[name].evaluated_get(dg)]}
        positions[a.name] = samples
    return {'meshes': meshes, 'skeleton': digest(bones), 'actions': actions,
            'timing': timing, 'helmet_positions': positions}


def main():
    if (ROOT / 'Tools/Blender/hand_rig_baseline.json.gz').exists() and globals().get('MODE') != 'capture':
        from validate_player_hands import main as validate_hands
        return validate_hands()
    current = {}
    for suffix in ('rigged', 'jog', 'run', 'sprint'):
        bpy.ops.wm.open_mainfile(filepath=str(OUT / ('lowpoly_human_' + suffix + '.blend')))
        current[suffix] = snapshot()
    current['csharp'] = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                         for p in ROOT.rglob('*.cs')}
    if globals().get('MODE') == 'capture':
        BASE.write_bytes(gzip.compress(json.dumps(current).encode(), mtime=0))
        print('HELMET_BASELINE_CAPTURED', BASE)
        return
    before = json.loads(gzip.decompress(BASE.read_bytes()))
    def source_files(files):
        return {p: h for p, h in files.items()
                if not {'obj', 'bin'} & set(Path(p).parts)}
    assert source_files(current['csharp']) == source_files(before['csharp']), 'C# source files changed'
    report = {'csharp_unchanged': True, 'assets': {}, 'glb_head_skinned_vertex_counts': {}}
    for suffix in ('rigged', 'jog', 'run', 'sprint'):
        old, new = before[suffix], current[suffix]
        for key in ('meshes', 'skeleton', 'actions', 'timing'):
            assert digest(old[key]) == digest(new[key]), (suffix, key)
        error = 0.0
        for action, samples in old['helmet_positions'].items():
            for tick, meshes in samples.items():
                for name, coords in meshes.items():
                    actual = new['helmet_positions'][action][tick][name]
                    assert len(coords) == len(actual)
                    error = max(error, max((Vector(a) - Vector(b)).length for a, b in zip(coords, actual)))
        assert error < 1e-5, (suffix, error)
        report['assets'][suffix] = {'geometry_body_weights_pads_skeleton_actions_timing_unchanged': True,
                                    'max_helmet_world_vertex_error_m': error,
                                    'action_hashes': new['actions']}
    for filename in ('football_player.glb', 'lowpoly_human_jog_validation.glb',
                     'lowpoly_human_run_validation.glb', 'lowpoly_human_sprint_validation.glb'):
        report['glb_head_skinned_vertex_counts'][filename] = validate_glb(OUT / filename)
    (OUT / 'helmet_skinning_validation.json').write_text(json.dumps(report, indent=2))
    print('HELMET_PRESERVATION_PASSED', json.dumps(report))


if __name__ == '__main__':
    main()
