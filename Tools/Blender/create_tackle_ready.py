"""Five explosive, square, grounded defender breakdown loops. Run via Blender MCP.

Y is up; forward is -Z; Left/Right use the in-game rear view (-X/+X).
Virtual directional steps retain bounded pelvis bursts after uniform travel is
removed. Analytic two-bone poses are baked; skeleton and equipment are unchanged.
"""
from pathlib import Path
import sys, math, json, hashlib
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector, Quaternion
from create_player_cut_animations import rotate
from upgrade_player_hands import export

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Models'
PRE=OUT/'TackleReadyPreviews'
CLIPS={'TackleReady':(48,(0,0),0),
 'TackleReadyForward':(36,(0,-1),.38),
 'TackleReadyBackward':(36,(0,1),.34),
 'TackleReadyLeft':(36,(-1,0),.34),
 'TackleReadyRight':(36,(1,0),.34)}

def smooth(t,a,b):
 u=max(0,min(1,(t-a)/(b-a)));return u*u*(3-2*u)

def motion(t,direction,stride):
 # Author a advancing step in virtual world space, then subtract uniform
 # locomotion travel. Root remains identity and the pelvis retains the burst.
 lane=Vector((direction[0],0,direction[1]))
 load=math.sin(math.pi*min(t/.20,1))**2
 burst=smooth(t,.18,.43)
 advance=stride*(.88*burst+.12*smooth(t,.43,1))-.035*load
 hip=lane*(advance-stride*t)
 hip.y=-.28-.030*load+.042*math.sin(math.pi*burst)**2+.012*math.sin(math.pi*smooth(t,.43,1))**2
 return hip,load,burst

def asset_stem(name):
 return 'football_player_tackle_ready'+('_'+name[len('TackleReady'):].lower() if name!='TackleReady' else '')

def foot_cycle(u):
 # 35% low recovery, 65% support: there is always at least one planted foot.
 if u<.35:
  f=u/.35
  # Cubic has support-matched endpoint velocity, avoiding a stop at touchdown.
  m=-.35/.65
  position=(2*f**3-3*f*f+1)*(-.5)+(f**3-2*f*f+f)*m+(-2*f**3+3*f*f)*.5+(f**3-f*f)*m
  return position,.035*math.sin(math.pi*f)**2
 return .5-(u-.35)/.65,0

def aim(rig,name,direction):
 p=rig.pose.bones[name]; rest=p.bone
 turn=(rest.tail_local-rest.head_local).rotation_difference(Vector(direction))
 chest=rig.pose.bones['Chest'].matrix @ rig.data.bones['Chest'].matrix_local.inverted()
 m=(chest.to_3x3() @ turn.to_matrix() @ rest.matrix_local.to_3x3()).to_4x4()
 m.translation=p.head; p.matrix=m
 bpy.context.view_layer.update()

def solve_leg(rig,side,target,orientation):
 # Analytic two-bone solve: explicit forward/outward knee pole avoids the
 # previous IK twist that splayed knees almost entirely sideways.
 upper=rig.pose.bones['UpperLeg.'+side]; lower=rig.pose.bones['LowerLeg.'+side]
 start=upper.head.copy(); delta=target-start; distance=delta.length; axis=delta.normalized()
 a=upper.bone.length; b=lower.bone.length
 along=(a*a-b*b+distance*distance)/(2*distance)
 radius=math.sqrt(max(0,a*a-along*along))
 sign=1 if side=='L' else -1
 pole=Vector((sign*.30,0,-1))
 pole=(pole-axis*pole.dot(axis)).normalized()
 knee=start+axis*along+pole*radius
 for p,direction in [(upper,knee-start),(lower,target-knee)]:
  rest=p.bone
  turn=(rest.tail_local-rest.head_local).rotation_difference(direction)
  m=(turn.to_matrix() @ rest.matrix_local.to_3x3()).to_4x4()
  m.translation=p.head; p.matrix=m;p.location=(0,0,0);p.scale=(1,1,1)
  bpy.context.view_layer.update()
 foot=rig.pose.bones['Foot.'+side]
 m=orientation.to_matrix().to_4x4();m.translation=foot.head;foot.matrix=m
 foot.location=(0,0,0);foot.scale=(1,1,1)
 bpy.context.view_layer.update()
 assert (foot.head-target).length<.0001

def build(name,count,direction,stride,render=True):
 bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_rigged.blend'))
 bpy.context.preferences.filepaths.save_version=0
 scene=bpy.context.scene; rig=bpy.data.objects['PlayerRig']
 action=bpy.data.actions.new(name); action.use_fake_user=True
 rig.animation_data.action=action
 scene.render.fps=60; scene.render.fps_base=1
 scene.frame_start=1; scene.frame_end=count
 scene.timeline_markers.clear()
 action['description']='Square low/wide breakdown; no ball or root travel; grounded '+name
 for frame in range(1,count+2):
  t=(frame-1)/count; phase=2*math.pi*t
  scene.frame_set(frame)
  for p in rig.pose.bones:
   p.rotation_mode='QUATERNION';p.location=(0,0,0);p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)
  if stride:
   hip,load,burst=motion(t,direction,stride)
  else:
   load=math.sin(phase)**2;burst=0
   hip=Vector((.050*math.sin(phase),-.275-.018*math.cos(phase*2),.018*math.sin(phase*2)))
  rig.pose.bones['Hips'].location=hip
  pitch=14+3*load
  rotate(rig,'Spine',(1,0,0),-pitch)
  rotate(rig,'Head',(1,0,0),pitch+3)
  bpy.context.view_layer.update()
  for side,sign in [('L',1),('R',-1)]:
   # L is +X on this rig. Lateral lead follows travel direction, without yaw.
   lead='R' if direction[0]<0 or direction[1]>0 else 'L'
   if stride:
    step=smooth(t,.18,.43) if side==lead else smooth(t,.49,.81)
    travel=stride*(step-t);lift=(.075 if side==lead else .055)*math.sin(math.pi*step)**2
   else:
    travel=0;lift=.009*max(0,sign*math.sin(phase))**2
   stagger=(-sign*.075 if direction[1] else 0)
   target=Vector((sign*.27+direction[0]*travel,.1208+lift,.055+stagger+direction[1]*travel))
   orientation=rig.data.bones['Foot.'+side].matrix_local.to_quaternion()
   solve_leg(rig,side,target,orientation)
   breath=.035*math.sin(phase)
   aim(rig,'UpperArm.'+side,(sign*(.48+.08*load),-.85,-.15-.12*math.sin(math.pi*burst)))
   aim(rig,'LowerArm.'+side,(-sign*.08,.18+breath,-1))
   aim(rig,'Hand.'+side,(-sign*.06,.12+breath,-1))
   for bone,angle in [('Fingers',8),('Thumb',5)]:
    rig.pose.bones[bone+'.'+side].rotation_quaternion=Quaternion((0,0,1),math.radians(-sign*angle))
  for p in rig.pose.bones:
   for prop in ('location','rotation_quaternion','scale'):p.keyframe_insert(prop,frame=frame,group=p.name)
 for fc in action.fcurves:
  for k in fc.keyframe_points:k.interpolation='LINEAR'
  fc.modifiers.new('CYCLES')
 scene.frame_set(1)
 scene['stance_notes']='54 cm restored base; low bent-knee stance; timed plant/push and lead/follow step; square torso, head up. Bounded pelvis burst with identity Root.'
 scene['intended_travel_direction_xz']=direction
 scene['in_place_support_speed_mps']=stride/(count/60)
 scene['virtual_step_distance_m']=stride
 scene.timeline_markers.clear()
 for fraction,label in [(0,'WideBase'),(.10,'Load'),(.30,'Push'),(.43,'GainGround'),(.65,'Follow'),(.85,'Recover')]:scene.timeline_markers.new(label,frame=1+round(count*fraction))
 scene['reference']='Assets/References/set-up-wraptackle.jpg'
 scene.camera=bpy.data.objects['FrontThreeQuarter']
 scene.render.engine='BLENDER_EEVEE';scene.eevee.taa_render_samples=48
 scene.render.resolution_x=640;scene.render.resolution_y=720;scene.render.resolution_percentage=100
 scene.render.image_settings.file_format='PNG'
 for v in ['Front','Back','Side','FrontThreeQuarter']:bpy.data.objects[v].data.ortho_scale=2.1
 path=OUT/asset_stem(name)
 bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
 export(scene,rig,action,path.with_suffix('.glb'))
 if not render:return
 folder=PRE/name;folder.mkdir(parents=True,exist_ok=True)
 assert folder.resolve().is_relative_to(PRE.resolve())
 for old in folder.glob('*.png'):old.unlink()
 for frame in sorted(set([1,1+round(count*.175),1+round(count*.675)]+[1+round(count*f) for f in [.10,.30,.43,.65,.85]])):
  scene.frame_set(frame)
  for view in ['Front','Back','Side','FrontThreeQuarter']:
   scene.camera=bpy.data.objects[view]
   scene.render.filepath=str(folder/f'{name}_{frame:03d}_{view}.png')
   bpy.ops.render.render(write_still=True)
 scene.frame_end=count;scene.camera=bpy.data.objects['FrontThreeQuarter']
 scene.render.image_settings.file_format='FFMPEG';scene.render.ffmpeg.format='MPEG4';scene.render.ffmpeg.codec='H264'
 scene.render.ffmpeg.constant_rate_factor='HIGH';scene.render.filepath=str(folder/(name+'_loop.mp4'))
 bpy.ops.render.render(animation=True)
 print('TACKLE_READY_COMPLETE',name)

if __name__=='__main__':
 PRE.mkdir(parents=True,exist_ok=True)
 preserved={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in list(OUT.glob('*.blend'))+list(OUT.glob('*.glb'))+list(ROOT.rglob('*.cs')) if not p.name.startswith('football_player_tackle_ready')}
 (ROOT/'Tools/Blender/tackle_ready_preservation.json').write_text(json.dumps(preserved,indent=2))
 for name,(count,direction,stride) in CLIPS.items():build(name,count,direction,stride)
