"""Render 25 rear/side samples at clip timing, with slow motion and scrubbing."""
from pathlib import Path
import sys, json
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
from rework_player_jukes import evades
from preview_player_cuts import floor
from create_football import look

def main():
    out = evades.PRE
    out.mkdir(exist_ok=True)
    for name in evades.CONFIG:
        bpy.ops.wm.open_mainfile(filepath=str(evades.OUT / (evades.stem(name)+'.blend')))
        scene = bpy.context.scene
        floor()
        scene.render.engine = 'BLENDER_EEVEE'
        scene.eevee.taa_render_samples = 24
        scene.render.resolution_x = 360
        scene.render.resolution_y = 420
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = 'PNG'
        scene.camera.data.ortho_scale = 2.45
        for view, location in [('Rear',(0,1.45,4)), ('Side',(-4,1.45,0))]:
            scene.camera.location = location
            look(scene.camera,(0,.90,0))
            for i in range(25):
                frame=1+(evades.CONFIG[name]['frames']-1)*i/24
                scene.frame_set(int(frame),subframe=frame%1)
                scene.render.filepath = str(out / f'{name}_{view}_{i:02}.png')
                bpy.ops.render.render(write_still=True)
    html = '''<!doctype html><meta charset="utf-8"><title>Reworked jukes</title>
<style>body{background:#20262c;color:#eee;font:17px system-ui;margin:24px}button,select,input{font:inherit;margin:8px}main{display:flex;flex-wrap:wrap;gap:16px}img{width:360px;max-width:45vw}figure{margin:0}figcaption{margin:10px 0}</style>
<h1>Juke: brake, drive sideways, power out</h1>
<p>0.35-second in-place clips. Wide base, deep braking plant, low lateral transfer and forward push. Left: Foot.R plant → Foot.L landing and drive. Right reverses the footwork. Bone names follow the existing rig.</p>
<button id="play">Pause</button><select id="speed"><option value="1">Normal speed</option><option value="0.5">Half speed</option><option value="0.25">Quarter speed</option></select>
<input id="scrub" type="range" min="0" max="24" value="0"><span id="phase"></span><main>'''
    for name in evades.CONFIG:
        html += f'<figure><figcaption>{name} — rear / side</figcaption>'
        for view in ('Rear','Side'):
            html += f'<img data-prefix="{name}_{view}_" src="{name}_{view}_00.png">'
        html += '</figure>'
    html += '''</main><p><a href="index.html" style="color:#acd">Four-view phase sheets</a></p>
<script>
const imgs=[...document.querySelectorAll('img')],scrub=document.querySelector('#scrub'),phase=document.querySelector('#phase'),play=document.querySelector('#play'),speed=document.querySelector('#speed');
for(const img of imgs)for(let i=0;i<25;i++){let preload=new Image();preload.src=img.dataset.prefix+String(i).padStart(2,'0')+'.png'}
let playing=true,time=0,last=performance.now();
function show(){let frame=Math.min(24,Math.floor(time/.35*24));scrub.value=frame;for(const img of imgs)img.src=img.dataset.prefix+String(frame).padStart(2,'0')+'.png';phase.textContent=(frame*.35/24).toFixed(3)+' s — '+(frame<4?'Approach':frame<9?'Brake low':frame<14?'Push sideways':frame<19?'Land and load':'Power forward')}
play.onclick=()=>{playing=!playing;play.textContent=playing?'Pause':'Play'};
scrub.oninput=()=>{playing=false;play.textContent='Play';time=Number(scrub.value)*.35/24;show()};
function tick(now){if(playing){time+=(now-last)/1000*Number(speed.value);if(time>.7)time=0;show()}last=now;requestAnimationFrame(tick)}requestAnimationFrame(tick);
</script>'''
    (out/'playback.html').write_text(html,encoding='utf-8')
    print('JUKE_PLAYBACK_COMPLETE')

if __name__ == '__main__': main()
