"""Check saved recovery assets and render a single sequence preview with floor."""
from pathlib import Path
import sys,json,struct
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector
from create_defender_recovery import OUT,PRE,STEMS,snapshot,apply
from validate_set_wrap import signature

def mesh_pose():
    dg=bpy.context.evaluated_depsgraph_get()
    return [o.matrix_world@v.co for src in sorted(bpy.context.scene.objects,key=lambda o:o.name) if src.type=='MESH'
            for o in [src.evaluated_get(dg)] for v in o.data.vertices]

def error(a,b):return max((x-y).length for x,y in zip(a,b))

def main():
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'football_player_lunge_tackle_forward.blend'))
    bpy.context.scene.frame_set(37);entry=mesh_pose();baseline=signature()
    report=json.loads((PRE/'validation.json').read_text());ends={};sequence=[]
    for name,stem in STEMS.items():
        path=OUT/('football_player_'+stem)
        bpy.ops.wm.open_mainfile(filepath=str(path.with_suffix('.blend')))
        s=bpy.context.scene;r=bpy.data.objects['PlayerRig']
        assert signature()==baseline,(name,'mesh/skeleton/weights changed')
        s.frame_set(1);start=mesh_pose();s.frame_set(s.frame_end);end=mesh_pose();ends[name]=(start,end)
        raw=path.with_suffix('.glb').read_bytes();doc=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
        assert len(doc['animations'])==1 and doc['animations'][0]['name']==name
        duration=max(doc['accessors'][a['input']]['max'][0] for a in doc['animations'][0]['samplers'])-min(doc['accessors'][a['input']]['min'][0] for a in doc['animations'][0]['samplers'])
        assert abs(duration-report[name]['duration_seconds'])<1e-5
        report[name]['geometry_skeleton_weights_preserved']=True
        report[name]['single_named_glb_action']=True
        for f in range(1,s.frame_end+1):
            s.frame_set(f);sequence.append(snapshot(r))
    checks={'lunge_to_land':error(entry,ends['LungeLand'][0]),
            'land_to_down':error(ends['LungeLand'][1],ends['Down'][0]),
            'down_loop':error(*ends['Down']),
            'down_to_get_up':error(ends['Down'][1],ends['GetUp'][0])}
    assert max(checks.values())<1e-5,checks
    report['transition_mesh_errors_m']=checks
    report['status']='PASS'
    (PRE/'validation.json').write_text(json.dumps(report,indent=2))
    r.animation_data.action=bpy.data.actions.new('RecoverySequencePreview')
    for f,pose in enumerate(sequence,1):
        apply(r,pose)
        for p in r.pose.bones:
            for prop in ['location','rotation_quaternion','scale']:p.keyframe_insert(prop,frame=f)
    s.frame_start=1;s.frame_end=len(sequence);s.frame_step=2
    # Authoring is Y-up; the temporary preview floor is an XZ plane.
    bpy.ops.mesh.primitive_plane_add(size=200,rotation=(1.57079632679,0,0))
    ground=bpy.context.object;ground.name='PreviewFloor'
    mat=bpy.data.materials.new('PreviewFloorMaterial');mat.diffuse_color=(.055,.075,.06,1);ground.data.materials.append(mat)
    s.camera=bpy.data.objects['Side'];s.render.fps=60
    s.render.image_settings.file_format='FFMPEG';s.render.ffmpeg.format='MPEG4';s.render.ffmpeg.codec='H264'
    s.render.ffmpeg.constant_rate_factor='MEDIUM';s.render.filepath=str(PRE/'defender_recovery.mp4')
    # Bake to 30 fps without changing playback duration.
    for fc in r.animation_data.action.fcurves:
        for key in fc.keyframe_points:key.co.x=(key.co.x-1)/2+1
    s.frame_end=(len(sequence)-1)//2+1;s.frame_step=1;s.render.fps=30
    bpy.ops.render.render(animation=True)
    print('RECOVERY_VALIDATION_PASS',json.dumps(report),flush=True)

if __name__=='__main__':main()
