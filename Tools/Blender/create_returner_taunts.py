"""Five non-looping 60 Hz returner taunts. Run with Blender MCP.
Existing recovery pose, hand grip, rig and GLB exporter are reused unchanged.
"""
from pathlib import Path
import sys, math, json, hashlib, struct
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Quaternion, Vector, Matrix
from create_defender_recovery import snapshot, apply, blend, orient
from upgrade_player_hands import export
from validate_set_wrap import signature
from create_football import look
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Models'; PRE=OUT/'ReturnerTauntPreviews'
CONFIG=[('marcus-reed','TauntSpeedSalute','speed_salute'),('eli-brooks','TauntBurstPump','burst_pump'),('jalen-price','TauntHeelFlourish','heel_flourish'),('darius-stone','TauntPowerFlex','power_flex'),('noah-grant','TauntCaptainSalute','captain_salute')]
GRIP=Matrix(((-.35795751,-.61571349,.70197102,-.12157734),(.34925157,.60892960,.71219947,.02833468),(-.86596175,.50010163,-.00293202,-.00747214),(0,0,0,1)))
def rotate(r,name,axis,deg): r.pose.bones[name].rotation_quaternion=Quaternion(axis,math.radians(deg))
def main():
 PRE.mkdir(parents=True,exist_ok=True)
 outputs={f'football_player_taunt_{stem}{ext}' for _,_,stem in CONFIG for ext in ('.blend','.glb')}
 preserved={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in OUT.iterdir() if p.suffix in ('.blend','.glb') and p.name not in outputs}
 bpy.ops.wm.open_mainfile(filepath=str(OUT/'football_player_carry_run.blend'))
 bpy.context.scene.frame_set(1); carry=snapshot(bpy.data.objects['PlayerRig'])
 report={}
 for ident,name,stem in CONFIG:
  bpy.ops.wm.open_mainfile(filepath=str(OUT/'football_player_taunt_bicep_flex.blend'))
  bpy.context.preferences.filepaths.save_version=0
  s=bpy.context.scene; r=bpy.data.objects['PlayerRig']; s.frame_set(1); base=snapshot(r)
  bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_rigged.blend'))
  s=bpy.context.scene; r=bpy.data.objects['PlayerRig']
  for ob in list(s.objects):
   if ob.get('uniform_reference'):bpy.data.objects.remove(ob,do_unlink=True)
  baseline=signature()
  r.animation_data.action=None
  for bone in ('Clavicle.R','UpperArm.R','LowerArm.R','Hand.R','Fingers.R','Thumb.R'): base[bone]=carry[bone]
  apply(r,base)
  def pose(upper,lower,hand,flex=65,chest=0,head=0,heel=0):
   apply(r,base)
   rotate(r,'Chest',(1,0,0),chest); rotate(r,'Head',(1,0,0),head)
   bpy.context.view_layer.update()
   orient(r,'UpperArm.L',upper); orient(r,'LowerArm.L',lower); orient(r,'Hand.L',hand)
   rotate(r,'Fingers.L',(0,0,1),-flex); rotate(r,'Thumb.L',(0,0,1),-min(flex*.6,45))
   # Left heel flick bends the knee backwards; right support chain never changes.
   if heel:
    foot_direction=r.pose.bones['Foot.L'].tail-r.pose.bones['Foot.L'].head
    rotate(r,'LowerLeg.L',(1,0,0),heel)
    bpy.context.view_layer.update()
    orient(r,'Foot.L',foot_direction)
   bpy.context.view_layer.update(); return snapshot(r)
  low=pose((.35,-1,0),(.1,-.6,-.7),(0,-.4,-1),50)
  if ident=='marcus-reed':
   gather=pose((.8,.15,-.2),(-.15,.85,-.5),(0,.5,-.8),15,0,-3)
   peak=pose((.7,.35,-.7),(.2,.2,-1),(.1,.1,-1),12,-2,-4)
   keys=[(1,base),(15,gather),(33,peak),(61,peak),(85,low),(125,base),(157,base)]
  elif ident=='eli-brooks':
   gather=pose((.35,-.8,.1),(.1,.65,-.8),(.1,.7,-.7),80,4,2)
   peak=pose((.65,.5,-.3),(.15,1,-.2),(0,1,-.1),85,-4,-3)
   keys=[(1,base),(19,gather),(35,peak),(49,peak),(63,gather),(79,peak),(101,peak),(139,base),(157,base)]
  elif ident=='jalen-price':
   gather=pose((.8,.1,.1),(.2,.3,-1),(.1,.3,-1),18,0,-2,12)
   peak=pose((1,.25,0),(.35,.8,-.25),(.4,.5,-.3),15,-2,-3,32)
   keys=[(1,base),(19,gather),(39,peak),(55,gather),(73,peak),(89,gather),(119,base),(157,base)]
  elif ident=='darius-stone':
   gather=pose((.65,-.65,0),(.1,.3,-1),(0,.5,-1),85,3,2)
   peak=pose((1,.1,-.08),(-.25,1,-.12),(-.2,1,-.1),85,-5,4)
   squeeze=pose((1,.18,-.1),(-.35,1,-.15),(-.3,1,-.1),90,-7,4)
   keys=[(1,base),(21,gather),(51,peak),(69,squeeze),(87,peak),(107,squeeze),(121,peak),(157,base)]
  else:
   gather=pose((.6,-.4,-.1),(-.1,.7,-.7),(0,.65,-.7),12,0,0)
   peak=pose((.9,.15,-.1),(-.3,.85,-.4),(-.2,.6,-.6),10,-2,0)
   nod=pose((.9,.15,-.1),(-.3,.85,-.4),(-.2,.6,-.6),10,-2,9)
   keys=[(1,base),(27,gather),(49,peak),(65,nod),(83,peak),(105,peak),(145,base),(157,base)]
  action=bpy.data.actions.new(name); action.use_fake_user=True; r.animation_data.action=action
  s.frame_start=1;s.frame_end=157;s.render.fps=60;s.render.fps_base=1
  s.timeline_markers.clear()
  for f,_ in keys: s.timeline_markers.new('Pose '+str(f),frame=f)
  previous={}
  for f in range(1,158):
   s.frame_set(f); a,b=next((a,b) for a,b in zip(keys,keys[1:]) if a[0]<=f<=b[0]);apply(r,blend(a[1],b[1],(f-a[0])/(b[0]-a[0])))
   for p in r.pose.bones:
    if p.name in previous and p.rotation_quaternion.dot(previous[p.name])<0:p.rotation_quaternion.negate()
    previous[p.name]=p.rotation_quaternion.copy()
    for prop in ('location','rotation_quaternion','scale'):p.keyframe_insert(prop,frame=f,group=p.name)
  for fc in action.fcurves:
   for k in fc.keyframe_points:k.interpolation='LINEAR'
  action['loop']=False;action['description']=ident+'; right-hand carry retained; 2.6 seconds; stationary endpoint'
  assert signature()==baseline
  s.frame_set(1); support=r.pose.bones['Foot.R'].matrix.copy();left=r.pose.bones['Foot.L'].matrix.copy()
  support_error=0; minimum=1e6
  for tick in range(313):
   s.frame_set(1+tick//2,subframe=(tick%2)/2)
   assert all(math.isfinite(v) for p in r.pose.bones for row in p.matrix for v in row)
   assert r.pose.bones['Root'].matrix_basis==Matrix.Identity(4)
   support_error=max(support_error,max(abs(r.pose.bones['Foot.R'].matrix[i][j]-support[i][j]) for i in range(4) for j in range(4)))
   if ident!='jalen-price':assert max(abs(r.pose.bones['Foot.L'].matrix[i][j]-left[i][j]) for i in range(4) for j in range(4))<1e-5
   minimum=min(minimum,r.pose.bones['Foot.L'].head.y)
  assert support_error<1e-5
  for f in (1,157):
   s.frame_set(f)
   for n,(loc,rot,scale) in base.items():
    assert (r.pose.bones[n].location-loc).length<1e-5 and abs(r.pose.bones[n].rotation_quaternion.dot(rot))>.99999
  path=OUT/('football_player_taunt_'+stem)
  export(s,r,action,path.with_suffix('.glb'))
  raw=path.with_suffix('.glb').read_bytes(); doc=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
  assert len(doc['animations'])==1 and doc['animations'][0]['name']==name
  assert len(doc['skins'][0]['joints'])==24
  with bpy.data.libraries.load(str(OUT/'football.blend'),link=False) as (src,dst):dst.objects=['Football']
  ball=dst.objects[0];s.collection.objects.link(ball);ball.name='FootballPreview'
  ball.constraints.clear()
  s.camera=bpy.data.objects['FrontThreeQuarter'];s.camera.location=(2.8,1.8,-5.4);s.camera.data.ortho_scale=2.65;look(s.camera,(0,1,0))
  s.render.engine='BLENDER_EEVEE';s.eevee.taa_render_samples=16;s.render.resolution_x=560;s.render.resolution_y=620;s.render.resolution_percentage=100
  # Bone parenting accounts for Blender's tail offset; match the game's hand-local matrix.
  ball.parent=r;ball.parent_type='BONE';ball.parent_bone='Hand.R'
  s.frame_set(1);bpy.context.view_layer.update();ball.matrix_world=r.matrix_world@r.pose.bones['Hand.R'].matrix@GRIP
  s.frame_set(keys[2][0]);bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
  for f in (1,keys[2][0],keys[3][0],121,157):
   s.frame_set(f);s.render.image_settings.file_format='PNG';s.render.filepath=str(PRE/(stem+'_'+str(f)+'.png'));bpy.ops.render.render(write_still=True)
  # Reviewable 30 fps movie of the full 60 Hz clip.
  s.frame_step=2;s.render.resolution_x=360;s.render.resolution_y=400;s.eevee.taa_render_samples=8
  s.render.image_settings.file_format='FFMPEG';s.render.ffmpeg.format='MPEG4';s.render.ffmpeg.codec='H264';s.render.fps=30
  # Keep authored time by scaling keys and range for preview only.
  for fc in action.fcurves:
   for k in fc.keyframe_points:k.co.x=1+(k.co.x-1)/2
  s.frame_end=79;s.frame_step=1;s.render.filepath=str(PRE/(stem+'.mp4'));bpy.ops.render.render(animation=True)
  report[ident]={'action':name,'asset':'FootballPlayer'+name+'Animations','duration_seconds':2.6,'frames':157,'fps':60,'nonlooping':True,'entry_exit_match':True,'right_support_error':support_error,'minimum_left_ankle_y':minimum,'rig_mesh_weights_preserved':True,'joints':24,'single_named_glb_action':True}
 assert all(hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==h for p,h in preserved.items())
 (PRE/'validation.json').write_text(json.dumps({'status':'PASS','clips':report,'preserved_sha256':preserved},indent=2)+'\n')
 print('RETURNER_TAUNTS_PASS',json.dumps(report))
if __name__=='__main__':main()
