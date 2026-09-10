"""Create only Sprint on the approved rig through Blender MCP; no rig/mesh edits.
20 intervals at 30 fps, keys 1..21, preview playback 1..20 (no duplicate endpoint).
Adapted from the approved Run generator; loads Run and Jog without editing its curves.
Existing +Y-up coordinates are retained in GLB with export_yup=False.
"""
from pathlib import Path
import bpy, math, json, hashlib, struct
from mathutils import Vector, Quaternion
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Models'
PRE=OUT/'SprintPreviews'; PRE.mkdir(exist_ok=True)
SOURCE=OUT/'lowpoly_human_run.blend'
source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene; rig=bpy.data.objects['PlayerRig']
prior=set(bpy.data.actions.keys())
action=bpy.data.actions.new('Sprint'); action.use_fake_user=True
rig.animation_data.action=action
action['description']='In-place highest-tier football sprint; 20 intervals at 30 fps; duplicate closing key at 21.'
rig['pose_usage']='Sprint active; approved Jog, Run and static Pose_* inspection actions preserved'
scene.render.fps=30; scene.render.fps_base=1
scene.frame_start=1; scene.frame_end=20
def rotate(name,axis,degrees):
 p=rig.pose.bones[name]
 v=rig.data.bones[name].matrix_local.to_3x3().inverted()@Vector(axis)
 p.rotation_quaternion=Quaternion(v,math.radians(degrees))@p.rotation_quaternion
def curve(t,values):
 # Periodic cubic Hermite interpolation: matching endpoint value and velocity.
 x=(t%1)*len(values); i=int(x); f=x-i; n=len(values)
 a,b=values[i%n],values[(i+1)%n]
 m0=(b-values[(i-1)%n])*.5; m1=(values[(i+2)%n]-a)*.5
 return (2*f**3-3*f*f+1)*a+(f**3-2*f*f+f)*m0+(-2*f**3+3*f*f)*b+(f**3-f*f)*m1
samples=[]
for frame in range(1,22):
 t=(frame-1)/20; phase=2*math.pi*t
 for p in rig.pose.bones:
  p.rotation_mode='QUATERNION'; p.rotation_quaternion=(1,0,0,0); p.location=(0,0,0); p.scale=(1,1,1)
 rotate('Hips',(0,1,0),4*math.cos(phase))
 rotate('Spine',(1,0,0),-16)
 rotate('Chest',(0,1,0),-8*math.cos(phase))
 rotate('Head',(0,1,0),4*math.cos(phase))
 rotate('Head',(1,0,0),16)
 for side,offset,sign in [('L',0,1),('R',.5,-1)]:
  u=(t+offset)%1
  # Advance stance/push-off slightly; periodic warp retains half-cycle symmetry.
  v=u+0.04*math.sin(2*math.pi*u)
  hip=curve(v,[40,12,-24,-34,5,48])
  knee=curve(v,[-33,-43,-26,-72,-100,-61])
  # Preserve the saved Run toe-down propulsion, using explicit six-phase values.
  # At extension: shank -50 deg, foot -92 deg => 42 deg plantarflexion.
  # Recovery returns toward neutral; no inverted push-off direction.
  pitch=curve(v,[-7,0,-92,-80,-18,-7])
  rotate('UpperLeg.'+side,(1,0,0),hip)
  rotate('LowerLeg.'+side,(1,0,0),knee)
  rotate('Foot.'+side,(1,0,0),pitch-hip-knee)
  # Narrow the approved A-pose through bone rotation only, leaving pad clearance.
  rotate('UpperArm.'+side,(0,0,1),-sign*19)
  rotate('UpperArm.'+side,(1,0,0),-36*math.cos(2*math.pi*u))
  rotate('LowerArm.'+side,(1,0,0),80+10*math.cos(2*math.pi*u))
 bpy.context.view_layer.update()
 dg=bpy.context.evaluated_depsgraph_get()
 lows=[]
 for name in ['LeftFoot','RightFoot']:
  o=bpy.data.objects[name].evaluated_get(dg)
  lows.append(min((o.matrix_world@v.co).y for v in o.data.vertices))
 # Ground the lower sole; body bob comes entirely from Hips. No X/Z travel.
 rig.pose.bones['Hips'].location.y=-min(lows)
 for p in rig.pose.bones:
  for prop in ('rotation_quaternion','location','scale'):p.keyframe_insert(data_path=prop,frame=frame,group=p.name)
 samples.append({'frame':frame,'hips_y':-min(lows),'sole_y':[v-min(lows) for v in lows]})
for fc in action.fcurves:
 for k in fc.keyframe_points:k.interpolation='LINEAR'
 fc.modifiers.new('CYCLES')
# Conservative per-interval clearance correction for linear runtime interpolation.
# Only Hips Y changes; adjacent frames cover each measured between-key sole dip.
clearance=[0.0]*20
for i in range(20):
 for sub in (0.125,0.25,0.375,0.5,0.625,0.75,0.875):
  scene.frame_set(i+1,subframe=sub); dg=bpy.context.evaluated_depsgraph_get()
  low=min((o.matrix_world@v.co).y for name in ['LeftFoot','RightFoot'] for o in [bpy.data.objects[name].evaluated_get(dg)] for v in o.data.vertices)
  lift=max(0.0,-low+0.0001)
  clearance[i]=max(clearance[i],lift)
  clearance[(i+1)%20]=max(clearance[(i+1)%20],lift)
# Enforce exact half-cycle equality despite floating-point grounding noise.
for i in range(10): clearance[i]=clearance[i+10]=max(clearance[i],clearance[i+10])
fc=next(f for f in action.fcurves if f.data_path=='pose.bones["Hips"].location' and f.array_index==1)
for k in fc.keyframe_points: k.co.y+=clearance[(int(k.co.x)-1)%20]
for sample in samples:
 lift=clearance[(sample['frame']-1)%20]
 sample['hips_y']+=lift; sample['sole_y']=[v+lift for v in sample['sole_y']]
scene.timeline_markers.clear()
for f,label in [(1,'Left contact'),(4,'Left passing'),(7,'Left push-off / right knee forward'),(11,'Right contact'),(14,'Right passing'),(17,'Right push-off / left knee forward'),(21,'Loop closure')]:
 scene.timeline_markers.new(label,frame=f)
scene.render.engine='BLENDER_EEVEE'; scene.eevee.taa_render_samples=48
scene.render.resolution_x=640; scene.render.resolution_y=720; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
for frame,label,view in [(1,'LeftContact','Front'),(4,'LeftPassing','Side'),(7,'LeftPushOff','Side'),(11,'RightContact','Front'),(14,'RightPassing','FrontThreeQuarter'),(17,'RightPushOff','FrontThreeQuarter'),(1,'Stride','FrontThreeQuarter')]:
 scene.frame_set(frame); scene.camera=bpy.data.objects[view]
 scene.render.filepath=str(PRE/('Sprint_'+label+'_'+view+'.png')); bpy.ops.render.render(write_still=True)
# Evaluate full loop and endpoint geometry, not only animation channel values.
mesh_objects=[o for o in scene.objects if o.type=='MESH']
def snapshot(frame):
 scene.frame_set(frame); dg=bpy.context.evaluated_depsgraph_get()
 return [(o.matrix_world@v.co).copy() for src in mesh_objects for o in [src.evaluated_get(dg)] for v in o.data.vertices]
start=snapshot(1); end=snapshot(21)
error=max((a-b).length for a,b in zip(start,end))
assert error<1e-5
assert set(bpy.data.actions.keys())==prior|{'Sprint'}
assert 'Jog' in bpy.data.actions and 'Run' in bpy.data.actions
for f in range(1,22):
 scene.frame_set(f)
 assert rig.pose.bones['Root'].location.length<1e-8
 assert rig.pose.bones['Root'].rotation_quaternion.angle<1e-8
 assert abs(rig.pose.bones['Hips'].location.x)+abs(rig.pose.bones['Hips'].location.z)<1e-8
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_hash
scene.frame_set(1); scene.camera=bpy.data.objects['FrontThreeQuarter']
scene['stage_notes']='Approved Stage 6 rig plus in-place Sprint. Geometry, weights, skeleton and equipment parenting unchanged.'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_sprint.blend'))
# Export only Sprint; inspection actions remain retained in the .blend.
for other in list(bpy.data.actions):
 if other!=action:bpy.data.actions.remove(other)
rig.animation_data.action=action
bpy.ops.object.select_all(action='DESELECT')
for o in scene.objects:
 if o.type in {'MESH','ARMATURE','EMPTY'}:o.select_set(True)
scene.frame_end=21
bpy.ops.export_scene.gltf(filepath=str(OUT/'lowpoly_human_sprint_validation.glb'),export_format='GLB',use_selection=True,export_yup=False,export_animations=True,export_nla_strips=False,export_frame_range=True,export_force_sampling=True,export_skins=True,export_current_frame=False)
# Blender 3.3 merges rigid equipment channels into an action named Animation.
# Keep those channels in the single Sprint clip and normalize its metadata name.
glb=OUT/'lowpoly_human_sprint_validation.glb'; raw=glb.read_bytes()
json_size=struct.unpack_from('<I',raw,12)[0]; doc=json.loads(raw[20:20+json_size])
assert len(doc['animations'])==1
doc['animations'][0]['name']='Sprint'
payload=json.dumps(doc,separators=(',',':')).encode(); payload+=b' '*((-len(payload))%4)
tail=raw[20+json_size:]
glb.write_bytes(struct.pack('<4sII',b'glTF',2,20+len(payload)+len(tail))+struct.pack('<II',len(payload),0x4e4f534a)+payload+tail)
scene.frame_end=20
scene.render.image_settings.file_format='FFMPEG'; scene.render.ffmpeg.format='MPEG4'; scene.render.ffmpeg.codec='H264'
scene.render.ffmpeg.constant_rate_factor='HIGH'; scene.render.filepath=str(PRE/'Sprint_loop.mp4')
bpy.ops.render.render(animation=True)
(PRE/'validation.json').write_text(json.dumps({'source':SOURCE.name,'source_sha256':source_hash,'action':'Sprint','fps':30,'keyed_range':[1,21],'playback_range':[1,20],'duration_seconds':20/30,'endpoint_mesh_error_m':error,'root_translation':False,'rig_weight_corrections':None,'samples':samples},indent=2))
print('SPRINT_COMPLETE endpoint_error=',error)


