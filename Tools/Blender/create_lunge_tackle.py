"""Run-entry, in-place mid-speed tackles. Execute with Blender MCP.

Y up, -Z forward. Game right leg is rig L (+X). No source assets edited.
"""
from pathlib import Path
import sys, math, json, hashlib
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector, Quaternion, Matrix
from create_tackle_ready import aim, rotate, solve_leg
from create_set_wrap import smooth
from upgrade_player_hands import export
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Models'; PRE=OUT/'LungeTacklePreviews'
CONFIG={'LungeTackleForward':(0,'L',1),'LungeTackleLeft':(24,'L',1),'LungeTackleRight':(-24,'R',13)}
PHASES=[(1,'Run'),(7,'Plant'),(11,'PushOff'),(18,'ShoulderHit'),(25,'Wrap'),(31,'TrailDrive'),(37,'FollowThrough')]
def stem(name):return 'football_player_lunge_tackle_'+name.removeprefix('LungeTackle').lower()

def build(name,yaw,push,entry_frame):
 bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_run.blend'))
 bpy.context.preferences.filepaths.save_version=0
 s=bpy.context.scene;r=bpy.data.objects['PlayerRig'];s.frame_set(entry_frame)
 entry={p.name:(p.location.copy(),p.rotation_quaternion.copy(),p.scale.copy()) for p in r.pose.bones}
 lead='R' if push=='L' else 'L';ps=1 if push=='L' else -1
 action=bpy.data.actions.new(name);action.use_fake_user=True;r.animation_data.action=action
 s.frame_start=1;s.frame_end=37;s.render.fps=60;s.render.fps_base=1
 s.timeline_markers.clear()
 for f,label in PHASES:s.timeline_markers.new(label,frame=f)
 lane=Quaternion((0,1,0),math.radians(yaw))@Vector((0,0,-1))
 anchors={push:Vector((ps*.23,.1208,.035)),lead:Vector((-ps*.24,.1208,-.30))}
 previous={}
 for frame in range(1,38):
  s.frame_set(frame);t=(frame-1)/36
  gather=smooth(t,0,1/6);hit=smooth(t,1/6,17/36);wrap=smooth(t,17/36,27/36)
  release=smooth(t,10/36,17/36);follow=smooth(t,25/36,1)
  for p in r.pose.bones:
   p.rotation_mode='QUATERNION';p.location=(0,0,0);p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)
  hip=r.pose.bones['Hips'];hip.location.y=-.14+.09*hit-.11*follow
  hip.location+=lane*(.46*hit+.08*follow)
  # Pelvis supplies most of the launch angle: the spine stays nearly aligned.
  rotate(r,'Hips',(1,0,0),-58*hit+8*follow)
  rotate(r,'Hips',(0,1,0),yaw*hit)
  rotate(r,'Spine',(1,0,0),-14+4*hit)
  rotate(r,'Chest',(0,1,0),ps*5*hit*(1-.6*wrap))
  rotate(r,'Head',(1,0,0),17+54*hit-8*follow)
  bpy.context.view_layer.update()
  for side in ['L','R']:
   target=anchors[side].copy()
   if side==lead:
    step=smooth(t,1/6,10/36);target=Vector((-ps*.22,.1208,.12)).lerp(Vector((-ps*.24,.20,-.16)),step)
   sign=1 if side=='L' else -1
   # Both feet leave after the correct support leg extends. Feet stay behind
   # the advancing pelvis, with asymmetric knee recoil and a late drive.
   trail=.48+(.05 if side==push else -.06)*math.sin(math.pi*wrap)-.13*follow
   drop=.48+(.05 if side==push else -.05)*math.sin(math.pi*wrap)+.06*follow
   airborne=hip.head.copy()+lane*(-trail)+Vector((sign*.18,-drop,0))
   target=target.lerp(airborne,release)
   orientation=Quaternion((0,1,0),math.radians(yaw*hit))@Quaternion((1,0,0),math.radians(-32*release))@r.data.bones['Foot.'+side].matrix_local.to_quaternion()
   solve_leg(r,side,target,orientation)
  for side,sign in [('L',1),('R',-1)]:
   # Raise the reach relative to the pitched chest so hands surround the
   # runner ahead of the shoulder, rather than dangling toward the ground.
   upper=Vector((sign*.85,-.50,-.25)).lerp(Vector((sign*.85,-.15,-.10)),hit).lerp(Vector((sign*.55,.50,-.55)),wrap)
   lower=Vector((sign*.15,.16,-1)).lerp(Vector((sign*.15,.85,-.45)),hit).lerp(Vector((-sign*.85,.55,-.25)),wrap)
   hand=Vector((-sign*.05,.12,-1)).lerp(Vector((-sign*.05,.85,-.40)),hit).lerp(Vector((-sign*.30,.70,-.25)),wrap)
   aim(r,'UpperArm.'+side,upper);aim(r,'LowerArm.'+side,lower);aim(r,'Hand.'+side,hand)
   r.pose.bones['Fingers.'+side].rotation_quaternion=Quaternion((0,0,1),math.radians(-sign*(8+22*wrap)))
  # Exact Run start; smooth, short transition into the gathered plant.
  if frame<=7:
   for p in r.pose.bones:
    loc,rot,scale=entry[p.name];p.location=loc.lerp(p.location,gather);p.rotation_quaternion=rot.slerp(p.rotation_quaternion,gather)
  for p in r.pose.bones:
   if p.name in previous and p.rotation_quaternion.dot(previous[p.name])<0:p.rotation_quaternion.negate()
   previous[p.name]=p.rotation_quaternion.copy()
   for prop in ['location','rotation_quaternion','scale']:p.keyframe_insert(prop,frame=frame,group=p.name)
 for fc in action.fcurves:
  for k in fc.keyframe_points:k.interpolation='LINEAR'
 # Quaternion blending from Run can dip a sole during the gather. Correct only
 # the gather's Hips Y, including interpolated samples, preserving exact entry.
 fc=next(c for c in action.fcurves if c.data_path=='pose.bones["Hips"].location' and c.array_index==1)
 for iteration in range(3):
  for tick in range(1,24):
   frame=1+tick//4;sub=(tick%4)/4;s.frame_set(frame,subframe=sub)
   dg=bpy.context.evaluated_depsgraph_get()
   low=min((o.matrix_world@v.co).y for n in ['LeftFoot','RightFoot'] for o in [bpy.data.objects[n].evaluated_get(dg)] for v in o.data.vertices)
   if low<.0001:
    ids=[i for i in [frame-1,frame] if 0<i<6]
    weight=sum((1-sub if i==frame-1 else sub) for i in ids)
    if weight>0:
     for i in ids:fc.keyframe_points[i].co.y+=(.0001-low)/weight
 action['description']='Run/gather, correct-leg plant, pelvis-driven 68-degree whole-body launch, airborne shoulder hit, delayed wrap and trailing-leg drive. Identity Root; bounded 54 cm pelvis transfer.'
 s['lunge_push_bone']=push;s['lunge_run_entry_frame']=entry_frame;s['lunge_lane_degrees']=yaw
 s.render.engine='BLENDER_EEVEE';s.eevee.taa_render_samples=24
 s.render.resolution_x=480;s.render.resolution_y=540;s.render.resolution_percentage=100
 s.render.image_settings.file_format='PNG'
 for view in ['Front','Back','Side','FrontThreeQuarter']:
  camera=bpy.data.objects[view];camera.data.ortho_scale=2.7
  forward=(Vector((0,.95,-.30))-camera.location).normalized()
  right=forward.cross(Vector((0,1,0))).normalized();up=right.cross(forward)
  camera.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
 s.frame_set(1);s.camera=bpy.data.objects['FrontThreeQuarter']
 path=OUT/stem(name);bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
 export(s,r,action,path.with_suffix('.glb'))
 print('LUNGE_ASSET_COMPLETE',name,flush=True)

def previews(name):
 bpy.ops.wm.open_mainfile(filepath=str(OUT/(stem(name)+'.blend')))
 s=bpy.context.scene;folder=PRE/name;folder.mkdir(parents=True,exist_ok=True)
 assert folder.resolve().is_relative_to(PRE.resolve())
 for old in folder.glob('*.png'):old.unlink()
 for frame,label in PHASES:
  s.frame_set(frame)
  for view in ['Front','Back','Side','FrontThreeQuarter']:
   s.camera=bpy.data.objects[view];s.render.filepath=str(folder/f'{name}_{frame:02d}_{label}_{view}.png');bpy.ops.render.render(write_still=True)
 print('LUNGE_PREVIEWS_COMPLETE',name,flush=True)

if __name__=='__main__':
 PRE.mkdir(parents=True,exist_ok=True)
 preserve={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in list(OUT.glob('*.blend'))+list(OUT.glob('*.glb'))+list(ROOT.rglob('*.cs')) if 'lunge_tackle' not in p.name}
 (PRE/'preservation.json').write_text(json.dumps(preserve,indent=2))
 for name,args in CONFIG.items():build(name,*args)
 for name in CONFIG:previews(name)
