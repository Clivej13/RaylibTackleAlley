"""Author the Y-up, ball-free LungeLand -> Down -> GetUp chain via Blender MCP.

Rebuild: run this file with Blender MCP run_blender_script. Source assets are
read-only; each output has one baked 60 fps action and an identity Root bone.
"""
from pathlib import Path
import sys, math, json, struct
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector, Quaternion, Matrix
from upgrade_player_hands import export

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
PRE = OUT / 'DefenderRecoveryPreviews'
STEMS = {'LungeLand':'lunge_land', 'Down':'down', 'GetUp':'get_up'}

def snapshot(r):
    return {p.name:(p.location.copy(), p.rotation_quaternion.copy(), p.scale.copy()) for p in r.pose.bones}

def apply(r, pose):
    for p in r.pose.bones:
        p.rotation_mode = 'QUATERNION'
        p.location, p.rotation_quaternion, p.scale = pose[p.name]
    bpy.context.view_layer.update()

def blend(a,b,t):
    t=t*t*(3-2*t)
    return {n:(a[n][0].lerp(b[n][0],t),a[n][1].slerp(b[n][1],t),a[n][2].lerp(b[n][2],t)) for n in a}

def orient(r,name,direction):
    p=r.pose.bones[name];rest=p.bone
    q=(rest.tail_local-rest.head_local).rotation_difference(Vector(direction))
    m=(q.to_matrix() @ rest.matrix_local.to_3x3()).to_4x4()
    m.translation=p.head;p.matrix=m;p.location=(0,0,0);p.scale=(1,1,1)
    bpy.context.view_layer.update()

def limb(r,upper,lower,target,pole):
    a=r.pose.bones[upper];b=r.pose.bones[lower]
    start=a.head.copy();delta=Vector(target)-start
    d=min(max(delta.length,.02),a.bone.length+b.bone.length-.0001)
    axis=delta.normalized();v=Vector(pole);v=(v-axis*v.dot(axis)).normalized()
    x=(a.bone.length**2-b.bone.length**2+d*d)/(2*d)
    knee=start+axis*x+v*math.sqrt(max(0,a.bone.length**2-x*x))
    orient(r,upper,knee-start);orient(r,lower,start+axis*d-knee)

def floor(r):
    bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get()
    return min((o.matrix_world@v.co).y for src in bpy.context.scene.objects if src.type=='MESH'
               for o in [src.evaluated_get(dg)] for v in o.data.vertices)

def ground(r):
    low=floor(r)
    if low<.003:
        r.pose.bones['Hips'].location.y += .003-low
        bpy.context.view_layer.update()

def pose(r,height,z,pitch,head,arms,feet=None):
    for p in r.pose.bones:
        p.rotation_mode='QUATERNION';p.location=(0,0,0);p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)
    r.pose.bones['Hips'].location=(0,height-r.data.bones['Hips'].head_local.y,z-.01)
    r.pose.bones['Hips'].rotation_quaternion=Quaternion((1,0,0),math.radians(pitch))
    r.pose.bones['Head'].rotation_quaternion=Quaternion((1,0,0),math.radians(head))
    bpy.context.view_layer.update()
    for side,sign in [('L',1),('R',-1)]:
        if feet:
            target=feet[side]
            limb(r,'UpperLeg.'+side,'LowerLeg.'+side,target,(sign*.12,0,-1))
            orient(r,'Foot.'+side,(0,-.085,-.185))
        else:
            orient(r,'UpperLeg.'+side,(sign*.10,-.08,1))
            orient(r,'LowerLeg.'+side,(sign*.025,-.045,1))
            orient(r,'Foot.'+side,(0,-.04,1))
        if arms=='prone':
            orient(r,'UpperArm.'+side,(sign*.72,-.15,.65))
            orient(r,'LowerArm.'+side,(-sign*.12,-.13,-1))
            orient(r,'Hand.'+side,(0,-.08,-1))
        elif arms=='stand':
            orient(r,'UpperArm.'+side,(sign*.25,-1,.02))
            orient(r,'LowerArm.'+side,(sign*.06,-.95,-.28))
            orient(r,'Hand.'+side,(0,-1,-.15))
        else:
            target=(sign*.39,.085,-1.13)
            limb(r,'UpperArm.'+side,'LowerArm.'+side,target,(sign,0,.15))
            orient(r,'Hand.'+side,(0,-.08,-1))
    ground(r)
    return snapshot(r)

def main():
    PRE.mkdir(parents=True,exist_ok=True)
    source=OUT/'football_player_lunge_tackle_forward.blend'
    bpy.ops.wm.open_mainfile(filepath=str(source))
    s=bpy.context.scene;r=bpy.data.objects['PlayerRig'];s.frame_set(37)
    launch=snapshot(r);r.animation_data.action=None
    prone=pose(r,.205,-.67,-88,23,'prone')
    r.pose.bones['Hips'].location.y += .003-floor(r)
    bpy.context.view_layer.update();prone=snapshot(r)
    apply(r,prone)
    for side,sign in [('L',1),('R',-1)]:
        limb(r,'UpperArm.'+side,'LowerArm.'+side,(sign*.39,.085,-1.13),(sign,0,.15))
        orient(r,'Hand.'+side,(0,-.08,-1))
    ground(r);brace=snapshot(r)
    impact=pose(r,.215,-.64,-94,29,'prone')
    rebound=pose(r,.25,-.66,-86,28,'prone')
    push=pose(r,.44,-.65,-76,35,'push',{'L':(.19,.13,-.02),'R':(-.19,.13,-.02)})
    kneel=pose(r,.49,-.55,-42,28,'push',{'L':(.21,.13,-.85),'R':(-.22,.13,-.04)})
    squat=pose(r,.64,-.50,-25,20,'stand',{'L':(.21,.121,-.78),'R':(-.22,.121,-.35)})
    stand=pose(r,.94,-.50,0,0,'stand',{'L':(.19,.121,-.56),'R':(-.19,.121,-.43)})
    clips={'LungeLand':[(1,launch),(17,impact),(23,rebound),(37,prone)],
           'Down':[(1,prone),(61,prone)],
           'GetUp':[(1,prone),(15,brace),(35,push),(57,kneel),(77,squat),(101,stand)]}
    labels={'LungeLand':[(1,'Airborne'),(17,'Impact'),(23,'Rebound'),(37,'Prone')],
            'Down':[(1,'Prone'),(31,'Breath'),(61,'Loop')],
            'GetUp':[(1,'Prone'),(15,'Brace'),(35,'Push'),(57,'HalfKneel'),(77,'Rise'),(101,'Standing')]}
    report={}
    for name,keys in clips.items():
        bpy.ops.wm.open_mainfile(filepath=str(source));s=bpy.context.scene;r=bpy.data.objects['PlayerRig']
        bpy.context.preferences.filepaths.save_version=0
        action=bpy.data.actions.new(name);action.use_fake_user=True;r.animation_data.action=action
        s.frame_start=1;s.frame_end=keys[-1][0];s.render.fps=60;s.render.fps_base=1
        s.timeline_markers.clear()
        for f,label in labels[name]:s.timeline_markers.new(label,frame=f)
        previous={}
        for f in range(1,s.frame_end+1):
            s.frame_set(f)
            a,b=next((a,b) for a,b in zip(keys,keys[1:]) if a[0]<=f<=b[0])
            apply(r,blend(a[1],b[1],(f-a[0])/(b[0]-a[0])))
            if name=='Down':
                r.pose.bones['Spine'].rotation_quaternion=Quaternion((1,0,0),math.radians(.6*math.sin(math.pi*(f-1)/60)**2))
            if name=='GetUp' and 15<=f<=35:
                for side,sign in [('L',1),('R',-1)]:
                    limb(r,'UpperArm.'+side,'LowerArm.'+side,(sign*.39,.085,-1.13),(sign,0,.15))
                    orient(r,'Hand.'+side,(0,-.08,-1))
            ground(r)
            for p in r.pose.bones:
                if p.name in previous and p.rotation_quaternion.dot(previous[p.name])<0:p.rotation_quaternion.negate()
                previous[p.name]=p.rotation_quaternion.copy()
                for prop in ['location','rotation_quaternion','scale']:p.keyframe_insert(prop,frame=f,group=p.name)
        for fc in action.fcurves:
            for k in fc.keyframe_points:k.interpolation='LINEAR'
        action['loop']=name=='Down'
        action['description']='Defender recovery chain; forward lunge entry, prone contact, hand-supported push and half-kneeling rise. Y up, -Z forward. Root identity; local pelvis displacement retained.'
        s.render.engine='BLENDER_EEVEE';s.eevee.taa_render_samples=24
        s.render.resolution_x=640;s.render.resolution_y=480;s.render.resolution_percentage=100
        s.render.image_settings.file_format='PNG'
        for view in ['Side','FrontThreeQuarter']:
            camera=bpy.data.objects[view];camera.data.ortho_scale=3.3
            forward=(Vector((0,.95,-.55))-camera.location).normalized()
            right=forward.cross(Vector((0,1,0))).normalized();up=right.cross(forward)
            camera.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
        s.camera=bpy.data.objects['FrontThreeQuarter'];s.frame_set(1)
        path=OUT/('football_player_'+STEMS[name])
        bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
        export(s,r,action,path.with_suffix('.glb'))
        lows=[]
        for tick in range((keys[-1][0]-1)*4+1):
            s.frame_set(1+tick//4,subframe=(tick%4)/4);lows.append(floor(r))
            assert r.pose.bones['Root'].matrix_basis==Matrix.Identity(4)
        report[name]={'duration_seconds':(keys[-1][0]-1)/60,'minimum_mesh_y':min(lows),'loop':name=='Down','root_identity':True}
        assert min(lows)>-.005,(name,min(lows))
        for f,label in labels[name]:
            if name=='Down' and f!=31:continue
            s.frame_set(f);s.camera=bpy.data.objects['Side']
            s.render.filepath=str(PRE/f'{name}_{f:03d}_{label}.png');bpy.ops.render.render(write_still=True)
        print('RECOVERY_COMPLETE',name,report[name],flush=True)
    (PRE/'validation.json').write_text(json.dumps(report,indent=2))

if __name__=='__main__':main()
