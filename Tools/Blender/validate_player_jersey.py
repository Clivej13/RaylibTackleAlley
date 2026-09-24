"""Read-only source/export audit and rendered pad-exposure checks via Blender MCP.

Temporary diagnostic materials are never saved to source files. The visibility
check measures pad pixels visible through the complete clothing stack; internal
overlapping garment panels can intersect pads without exposing them externally.
"""
from pathlib import Path
import bpy, json, struct, subprocess, sys, hashlib
import numpy as np
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Assets/Models';PRE=OUT/'JerseyPreviews'
sys.dont_write_bytecode=True
sys.path.insert(0,str(ROOT/'Tools/Blender'))
from upgrade_player_jersey import signature

def channels(raw):
    size=struct.unpack_from('<I',raw,12)[0];d=json.loads(raw[20:20+size]);binary=raw[28+size:]
    def read(i):
        a=d['accessors'][i];v=d['bufferViews'][a['bufferView']];c={'SCALAR':1,'VEC3':3,'VEC4':4}[a['type']]
        assert a['componentType']==5126
        return np.frombuffer(binary,dtype='<f4',count=a['count']*c,offset=v.get('byteOffset',0)+a.get('byteOffset',0)).reshape(a['count'],c)
    result={}
    for a in d['animations']:
        for c in a['channels']:
            k=(a['name'],d['nodes'][c['target']['node']]['name'],c['target']['path']);s=a['samplers'][c['sampler']]
            assert k not in result;result[k]=(read(s['input']),read(s['output']))
    return d,result

def emission(name,color):
    m=bpy.data.materials.new(name);m.use_nodes=True;n=m.node_tree.nodes;n.clear();out=n.new('ShaderNodeOutputMaterial');em=n.new('ShaderNodeEmission');em.inputs['Color'].default_value=(*color,1);m.node_tree.links.new(em.outputs[0],out.inputs['Surface']);return m

def contacts(clip,frames):
    s=bpy.context.scene;s.render.resolution_x=240;s.render.resolution_y=280;s.render.resolution_percentage=100;s.eevee.taa_render_samples=16;s.render.image_settings.file_format='PNG'
    sheet=np.zeros((560,240*len(frames),4),dtype=np.float32)
    for row,view in enumerate(('FrontThreeQuarter','Side')):
        s.camera=bpy.data.objects[view]
        for col,f in enumerate(frames):
            s.frame_set(f);temp=PRE/'_tile.png';s.render.filepath=str(temp);bpy.ops.render.render(write_still=True)
            im=bpy.data.images.load(str(temp),check_existing=False);sheet[(1-row)*280:(2-row)*280,col*240:(col+1)*240]=np.array(im.pixels[:]).reshape(280,240,4);bpy.data.images.remove(im)
    im=bpy.data.images.new(clip+'_ContactSheet',width=240*len(frames),height=560);im.pixels.foreach_set(sheet.ravel());im.filepath_raw=str(PRE/(clip+'_ContactSheet.png'));im.file_format='PNG';im.save();bpy.data.images.remove(im);temp.unlink()

def exposure(clip):
    s=bpy.context.scene;r=bpy.data.objects['PlayerRig'];black=emission('AuditOccluder',(0,0,0));red=emission('AuditPads',(1,0,0))
    for o in s.objects:
        if o.type=='MESH':
            for i in range(len(o.data.materials)):o.data.materials[i]=red if o.name=='ShoulderPads' else black
    s.render.resolution_x=320;s.render.resolution_y=360;s.render.resolution_percentage=100;s.eevee.taa_render_samples=8;s.render.film_transparent=True
    s.view_settings.view_transform='Standard';s.view_settings.look='Medium High Contrast';s.view_settings.exposure=0;s.view_settings.gamma=1
    temp=PRE/'_exposure.png';failures=[];tests=0
    for view in ('Front','Side','Back','FrontThreeQuarter'):
        s.camera=bpy.data.objects[view]
        start,end=(1,1) if clip=='Standing' else map(int,r.animation_data.action.frame_range)
        for f in range(start,end+1):
            s.frame_set(f);s.render.filepath=str(temp);bpy.ops.render.render(write_still=True)
            im=bpy.data.images.load(str(temp),check_existing=False);p=np.array(im.pixels[:]).reshape(-1,4);count=int(np.sum((p[:,0]>.4)&(p[:,1]<.05)&(p[:,3]>.9)));bpy.data.images.remove(im);tests+=1
            if count:failures.append({'view':view,'frame':f,'pad_pixels':count})
    temp.unlink();return {'renders_checked':tests,'resolution':[320,360],'visible_pad_samples':failures}

def main():
    source=json.loads((PRE/'validation.json').read_text());report={}
    for stem in ('rigged','jog','run','sprint'):
        bpy.ops.wm.open_mainfile(filepath=str(OUT/('lowpoly_human_'+stem+'.blend')))
        assert json.loads(json.dumps(signature()))==source[stem]['preservation']
        report['lowpoly_human_'+stem+'.blend']={'saved_source_preservation':True}
    for name in ['football_player.glb']+['lowpoly_human_'+x+'_validation.glb' for x in ('jog','run','sprint')]:
        baseline='lowpoly_human_jog_validation.glb' if name=='football_player.glb' else name
        raw=subprocess.check_output(['git','-c','safe.directory='+ROOT.as_posix(),'show','HEAD:Assets/Models/'+baseline],cwd=str(ROOT));_,before=channels(raw);doc,after=channels((OUT/name).read_bytes())
        for k in before.keys()&after.keys():
            assert all(np.array_equal(x,y) for x,y in zip(before[k],after[k])),(name,k)
        nodes={n['name']:n for n in doc['nodes']};assert all('skin' in nodes[n] for n in ('Undershirt','Jersey','Pants','Socks','Cleat_L','Cleat_R','ShoulderPads'))
        assert {'Undershirt','Jersey','Jersey_Sleeves'}<={m['name'] for m in doc['materials']}
        assert len(doc['skins'][0]['joints'])==24
        report[name]={'unchanged_animation_channels':len(before.keys()&after.keys()),'new_layers_skinned':True,'materials_identifiable':True}
    for clip,frames in [('Jog',[1,5,10,15,19,24,28]),('Run',[1,5,9,13,17,21,24]),('Sprint',[1,4,7,11,14,17,20])]:
        bpy.ops.wm.open_mainfile(filepath=str(OUT/('lowpoly_human_'+clip.lower()+'.blend')));contacts(clip,frames);report[clip]=exposure(clip)
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_rigged.blend'))
    bpy.data.objects['PlayerRig'].data.pose_position='REST';report['Standing']=exposure('Standing')
    (PRE/'export_validation.json').write_text(json.dumps(report,indent=2))
    assert all(not report[n]['visible_pad_samples'] for n in ('Standing','Jog','Run','Sprint')),'Visible shoulder pads detected'
    print('JERSEY_VALIDATION',json.dumps(report))

if __name__=='__main__':main()
