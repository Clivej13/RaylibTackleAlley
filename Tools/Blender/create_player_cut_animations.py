"""Author in-place football cuts from CarryRun using Blender IK, then bake/export."""
from pathlib import Path
import hashlib
import json
import math
import sys
sys.dont_write_bytecode = True
import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from upgrade_player_hands import export
from validate_player_hands import code_hashes

OUT = ROOT / 'Assets/Models'
PRE = OUT / 'CutPreviews'
SOURCE = OUT / 'football_player_carry_run.blend'
CONFIG = {
    # Clip directions use the in-game rear view; bone L/R names stay unchanged.
    'CutLeft': {'plant': 'L', 'direction': -1, 'phase': [19,20,21,22,23,24,2,4,6,8,9,10],
        'anchor': [.29,.1205,-.11], 'foot_yaw': -10,
        'lean': [0,-4,-9,-9,-4,5,12,11,6,2,0,0],
        'yaw': [0,-4,-9,-8,-2,10,17,15,8,3,0,0],
        'drop': [0,.025,.055,.080,.100,.090,.060,.030,.010,0,0,0], 'drive': .09},
    'CutRight': {'plant': 'R', 'direction': 1, 'phase': [7,8,9,10,11,12,14,16,18,20,21,22],
        'anchor': [-.28,.1205,-.10], 'foot_yaw': 8,
        'lean': [0,3.5,8,8,3,-5,-11,-10,-5,-1.5,0,0],
        'yaw': [0,3,8,7,1,-9,-16,-14,-7,-2,0,0],
        'drop': [0,.022,.052,.077,.095,.085,.055,.026,.008,0,0,0], 'drive': .08},
}
# Heading relative to incoming run, positive yaw turns left in the rear view.
TURN = [0,3,8,15,25,38,50,64,76,84,89,90]
CHEST_LEAD = [0,4,8,12,14,13,11,8,5,2,0,0]
HEAD_LEAD = [0,6,9,10,10,9,7,5,3,1,0,0]

PLANT_WEIGHT = [0,.15,.70,1,1,1,1,.65,.25,0,0,0]
DRIVE = [0,0,0,0,.2,.55,1,.85,.45,.12,0,0]
PLANT_LIFT = [0,.035,.015,0,0,0,0,.065,.045,0,0,0]


def sample_curve(values, time):
    i = min(int(time), len(values)-2)
    return values[i] + (values[i+1]-values[i])*(time-i)


def source_phases(cfg):
    unwrapped = []
    for phase in cfg['phase']:
        while unwrapped and phase < unwrapped[-1]:
            phase += 24
        unwrapped.append(phase)
    return [1+(sample_curve(unwrapped, i/2)-1)%24 for i in range(23)]


def protected_hashes():
    paths = [p for p in OUT.rglob('*') if p.suffix in ('.blend', '.glb') and '_cut_' not in p.name]
    return {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}


def rotate(rig, name, axis, degrees):
    local = rig.data.bones[name].matrix_local.to_3x3().inverted() @ Vector(axis)
    p = rig.pose.bones[name]
    p.rotation_quaternion = Quaternion(local, math.radians(degrees)) @ p.rotation_quaternion


def solve_leg(rig, side, target, orientation, heading=0):
    """Use Blender's two-bone IK; bake only rotations and discard authoring controls."""
    scene = bpy.context.scene
    lower = rig.pose.bones['LowerLeg.' + side]
    upper = rig.pose.bones['UpperLeg.' + side]
    foot = rig.pose.bones['Foot.' + side]
    goal = bpy.data.objects.new('_CutIKTarget', None)
    pole = bpy.data.objects.new('_CutIKPole', None)
    scene.collection.objects.link(goal)
    scene.collection.objects.link(pole)
    goal.location = target
    pole.location = target + Quaternion((0,1,0), math.radians(heading)) @ Vector((0,.4,-1.2))
    c = lower.constraints.new('IK')
    c.target, c.pole_target = goal, pole
    c.pole_angle = -math.pi/2
    c.chain_count = 2
    c.use_stretch = False
    c.iterations = 200
    bpy.context.view_layer.update()
    solved = [upper.matrix.copy(), lower.matrix.copy()]
    lower.constraints.remove(c)
    bpy.context.view_layer.update()
    for p, matrix in zip((upper, lower), solved):
        p.matrix = matrix
        p.location = (0, 0, 0)
        p.scale = (1, 1, 1)
        bpy.context.view_layer.update()
    desired = orientation.to_matrix().to_4x4()
    desired.translation = foot.head
    foot.matrix = desired
    foot.location = (0, 0, 0)
    foot.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    assert (foot.head-target).length < .0001, (side, 'IK reach', list(foot.head), list(target))
    bpy.data.objects.remove(goal, do_unlink=True)
    bpy.data.objects.remove(pole, do_unlink=True)


def build(name, cfg):
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene
    rig = bpy.data.objects['PlayerRig']
    source = rig.animation_data.action
    samples = []
    phases = source_phases(cfg)
    for phase in phases:
        scene.frame_set(int(phase), subframe=phase-int(phase))
        samples.append({'basis': {p.name: (p.location.copy(), p.rotation_quaternion.copy(), p.scale.copy()) for p in rig.pose.bones},
                        'feet': {s: rig.pose.bones['Foot.'+s].matrix.copy() for s in ('L','R')}})
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action['description'] = 'Relative 0-to-90-degree cut with staggered head/chest/pelvis turn; ' + cfg['plant'] + ' plant; release-Sprint transition'
    rig.animation_data.action = action
    for i, sample in enumerate(samples):
        frame = i+1
        time = i/2
        drive = sample_curve(DRIVE, time)
        drop = sample_curve(cfg['drop'], time)
        scene.frame_set(frame)
        for p in rig.pose.bones:
            loc, quat, scale = sample['basis'][p.name]
            p.location, p.rotation_quaternion, p.scale = loc, quat, scale
        heading = -cfg['direction']*sample_curve(TURN, time)
        turn = Quaternion((0,1,0), math.radians(heading))
        rotate(rig, 'Hips', (0,0,1), sample_curve(cfg['lean'], time))
        rotate(rig, 'Root', (0,1,0), heading)
        rig.pose.bones['Hips'].location.y -= drop
        rotate(rig, 'Spine', (1,0,0), -drop*50)
        rotate(rig, 'Chest', (0,1,0), -cfg['direction']*sample_curve(CHEST_LEAD, time))
        rotate(rig, 'Head', (0,1,0), -cfg['direction']*sample_curve(HEAD_LEAD, time))
        rotate(rig, 'UpperArm.L', (1,0,0), -12*drive)
        bpy.context.view_layer.update()
        if 0 < time < 10:
            for side in ('L','R'):
                original = sample['feet'][side]
                target = turn @ original.translation
                orientation = turn @ original.to_quaternion()
                if side == cfg['plant']:
                    weight = sample_curve(PLANT_WEIGHT, time)
                    target = target.lerp(Vector(cfg['anchor']), weight)
                    target.y += sample_curve(PLANT_LIFT, time)
                    flat = Quaternion((0,1,0), math.radians(cfg['foot_yaw'])) @ rig.data.bones['Foot.'+side].matrix_local.to_quaternion()
                    orientation = orientation.slerp(flat, weight)
                else:
                    target.x += cfg['direction']*cfg['drive']*drive
                    target.y += .025*drive
                    target.z -= .035*drive
                # Keep the actual rigid foot surface clear, not just the IK ankle.
                obj = bpy.data.objects['LeftFoot' if side == 'L' else 'RightFoot']
                rest_inv = rig.data.bones['Foot.'+side].matrix_local.inverted()
                low = min((orientation @ (rest_inv @ obj.matrix_world @ v.co)).y + target.y for v in obj.data.vertices)
                target.y += max(0, .0005-low)
                solve_leg(rig, side, target, orientation, heading)
        for p in rig.pose.bones:
            for prop in ('location', 'rotation_quaternion', 'scale'):
                p.keyframe_insert(prop, frame=frame, group=p.name)
    for fc in action.fcurves:
        for k in fc.keyframe_points:
            k.interpolation = 'LINEAR'
    # A 60 Hz bake limits FK interpolation drift between the locked IK samples.
    scene.render.fps = 60
    scene.render.fps_base = 1
    scene.frame_start, scene.frame_end = 1, 23
    scene.timeline_markers.clear()
    for frame, label in [(1,'CarryRun approach'),(3,'Load'),(4,'Plant locked'),(6,'Redirect'),(7,'Push'),(9,'Drive'),(12,'CarryRun recovery')]:
        scene.timeline_markers.new(label, frame=2*frame-1)
    scene['cut_source'] = SOURCE.name
    scene['cut_source_phase_frames'] = phases
    scene['cut_main_plant_foot'] = 'Foot.'+cfg['plant']
    scene['cut_plant_locked_frames'] = [7,13]
    scene['cut_duration_seconds'] = 11/30
    scene['cut_notes'] = 'Root yaw only, no root translation or horizontal Hips motion. Temporary IK baked to existing bones. Production ball preview-only.'
    scene.frame_set(1)
    path = OUT / ('football_player_' + name.replace('Cut','cut_').lower())
    bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
    export(scene, rig, action, path.with_suffix('.glb'))
    print('CUT_COMPLETE', name)


def main():
    PRE.mkdir(exist_ok=True)
    before = {'files': protected_hashes(), 'code': code_hashes()}
    for name, cfg in CONFIG.items():
        build(name, cfg)
    assert protected_hashes() == before['files'], 'Existing model/animation asset changed'
    assert code_hashes() == before['code'], 'C# or root JSON changed'
    (PRE / 'source_preservation.json').write_text(json.dumps(before, indent=2))


if __name__ == '__main__':
    main()
