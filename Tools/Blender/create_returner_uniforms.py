"""Generate returner atlases and previews via Blender MCP; no source model writes."""
from pathlib import Path
import bpy, json, hashlib, re
import numpy as np
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Textures/Uniforms/Returners'
PRE = OUT / 'Previews'
PRE.mkdir(parents=True, exist_ok=True)
N = 2048
NAVY, CREAM, GOLD = '#102344', '#f5f5f5', '#d9ac45'
# jersey, sleeves, pants, stripe segments (relative positions within existing band).
DESIGNS = {
 'marcus-reed': (NAVY, NAVY, CREAM, [(0,1,GOLD)]),
 'eli-brooks': ('#246b78', '#246b78', CREAM, [(0,.35,CREAM),(.65,1,CREAM)]),
 'jalen-price': (CREAM, NAVY, NAVY, [(0,.25,GOLD),(.4,.6,CREAM),(.75,1,GOLD)]),
 'darius-stone': ('#803247', '#803247', NAVY, [(0,1,NAVY),(.32,.68,GOLD)]),
 'noah-grant': (GOLD, NAVY, CREAM, [(0,.2,CREAM),(.2,.8,GOLD),(.8,1,CREAM)]),
}
regions = json.loads((ROOT/'Assets/Models/UniformAtlas/atlas_regions.json').read_text())
guides = json.loads((ROOT/'Assets/Models/UniformAtlas/artwork_guides.json').read_text())
roster = json.loads((ROOT/'returners.json').read_text())['Returners']
# Same original block alphabet as runtime PaintNumber, so generated/runtime art agrees.
digits = re.findall(r'"([01]{5}(?:/[01]{5}){6})"', (ROOT/'Game/PlayerUniform.cs').read_text())
assert len(digits) == 10
source = ROOT/'Assets/Models/football_player.glb'
protected = list((ROOT/'Assets/Models').glob('*.glb')) + list((ROOT/'Assets/Models').glob('*.blend'))
protected += [ROOT/'Assets/Textures/Uniforms'/f'{t}_uniform.png' for t in ('offense','defense')]
hashes = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
def rgba(c): return [int(c[i:i+2],16)/255 for i in (1,3,5)]+[1]
def rect(p, box, c):
 x0,y0,x1,y1 = map(round,box)
 p[max(0,y0):min(N,y1),max(0,x0):min(N,x1)] = rgba(c)
def atlas(entry):
 jersey,sleeve,pants,bands = DESIGNS[entry['Id']]
 p = np.ones((N,N,4),dtype=np.float32); p[:] = rgba(jersey)
 for name,region in regions.items():
  color = jersey
  if name.startswith('Trousers'): color = pants
  elif name.startswith('Helmet'): color = NAVY
  elif name.startswith('Sock') or name == 'Undershirt': color = NAVY
  elif name.startswith('Sleeve'): color = sleeve
  elif name == 'Jersey_sides_trim': color = NAVY
  rect(p,[v*N for v in region],color)
 for guide in guides:
  lo,hi = guide['span_px']; center=guide['center_px']; w=guide['width_px']
  if guide['region'].startswith('Sleeve'):
   for a,b,c in bands: rect(p,(lo,center-w/2+a*w,hi,center-w/2+b*w),c)
  elif guide['region'].startswith('Trousers'):
   rect(p,(center-w/2,lo,center+w/2,hi),GOLD if pants==NAVY else NAVY)
 # Reserve the runtime number patches exactly. Accent bars sit below them.
 ink = NAVY if sum(int(jersey[i:i+2],16)*v for i,v in zip((1,3,5),(.299,.587,.114)))>140 else CREAM
 number = str(entry['Profile']['JerseyNumber']); cell=46
 for cx in (round(N*.16),round(N*.48)):
  cy=N-round(N*.205); left=cx-(len(number)*6-1)*cell//2; top=cy+7*cell//2
  for d,char in enumerate(number):
   for row,line in enumerate(digits[int(char)].split('/')):
    for col,bit in enumerate(line):
     if bit=='1': rect(p,(left+(d*6+col)*cell,top-(row+1)*cell,left+(d*6+col+1)*cell,top-row*cell),ink)
  rect(p,(cx-55,cy-220,cx+55,cy-209),GOLD if jersey!=GOLD else NAVY)
 image=bpy.data.images.new(entry['Id'],width=N,height=N,alpha=True)
 image.colorspace_settings.name='sRGB'; image.pixels.foreach_set(p.ravel())
 image.filepath_raw=str(OUT/(entry['Id']+'.png')); image.file_format='PNG'; image.save()
 return image
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
for ob in bpy.data.objects:
 if ob.type=='ARMATURE': ob.data.pose_position='REST'
mat=bpy.data.materials['Uniform']; node=next(n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE')
scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'; scene.eevee.taa_render_samples=32
scene.world=bpy.data.worlds.new('ReturnerStudio'); scene.world.color=(.18,.18,.18)
scene.view_settings.view_transform='Standard'; scene.view_settings.look='Medium High Contrast'
scene.render.resolution_x=600; scene.render.resolution_y=800; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
for loc in [(3,4,5),(-3,1,3),(0,-4,4)]:
 bpy.ops.object.light_add(type='AREA',location=loc); ob=bpy.context.object; ob.data.energy=400; ob.data.size=5
 ob.rotation_euler=(Vector((0,0,1))-ob.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(); cam=bpy.context.object; cam.data.type='ORTHO'; cam.data.ortho_scale=2.2; scene.camera=cam
bpy.context.preferences.filepaths.save_version=0
for entry in roster:
 assert entry['Id'] in DESIGNS
 node.image=atlas(entry)
 for view,loc in [('front',(2.3,5,1.8)),('back',(-2.3,-5,1.8))]:
  cam.location=loc; cam.rotation_euler=(Vector((0,0,.98))-cam.location).to_track_quat('-Z','Y').to_euler()
  scene.render.filepath=str(PRE/(entry['Id']+'_'+view+'.png')); bpy.ops.render.render(write_still=True)
 node.image.filepath='//../'+entry['Id']+'.png'
 bpy.ops.wm.save_as_mainfile(filepath=str(PRE/(entry['Id']+'.blend')))
for path,h in hashes.items(): assert hashlib.sha256((ROOT/path).read_bytes()).hexdigest()==h, path
(PRE/'validation.json').write_text(json.dumps({'preserved_source_sha256':hashes,'source_assets_unchanged':True,'atlas_size':[N,N],'uniform_material':'Uniform','uv_channel':0,'returners':[{ 'id':r['Id'],'number':r['Profile']['JerseyNumber'],'texture':r['Uniform'],'design':DESIGNS[r['Id']]} for r in roster]},indent=2)+'\n')
print('Five returner atlases and ten previews generated; existing model/animation assets unchanged.')
