"""Render four-view phase sheets per evade and a labeled HTML review page."""
from pathlib import Path
import sys, json
sys.dont_write_bytecode=True
import bpy
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from create_player_evades import CONFIG, OUT, PRE, stem
from preview_player_cuts import floor
from create_football import look

def main():
    index={}
    for name,cfg in CONFIG.items():
        bpy.ops.wm.open_mainfile(filepath=str(OUT/(stem(name)+'.blend')))
        scene=bpy.context.scene
        floor()
        scene.render.engine='BLENDER_EEVEE'; scene.eevee.taa_render_samples=24
        scene.render.resolution_x=360; scene.render.resolution_y=420
        scene.render.resolution_percentage=100
        scene.render.image_settings.file_format='PNG'
        camera=scene.camera; camera.data.ortho_scale=2.45
        phases=([(0,'Entry'),(.26,'BrakeLow'),(.40,'PushSideways'),(.56,'LandLow'),(.75,'PushForward'),(1,'RunExit')]
                if cfg['kind']=='Juke' else [(0,'Entry'),(.23,'PlantLoad'),(.36,'Release'),(.53,'Rotate'),(.77,'RecoveryStep'),(1,'RunExit')])
        views=[('Front',(0,1.45,-4)),('Rear',(0,1.45,4)),('Side',(-4,1.45,0)),('ThreeQuarter',(-3,1.7,-4))]
        sheet=np.zeros((420*4,360*6,4),dtype=np.float32)
        index[name]={'rows':[v[0] for v in views],'columns':[]}
        for row,(view,location) in enumerate(views):
            camera.location=location; look(camera,(0,.90,0))
            for col,(t,label) in enumerate(phases):
                f=round(t*(cfg['frames']-1))+1; scene.frame_set(f)
                path=PRE/(name+'_'+view+'_'+label+'.png')
                scene.render.filepath=str(path); bpy.ops.render.render(write_still=True)
                img=bpy.data.images.load(str(path),check_existing=False)
                px=np.empty(360*420*4,dtype=np.float32); img.pixels.foreach_get(px)
                sheet[(3-row)*420:(4-row)*420,col*360:(col+1)*360]=px.reshape((420,360,4))
                bpy.data.images.remove(img)
                if row==0: index[name]['columns'].append({'phase':label,'frame':f,'seconds':(f-1)/120})
        img=bpy.data.images.new(name+'_phases',width=2160,height=1680,alpha=True)
        img.pixels.foreach_set(sheet.ravel()); img.filepath_raw=str(PRE/(name+'_phases.png')); img.file_format='PNG'; img.save()
        print('EVADE_PREVIEWS_COMPLETE',name,flush=True)
    (PRE/'phase_index.json').write_text(json.dumps(index,indent=2))
    html=['<!doctype html><meta charset="utf-8"><title>Ball-carrier evades</title><style>body{background:#20262c;color:#eee;font:16px system-ui;margin:24px}table{border-collapse:collapse;width:100%;table-layout:fixed}td,th{padding:5px;text-align:center}img{width:100%}th:first-child{width:90px}h2{margin-top:40px}</style><h1>Ball-carrier Juke / Spin</h1><p>Fixed root; forward is -Z. Rig anatomy: Left +X, Right -X. Phase times are seconds from clip entry.</p>']
    for name,data in index.items():
        html.append('<h2>'+name+'</h2><table><tr><th>View</th>'+''.join('<th>'+p['phase']+'<br>'+format(p['seconds'],'.3f')+' s</th>' for p in data['columns'])+'</tr>')
        for view in data['rows']:
            html.append('<tr><th>'+view+'</th>'+''.join('<td><a href="'+name+'_'+view+'_'+p['phase']+'.png"><img src="'+name+'_'+view+'_'+p['phase']+'.png"></a></td>' for p in data['columns'])+'</tr>')
        html.append('</table>')
    (PRE/'index.html').write_text('\n'.join(html),encoding='utf-8')

if __name__=='__main__': main()
