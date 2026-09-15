"""Reproducible in-place Juke/Spin clips; run with Blender MCP.

Directions follow rig anatomy: L is +X, R is -X, forward is -Z.
Root is identity throughout. Hips offsets are transient pose motion, net zero.
"""
from pathlib import Path
import sys, math, json, hashlib
sys.dont_write_bytecode = True
import bpy
from mathutils import Vector, Quaternion
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_player_cut_animations import rotate, solve_leg
from upgrade_player_hands import export
from validate_player_hands import code_hashes
OUT = ROOT / 'Assets/Models'
PRE = OUT / 'EvadePreviews'
SOURCE = OUT / 'football_player_carry_run.blend'
CONFIG = {kind+side: dict(kind=kind, direction=1 if side=='Left' else -1,
    plant='R' if side=='Left' else 'L', frames=43 if kind=='Juke' else 97)
    for kind in ('Juke','Spin') for side in ('Left','Right')}

def stem(name):
    return 'football_player_'+name.replace('Left','_left').replace('Right','_right').lower()

def curve(t, keys):
    if t <= keys[0][0]: return keys[0][1]
    for (a,x),(b,y) in zip(keys,keys[1:]):
        if t <= b:
            u=(t-a)/(b-a); u=u*u*(3-2*u)
            return x+(y-x)*u
    return keys[-1][1]

def protected_hashes():
    generated={stem(n)+s for n in CONFIG for s in ('.blend','.glb')}
    return {str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest()
        for p in OUT.rglob('*') if p.suffix in ('.blend','.glb') and p.name not in generated}

def phases(cfg):
    start=7 if cfg['plant']=='R' else 19
    # Preserve run cadence through approach and handoff, quicken it in the evade.
    travel=18 if cfg['kind']=='Juke' else 30
    return [1+(start-1+travel*i/(cfg['frames']-1))%24 for i in range(cfg['frames'])]

def heading(t,cfg):
    if cfg['kind']=='Juke':
        return cfg['direction']*curve(t,[(0,0),(.22,7),(.48,-12),(.72,-6),(1,0)])
    return -cfg['direction']*curve(t,[(0,0),(.15,0),(.26,35),(.43,135),(.59,230),(.76,325),(.9,360),(1,360)])

def contacts(cfg):
    d=cfg['direction']; p=cfg['plant']; other='L' if p=='R' else 'R'
    if cfg['kind']=='Juke':
        return [(p,.04,.14,.35,.44,Vector((-d*.48,.1205,-.24)),0),
                (other,.39,.51,.73,.87,Vector((d*.60,.1205,-.18)),0)]
    result=[]
    for side,a,b,c,e,angle in [(p,.05,.15,.25,.36,0),(other,.30,.43,.54,.65,160),(p,.61,.73,.82,.94,320)]:
        q=Quaternion((0,1,0),math.radians(-d*angle))
        x=.22 if side=='L' else -.22
        result.append((side,a,b,c,e,q@Vector((x,.1205,-.065)),-d*angle))
    return result

def build(name,cfg):
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    bpy.context.preferences.filepaths.save_version=0
    scene=bpy.context.scene; rig=bpy.data.objects['PlayerRig']
    samples=[]
    for f in phases(cfg):
        scene.frame_set(int(f),subframe=f-int(f))
        samples.append(({p.name:(p.location.copy(),p.rotation_quaternion.copy(),p.scale.copy()) for p in rig.pose.bones},
                        {s:rig.pose.bones['Foot.'+s].matrix.copy() for s in ('L','R')}))
    action=bpy.data.actions.new(name); action.use_fake_user=True
    rig.animation_data.action=action
    previous={}
    for i,(basis,feet) in enumerate(samples):
        frame=i+1; t=i/(cfg['frames']-1); d=cfg['direction']; spin=cfg['kind']=='Spin'
        scene.frame_set(frame)
        for p in rig.pose.bones:
            p.location,p.rotation_quaternion,p.scale=basis[p.name]
        yaw=heading(t,cfg); turn=Quaternion((0,1,0),math.radians(yaw))
        # Two distinct compression/extension cycles: brake on the outside
        # foot, drive sideways, absorb on the receiving foot, then drive out.
        drop=curve(t,[(0,0),(.14,.30),(.26,.43),(.35,.41),(.45,.33),(.56,.40),(.65,.37),(.78,.22),(.93,.04),(1,0)])
        if spin: drop=curve(t,[(0,0),(.15,.095),(.30,.12),(.55,.105),(.78,.09),(.94,.015),(1,0)])
        lateral=d*curve(t,[(0,0),(.20,-.085),(.29,-.085),(.48,.24),(.59,.28),(.72,.24),(.9,.065),(1,0)]) if not spin else d*curve(t,[(0,0),(.2,-.035),(.5,.045),(.8,.025),(1,0)])
        rig.pose.bones['Hips'].location.x+=lateral
        rig.pose.bones['Hips'].location.y-=drop
        rotate(rig,'Hips',(0,1,0),yaw)
        lean=d*curve(t,[(0,0),(.22,-9),(.40,18),(.56,10),(.72,5),(1,0)]) if not spin else d*curve(t,[(0,0),(.22,5),(.5,7),(.8,4),(1,0)])
        rotate(rig,'Hips',(0,0,1),-lean)
        rotate(rig,'Spine',(1,0,0),-drop*35)
        if not spin:
            # Sit into the braking leg, then tip into the forward restart.
            pitch=curve(t,[(0,0),(.2,9),(.32,7),(.48,-5),(.63,-10),(.78,-19),(.9,-10),(1,0)])
            rotate(rig,'Hips',(1,0,0),pitch)
            rotate(rig,'Head',(1,0,0),-pitch*.35+drop*12)
        # Chest carries the protected arm unchanged; modest fake, unified spin.
        rotate(rig,'Chest',(0,1,0),0 if spin else d*curve(t,[(0,0),(.2,6),(.46,-7),(.75,-3),(1,0)]))
        balance=curve(t,[(0,0),(.2,1),(.65,1),(1,0)])
        rotate(rig,'UpperArm.L',(0,0,1),18*balance if spin else 12*balance)
        rotate(rig,'UpperArm.L',(1,0,0),-10*balance)
        bpy.context.view_layer.update()
        if i not in (0,cfg['frames']-1):
            for side in ('L','R'):
                original=feet[side]
                target=turn@original.translation
                target.x+=lateral
                orientation=turn@original.to_quaternion()
                if not spin:
                    # Open both legs into a broad, low base instead of tucking
                    # the free leg behind the player during the lateral drive.
                    wide=curve(t,[(0,0),(.14,1),(.70,1),(.90,.25),(1,0)])
                    target.x=(1-wide)*target.x+wide*((.46 if side=='L' else -.46)+lateral)
                    target.y=(1-wide)*target.y+wide*.17
                    orientation=orientation.slerp(rig.data.bones['Foot.'+side].matrix_local.to_quaternion(),wide)
                for s,a,b,c,e,anchor,angle in contacts(cfg):
                    if side==s and a<t<e:
                        w=curve(t,[(a,0),(b,1),(c,1),(e,0)])
                        target=target.lerp(anchor,w)
                        flat=Quaternion((0,1,0),math.radians(angle))@rig.data.bones['Foot.'+side].matrix_local.to_quaternion()
                        orientation=orientation.slerp(flat,w)
                obj=bpy.data.objects['LeftFoot' if side=='L' else 'RightFoot']
                inv=rig.data.bones['Foot.'+side].matrix_local.inverted()
                low=min((orientation@(inv@obj.matrix_world@v.co)).y+target.y for v in obj.data.vertices)
                target.y+=max(0,.0005-low)
                solve_leg(rig,side,target,orientation,yaw)
        for p in rig.pose.bones:
            if p.name in previous and p.rotation_quaternion.dot(previous[p.name])<0:
                p.rotation_quaternion.negate()
            previous[p.name]=p.rotation_quaternion.copy()
            for prop in ('location','rotation_quaternion','scale'):
                p.keyframe_insert(prop,frame=frame,group=p.name)
    for fc in action.fcurves:
        for k in fc.keyframe_points: k.interpolation='LINEAR'
    scene.render.fps=120; scene.render.fps_base=1
    scene.frame_start=1; scene.frame_end=cfg['frames']
    scene.timeline_markers.clear()
    markers=([(0,'CarryRun entry'),(.26,'Brake low / '+cfg['plant']+' plant'),(.38,'Push sideways'),(.56,'Receive low / '+('L' if cfg['plant']=='R' else 'R')+' plant'),(.75,'Push forward / accelerate'),(1,'CarryRun exit')]
             if cfg['kind']=='Juke' else [(0,'CarryRun entry'),(.20,'Opposite leg load'),(.34,'Drive / release'),(.5,'Evade'),(.75,'Recovery step'),(1,'CarryRun exit')])
    for t,label in markers:
        scene.timeline_markers.new(label,frame=round(t*(cfg['frames']-1))+1)
    scene['evade_source']=SOURCE.name
    scene['evade_source_phases']=phases(cfg)
    scene['evade_direction_convention']='Rig L +X, rig R -X, forward -Z; fixed Root'
    scene['evade_plant']=cfg['plant']
    scene['evade_duration_seconds']=(cfg['frames']-1)/120
    action['description']='Deep braking plant, sideways drive, receiving-leg compression, forward acceleration push' if not spin else 'Loaded plant, stepping full revolution, forward run recovery'
    scene.frame_set(1)
    path=OUT/stem(name)
    bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
    export(scene,rig,action,path.with_suffix('.glb'))
    print('EVADE_COMPLETE',name,flush=True)

def main():
    PRE.mkdir(exist_ok=True)
    before={'files':protected_hashes(),'code':code_hashes()}
    (PRE/'source_preservation.json').write_text(json.dumps(before,indent=2))
    for name,cfg in CONFIG.items(): build(name,cfg)
    assert protected_hashes()==before['files']
    assert code_hashes()==before['code']

if __name__=='__main__': main()
