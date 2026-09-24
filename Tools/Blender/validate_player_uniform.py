"""Render locomotion contact sheets and compare exported motion to Git baseline.
Run via Blender MCP after upgrade_player_uniform and canonical export.
"""
from pathlib import Path
import bpy, json, struct, subprocess, sys, numpy as np
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Models'; PRE=OUT/'UniformPreviews'

def channels(raw):
    size=struct.unpack_from('<I',raw,12)[0];d=json.loads(raw[20:20+size]);binary=raw[28+size:]
    def accessor(index):
        a=d['accessors'][index];v=d['bufferViews'][a['bufferView']]
        count={'SCALAR':1,'VEC3':3,'VEC4':4}[a['type']]
        assert a['componentType']==5126
        return np.frombuffer(binary,dtype='<f4',count=a['count']*count,offset=v.get('byteOffset',0)+a.get('byteOffset',0)).reshape(a['count'],count)
    result={}
    for a in d['animations']:
        for c in a['channels']:
            key=(a['name'],d['nodes'][c['target']['node']]['name'],c['target']['path']);s=a['samplers'][c['sampler']]
            assert key not in result,('Duplicate animation channel',key)
            result[key]=(accessor(s['input']),accessor(s['output']))
    return result

report={}
sys.dont_write_bytecode=True
sys.path.insert(0,str(ROOT/'Tools/Blender'))
from upgrade_player_uniform import signatures
for stem in ('rigged','jog','run','sprint'):
    name='lowpoly_human_'+stem+'.blend'
    baseline=PRE/'_baseline.blend'
    baseline.write_bytes(subprocess.check_output(['git','-c','safe.directory='+ROOT.as_posix(),'show','HEAD:Assets/Models/'+name],cwd=str(ROOT)))
    bpy.ops.wm.open_mainfile(filepath=str(baseline));before=signatures()
    s=bpy.context.scene;timing=(s.frame_start,s.frame_end,s.render.fps,s.render.fps_base)
    bpy.ops.wm.open_mainfile(filepath=str(OUT/name));after=signatures()
    assert before==after,('Source preservation failure',name)
    s=bpy.context.scene;assert timing==(s.frame_start,s.frame_end,s.render.fps,s.render.fps_base)
    added=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in bpy.data.objects if o.name in ('Pants','Socks','Cleat_L','Cleat_R'))
    removed=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in bpy.data.objects if o.type=='MESH' and o.get('uniform_reference'))
    report[name]={'original_geometry_weights_bones_rest_matrices_actions_equipment_preserved':True,'timing_preserved':True,'new_triangles':added,'net_export_triangle_change':added-removed}
    baseline.unlink()
for name in ['football_player.glb']+['lowpoly_human_'+a+'_validation.glb' for a in ('jog','run','sprint')]:
    # Canonical HEAD predates the approved Jog validation export. The approved
    # validation clip is the motion authority for the regenerated canonical asset.
    baseline='lowpoly_human_jog_validation.glb' if name=='football_player.glb' else name
    old=subprocess.check_output(['git','-c','safe.directory='+ROOT.as_posix(),'show','HEAD:Assets/Models/'+baseline],cwd=str(ROOT))
    before=channels(old);after=channels((OUT/name).read_bytes());peak=0
    # Equipment adds nodes but must never alter existing skeletal channels.
    common=before.keys()&after.keys()
    for key in common:
        for x,y in zip(before[key],after[key]):
            assert x.shape==y.shape,(name,key,x.shape,y.shape)
            error=float(np.max(np.abs(x-y)));peak=max(peak,error);assert error<1e-5,(name,key,error)
    assert len(common)>=24*3,(name,len(common))
    report[name]={'baseline':'HEAD:Assets/Models/'+baseline,'unchanged_channels':len(common),'maximum_difference':peak}

for clip,frames in [('Jog',[1,5,10,15,19,24,28]),('Run',[1,5,9,13,17,21,24]),('Sprint',[1,4,7,11,14,17,20])]:
    bpy.ops.wm.open_mainfile(filepath=str(OUT/('lowpoly_human_'+clip.lower()+'.blend')))
    s=bpy.context.scene;s.render.resolution_x=240;s.render.resolution_y=280;s.render.resolution_percentage=100;s.eevee.taa_render_samples=16;s.render.image_settings.file_format='PNG'
    sheet=np.zeros((560,240*len(frames),4),dtype=np.float32)
    for row,view in enumerate(('FrontThreeQuarter','Side')):
        s.camera=bpy.data.objects[view]
        for col,frame in enumerate(frames):
            s.frame_set(frame);temp=PRE/'contact_tile.png';s.render.filepath=str(temp);bpy.ops.render.render(write_still=True)
            im=bpy.data.images.load(str(temp),check_existing=False);pixels=np.array(im.pixels[:]).reshape(280,240,4)
            sheet[(1-row)*280:(2-row)*280,col*240:(col+1)*240]=pixels;bpy.data.images.remove(im)
    im=bpy.data.images.new(clip+'_ContactSheet',width=240*len(frames),height=560)
    im.pixels.foreach_set(sheet.ravel());im.filepath_raw=str(PRE/(clip+'_ContactSheet.png'));im.file_format='PNG';im.save();temp.unlink()
    report[clip]={'visual_sample_frames':frames,'views':['FrontThreeQuarter','Side']}
(PRE/'export_validation.json').write_text(json.dumps(report,indent=2))
print('UNIFORM_EXPORT_VALIDATION',json.dumps(report))
