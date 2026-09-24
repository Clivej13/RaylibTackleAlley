"""Build GetUp -> TauntBicepFlex with planted feet. Run through Blender MCP."""
from pathlib import Path
import sys, math, json, struct
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
from mathutils import Quaternion, Vector, Matrix
from create_defender_recovery import snapshot, apply, blend, orient, floor
from upgrade_player_hands import export
from validate_set_wrap import signature

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
PRE = OUT / 'DefenderTauntPreviews'

def main():
    PRE.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'football_player_get_up.blend'))
    bpy.context.preferences.filepaths.save_version = 0
    s = bpy.context.scene; r = bpy.data.objects['PlayerRig']
    baseline = signature()
    recovery = []
    for f in range(1, s.frame_end+1):
        s.frame_set(f); recovery.append(snapshot(r))
    start = snapshot(r)
    r.animation_data.action = None
    def flex(amount, squeeze=0):
        apply(r, start)
        r.pose.bones['Chest'].rotation_quaternion = Quaternion((1,0,0), math.radians( -5*amount-squeeze))
        r.pose.bones['Head'].rotation_quaternion = Quaternion((1,0,0), math.radians(4*amount))
        bpy.context.view_layer.update()
        for side, sign in [('L',1),('R',-1)]:
            orient(r, 'UpperArm.'+side, (sign, .22+squeeze*.018, -.09))
            orient(r, 'LowerArm.'+side, (-sign*.30, 1, -.13))
            orient(r, 'Hand.'+side, (-sign*.28, 1, -.10))
            for name, angle in [('Fingers',85),('Thumb',48)]:
                r.pose.bones[name+'.'+side].rotation_quaternion = Quaternion((0,0,1), math.radians(-sign*angle))
        target = snapshot(r)
        return blend(start, target, amount)
    wind = flex(.14)
    peak = flex(1)
    squeeze = flex(1, 2)
    keys = [(1,start),(13,wind),(43,peak),(59,squeeze),(91,peak),(109,squeeze),(121,peak),(157,start)]
    action = bpy.data.actions.new('TauntBicepFlex')
    action.use_fake_user = True; r.animation_data.action = action
    s.frame_start = 1; s.frame_end = 157; s.render.fps = 60
    s.timeline_markers.clear()
    for f,label in [(1,'GetUp match'),(13,'Gather'),(43,'Double bicep flex'),(59,'Squeeze'),(121,'Release'),(157,'Standing')]:
        s.timeline_markers.new(label,frame=f)
    sequence=[]; previous={}
    for f in range(1,158):
        s.frame_set(f)
        a,b=next((a,b) for a,b in zip(keys,keys[1:]) if a[0]<=f<=b[0])
        apply(r,blend(a[1],b[1],(f-a[0])/(b[0]-a[0])))
        for p in r.pose.bones:
            if p.name in previous and p.rotation_quaternion.dot(previous[p.name])<0: p.rotation_quaternion.negate()
            previous[p.name]=p.rotation_quaternion.copy()
            for prop in ('location','rotation_quaternion','scale'): p.keyframe_insert(prop,frame=f,group=p.name)
        sequence.append(snapshot(r))
    for fc in action.fcurves:
        for k in fc.keyframe_points: k.interpolation='LINEAR'
    action['loop']=False
    action['description']='GetUp endpoint -> double bicep flex with two restrained squeezes -> same standing pose. Planted feet; Y up, -Z forward; identity Root.'
    assert signature()==baseline
    lows=[]; foot_error=0
    s.frame_set(1)
    feet={n:r.pose.bones[n].matrix.copy() for n in ['Foot.L','Foot.R']}
    for tick in range(625):
        s.frame_set(1+tick//4,subframe=(tick%4)/4)
        lows.append(floor(r))
        assert r.pose.bones['Root'].matrix_basis==Matrix.Identity(4)
        for n in feet:
            foot_error=max(foot_error,max(abs(r.pose.bones[n].matrix[i][j]-feet[n][i][j]) for i in range(4) for j in range(4)))
    assert min(lows)>-.005 and foot_error<1e-6
    for f in [1,157]:
        s.frame_set(f)
        for n,(loc,rot,scale) in start.items():
            p=r.pose.bones[n]
            assert (p.location-loc).length<1e-6 and abs(p.rotation_quaternion.dot(rot))>0.999999
    s.camera=bpy.data.objects['FrontThreeQuarter']
    camera=s.camera; camera.location=(2.8,1.9,-5.4); camera.data.ortho_scale=2.6
    forward=(Vector((0,1,-.5))-camera.location).normalized()
    right=forward.cross(Vector((0,1,0))).normalized(); up=right.cross(forward)
    camera.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
    s.render.engine='BLENDER_EEVEE'; s.eevee.taa_render_samples=24
    s.render.resolution_x=720;s.render.resolution_y=720;s.render.resolution_percentage=100
    s.frame_set(59)
    path=OUT/'football_player_taunt_bicep_flex'
    bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
    export(s,r,action,path.with_suffix('.glb'))
    raw=path.with_suffix('.glb').read_bytes(); doc=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
    assert len(doc['animations'])==1 and doc['animations'][0]['name']=='TauntBicepFlex'
    report={'status':'PASS','action':'TauntBicepFlex','duration_seconds':2.6,'get_up_entry_match':True,'standing_exit_match':True,'geometry_skeleton_weights_preserved':True,'root_identity':True,'maximum_foot_matrix_error':foot_error,'minimum_mesh_y':min(lows),'single_named_glb_action':True}
    (PRE/'validation.json').write_text(json.dumps(report,indent=2))
    s.frame_set(59);s.render.image_settings.file_format='PNG';s.render.filepath=str(PRE/'bicep_flex.png')
    bpy.ops.render.render(write_still=True)
    # Preview includes the complete get-up so the requested transition is visible.
    r.animation_data.action=bpy.data.actions.new('GetUpTauntPreview')
    for f,pose in enumerate((recovery+sequence[1:])[::2],1):
        apply(r,pose)
        for p in r.pose.bones:
            for prop in ('location','rotation_quaternion','scale'):p.keyframe_insert(prop,frame=f)
    s.frame_end=len((recovery+sequence[1:])[::2]);s.render.fps=30
    s.render.resolution_x=480;s.render.resolution_y=480;s.eevee.taa_render_samples=12
    s.render.image_settings.file_format='FFMPEG';s.render.ffmpeg.format='MPEG4';s.render.ffmpeg.codec='H264'
    s.render.filepath=str(PRE/'get_up_to_bicep_flex.mp4')
    bpy.ops.render.render(animation=True)
    print('TAUNT_VALIDATION',json.dumps(report),flush=True)

if __name__=='__main__':main()
