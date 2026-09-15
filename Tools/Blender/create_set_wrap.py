"""Grounded 0.4 s Set/Wrap tackles, authored from the exact TackleReady entry.
Root stays identity; a short bounded Hips offset supplies weight transfer.
Directions use rear-view game convention: left=-X; rig bone L is +X.
"""
from pathlib import Path
import sys, math
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector,Quaternion
from create_tackle_ready import aim, rotate, solve_leg
from upgrade_player_hands import export
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Assets/Models';PRE=OUT/'SetWrapPreviews'
CONFIG={'SetWrapForward':(0,'L','R'),'SetWrapLeft':(35,'L','R'),'SetWrapRight':(-35,'R','L')}
PHASES=[(1,'Ready'),(5,'Load'),(11,'PowerStep'),(16,'Contact'),(21,'CloseWrap'),(25,'Settle')]

def smooth(t,a,b):
 u=max(0,min(1,(t-a)/(b-a)));return u*u*(3-2*u)

def stem(name):return 'football_player_set_wrap_'+name.removeprefix('SetWrap').lower()

def build(name,yaw,push,lead):
 bpy.ops.wm.open_mainfile(filepath=str(OUT/'football_player_tackle_ready.blend'))
 bpy.context.preferences.filepaths.save_version=0
 s=bpy.context.scene;r=bpy.data.objects['PlayerRig'];s.frame_set(1)
 entry={p.name:(p.location.copy(),p.rotation_quaternion.copy(),p.scale.copy()) for p in r.pose.bones}
 anchors={side:r.pose.bones['Foot.'+side].head.copy() for side in ['L','R']}
 action=bpy.data.actions.new(name);action.use_fake_user=True;r.animation_data.action=action
 s.frame_start=1;s.frame_end=25;s.render.fps=60;s.render.fps_base=1
 s.timeline_markers.clear()
 for f,label in PHASES:s.timeline_markers.new(label,frame=f)
 lane=Quaternion((0,1,0),math.radians(yaw))@Vector((0,0,-1))
 for frame in range(1,26):
  t=(frame-1)/24;s.frame_set(frame)
  for p in r.pose.bones:p.location,p.rotation_quaternion,p.scale=entry[p.name]
  if frame>1:
   load=smooth(t,0,.17)*(1-smooth(t,.22,.55))
   drive=smooth(t,.18,.62);close=smooth(t,.52,.92);open_=smooth(t,.12,.48)*(1-close)
   hip=r.pose.bones['Hips'];hip.location+=lane*(.11*drive)
   hip.location.y-=.026*load
   rotate(r,'Hips',(0,1,0),yaw*.63*drive)
   rotate(r,'Chest',(0,1,0),yaw*.37*drive)
   rotate(r,'Spine',(1,0,0),-9*drive)
   rotate(r,'Head',(1,0,0),9*drive)
   bpy.context.view_layer.update()
   for side in ['L','R']:
    target=anchors[side].copy()
    if side==lead:
     step=smooth(t,.20,.52);u=max(0,min(1,(t-.20)/.32))
     target+=lane*((.16 if yaw else .13)*step)
     target.y+=.022*math.sin(math.pi*u)**2
    else:
     # Rear/right (left variant) or rear/left (right variant) stays planted
     # through contact, then takes a small recovery step after lead touchdown.
     step=smooth(t,.66,.94);u=max(0,min(1,(t-.66)/.28))
     target+=lane*(.065*step)
     target.y+=.012*math.sin(math.pi*u)**2
    foot_turn=yaw*(.75 if side==lead else .35)*step
    orientation=Quaternion((0,1,0),math.radians(foot_turn))@r.data.bones['Foot.'+side].matrix_local.to_quaternion()
    solve_leg(r,side,target,orientation)
   for side,sign in [('L',1),('R',-1)]:
    def mix(a,b,w):return Vector(a).lerp(Vector(b),w)
    upper=mix((sign*.48,-.85,-.15),(sign*.90,-.45,-.25),open_)
    upper=upper.lerp(Vector((sign*.55,-.45,-.90)),close)
    lower=mix((-sign*.08,.18,-1),(sign*.20,.12,-1),open_)
    lower=lower.lerp(Vector((-sign*.85,.10,-.52)),close)
    hand=mix((-sign*.06,.12,-1),(-sign*.5,.1,-.85),close)
    aim(r,'UpperArm.'+side,upper);aim(r,'LowerArm.'+side,lower);aim(r,'Hand.'+side,hand)
    r.pose.bones['Fingers.'+side].rotation_quaternion=Quaternion((0,0,1),math.radians(-sign*(8+24*close)))
  for p in r.pose.bones:
   for prop in ['location','rotation_quaternion','scale']:p.keyframe_insert(prop,frame=frame,group=p.name)
 for fc in action.fcurves:
  for k in fc.keyframe_points:k.interpolation='LINEAR'
 action['description']='Grounded load, power step, contact, bilateral wrap, two-foot settle; nonlooping.'
 s['set_wrap_push_bone']=push;s['set_wrap_lead_bone']=lead;s['set_wrap_lane_degrees']=yaw
 s['set_wrap_root_motion']='None; bounded 11 cm Hips weight transfer, world-locked support feet.'
 s.frame_set(1);s.camera=bpy.data.objects['FrontThreeQuarter'];s.render.image_settings.file_format='PNG'
 path=OUT/stem(name);bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
 folder=PRE/name;folder.mkdir(parents=True,exist_ok=True)
 for frame,label in PHASES:
  s.frame_set(frame)
  for view in ['Front','Back','Side','FrontThreeQuarter']:
   s.camera=bpy.data.objects[view];s.render.filepath=str(folder/f'{name}_{frame:02d}_{label}_{view}.png')
   bpy.ops.render.render(write_still=True)
 export(s,r,action,path.with_suffix('.glb'))
 s.camera=bpy.data.objects['FrontThreeQuarter'];s.render.image_settings.file_format='FFMPEG'
 s.render.ffmpeg.format='MPEG4';s.render.ffmpeg.codec='H264';s.render.ffmpeg.constant_rate_factor='HIGH'
 s.render.filepath=str(folder/(name+'.mp4'));bpy.ops.render.render(animation=True)
 print('SET_WRAP_COMPLETE',name)

if __name__=='__main__':
 for name,args in CONFIG.items():build(name,*args)
