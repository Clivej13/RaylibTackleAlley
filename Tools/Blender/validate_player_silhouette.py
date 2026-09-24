"""Validate refined saved sources and exports, render matching comparison sheets."""
from pathlib import Path
import bpy, sys, json
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
sys.dont_write_bytecode=True
sys.path.insert(0,str(ROOT/'Tools/Blender'))
import refine_player_silhouette as fit
import validate_player_jersey as audit
from upgrade_player_uniform import export
PRE=fit.PRE
audit.PRE=PRE

def combine(paths,destination,vertical=False):
    arrays=[]
    for path in paths:
        im=bpy.data.images.load(str(path),check_existing=False)
        arrays.append(np.array(im.pixels[:],dtype=np.float32).reshape(im.size[1],im.size[0],4))
        bpy.data.images.remove(im)
    # Blender pixel origin is bottom left; before above after in vertical sheets.
    pixels=np.concatenate(arrays[::-1] if vertical else arrays,axis=0 if vertical else 1)
    im=bpy.data.images.new(destination.stem,width=pixels.shape[1],height=pixels.shape[0])
    im.pixels.foreach_set(pixels.ravel());im.filepath_raw=str(destination);im.file_format='PNG';im.save();bpy.data.images.remove(im)

def main():
    report={}
    for stem in ('rigged','jog','run','sprint'):
        name='lowpoly_human_'+stem
        bpy.ops.wm.open_mainfile(filepath=str(PRE/'Baseline'/(name+'.blend')))
        before=fit.fingerprint()
        if stem!='rigged':export(PRE/'Baseline'/(name+'_validation.glb'))
        bpy.ops.wm.open_mainfile(filepath=str(fit.OUT/(name+'.blend')))
        assert before==fit.fingerprint(),stem
        # The saved geometry must also match a repeated deterministic refinement.
        coords={n:[tuple(v.co) for v in bpy.data.objects[n].data.vertices] for n in ('Jersey','Undershirt')}
        fit.refine()
        assert coords=={n:[tuple(v.co) for v in bpy.data.objects[n].data.vertices] for n in coords}
        report[stem]={'source_preservation':True,'weights_unchanged':True,'idempotent':True}
    for name,base in [('football_player.glb','lowpoly_human_jog_validation.glb')]+[(f'lowpoly_human_{c}_validation.glb',f'lowpoly_human_{c}_validation.glb') for c in ('jog','run','sprint')]:
        _,before=audit.channels((PRE/'Baseline'/base).read_bytes())
        doc,after=audit.channels((fit.OUT/name).read_bytes())
        assert before.keys()==after.keys(),name
        assert all(all(np.array_equal(x,y) for x,y in zip(before[k],after[k])) for k in before),name
        nodes={n['name']:n for n in doc['nodes']}
        assert all('skin' in nodes[n] for n in ('Jersey','Undershirt','Pants','Socks','Cleat_L','Cleat_R','ShoulderPads'))
        assert {'Helmet','Facemask','Visor','ChinStrap'}<=nodes.keys()
        report[name]={'exact_animation_channels':len(before),'triangles':sum(doc['accessors'][p['indices']]['count']//3 for m in doc['meshes'] for p in m['primitives']),'joints':len(doc['skins'][0]['joints'])}
    for clip,frames in [('Jog',[1,5,10,15,19,24,28]),('Run',[1,5,9,13,17,21,24]),('Sprint',[1,4,7,11,14,17,20])]:
        bpy.ops.wm.open_mainfile(filepath=str(fit.OUT/('lowpoly_human_'+clip.lower()+'.blend')))
        audit.contacts(clip,frames)
        combine([fit.OUT/'JerseyPreviews'/(clip+'_ContactSheet.png'),PRE/(clip+'_ContactSheet.png')],PRE/(clip+'_BeforeAfter.png'),True)
        report[clip]=audit.exposure(clip)
    bpy.ops.wm.open_mainfile(filepath=str(fit.OUT/'lowpoly_human_rigged.blend'))
    bpy.data.objects['PlayerRig'].data.pose_position='REST'
    report['Standing']=audit.exposure('Standing')
    combine([fit.OUT/'JerseyPreviews'/'Standing_Front.png',PRE/'Standing_Front.png'],PRE/'Standing_BeforeAfter.png')
    (PRE/'export_validation.json').write_text(json.dumps(report,indent=2))
    assert all(not report[n]['visible_pad_samples'] for n in ('Standing','Jog','Run','Sprint')),'Visible shoulder pads detected'
    print('SILHOUETTE_VALIDATION',json.dumps(report))

def torso_coverage():
    """Detect visible anatomical torso through garments in all integer frames."""
    report={}
    for clip in ('Standing','Jog','Run','Sprint'):
        stem='rigged' if clip=='Standing' else clip.lower()
        bpy.ops.wm.open_mainfile(filepath=str(fit.OUT/('lowpoly_human_'+stem+'.blend')))
        s=bpy.context.scene;r=bpy.data.objects['PlayerRig']
        if clip=='Standing':r.data.pose_position='REST'
        black=audit.emission('CoverageOccluder',(0,0,0));red=audit.emission('CoverageTorso',(1,0,0))
        for o in s.objects:
            if o.type=='MESH':
                for i in range(len(o.data.materials)):
                    o.data.materials[i]=red if o.name in ('Belly','LowerBack','LeftChest','RightChest','UpperBack','LeftShoulder','RightShoulder') else black
        s.render.resolution_x=320;s.render.resolution_y=360;s.render.resolution_percentage=100;s.eevee.taa_render_samples=8;s.render.film_transparent=True
        s.view_settings.view_transform='Standard';s.view_settings.look='Medium High Contrast';s.view_settings.exposure=0;s.view_settings.gamma=1
        failures=[];tests=0;temp=PRE/'_torso.png'
        for view in ('Front','Side','Back','FrontThreeQuarter'):
            s.camera=bpy.data.objects[view]
            start,end=(1,1) if clip=='Standing' else map(int,r.animation_data.action.frame_range)
            for f in range(start,end+1):
                s.frame_set(f);s.render.filepath=str(temp);bpy.ops.render.render(write_still=True)
                im=bpy.data.images.load(str(temp),check_existing=False);p=np.array(im.pixels[:]).reshape(-1,4)
                count=int(np.sum((p[:,0]>.4)&(p[:,1]<.05)&(p[:,3]>.9)))
                bpy.data.images.remove(im);tests+=1
                if count:failures.append({'view':view,'frame':f,'pixels':count})
        temp.unlink();report[clip]={'renders_checked':tests,'exposed_torso_samples':failures}
    (PRE/'torso_coverage.json').write_text(json.dumps(report,indent=2))
    assert all(not v['exposed_torso_samples'] for v in report.values()),report
    print('TORSO_COVERAGE',json.dumps(report))

if __name__=='__main__':
    main()
    torso_coverage()
