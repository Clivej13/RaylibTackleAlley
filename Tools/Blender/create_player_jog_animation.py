"""Create only Jog on the approved rig through Blender MCP; no rig/mesh edits.
28 intervals at 30 fps, keys 1..29, preview playback 1..28 (no duplicate endpoint).
Existing +Y-up coordinates are retained in GLB with export_yup=False.
"""
from pathlib import Path
import bpy, math, json, hashlib, struct
from mathutils import Vector, Quaternion
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Models'
PRE=OUT/'JogPreviews'; PRE.mkdir(exist_ok=True)
SOURCE=OUT/'lowpoly_human_rigged.blend'
source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene; rig=bpy.data.objects['PlayerRig']
prior=set(bpy.data.actions.keys())
action=bpy.data.actions.new('Jog'); action.use_fake_user=True
rig.animation_data.action=action
action['description']='In-place lowest-tier football jog; 28 intervals at 30 fps; duplicate closing key at 29.'
rig['pose_usage']='Jog locomotion plus preserved static Pose_* inspection actions'
scene.render.fps=30; scene.render.fps_base=1
scene.frame_start=1; scene.frame_end=28
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
for frame in range(1,30):
 t=(frame-1)/28; phase=2*math.pi*t
 for p in rig.pose.bones:
  p.rotation_mode='QUATERNION'; p.rotation_quaternion=(1,0,0,0); p.location=(0,0,0); p.scale=(1,1,1)
 rotate('Hips',(0,1,0),2*math.cos(phase))
 rotate('Spine',(1,0,0),-5)
 rotate('Chest',(0,1,0),-4*math.cos(phase))
 rotate('Head',(0,1,0),2*math.cos(phase))
 rotate('Head',(1,0,0),5)
 for side,offset,sign in [('L',0,1),('R',.5,-1)]:
  u=(t+offset)%1
  hip=curve(u,[23,8,-12,-20,0,26])
  knee=curve(u,[-24,-30,-20,-48,-65,-43])
  # Flat planted foot, then heel lift and relaxed swing dorsiflexion.
  pitch=curve(u,[-7,0,-50,-15,-30,-10])
  rotate('UpperLeg.'+side,(1,0,0),hip)
  rotate('LowerLeg.'+side,(1,0,0),knee)
  rotate('Foot.'+side,(1,0,0),pitch-hip-knee)
  # Narrow the approved A-pose through bone rotation only, leaving pad clearance.
  rotate('UpperArm.'+side,(0,0,1),-sign*19)
  rotate('UpperArm.'+side,(1,0,0),-16*math.cos(2*math.pi*u))
  rotate('LowerArm.'+side,(1,0,0),66+5*math.cos(2*math.pi*u))
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
for f,label in [(1,'Left contact'),(5,'Left passing'),(10,'Left push-off / right knee forward'),(15,'Right contact'),(19,'Right passing'),(24,'Right push-off / left knee forward'),(29,'Loop closure')]:
 scene.timeline_markers.new(label,frame=f)
scene.render.engine='BLENDER_EEVEE'; scene.eevee.taa_render_samples=48
scene.render.resolution_x=640; scene.render.resolution_y=720; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
for frame,label,view in [(1,'LeftContact','Front'),(5,'LeftPassing','Side'),(10,'LeftPushOff','Side'),(15,'RightContact','Front'),(19,'RightPassing','FrontThreeQuarter'),(24,'RightPushOff','FrontThreeQuarter'),(1,'Stride','FrontThreeQuarter')]:
 scene.frame_set(frame); scene.camera=bpy.data.objects[view]
 scene.render.filepath=str(PRE/('Jog_'+label+'_'+view+'.png')); bpy.ops.render.render(write_still=True)
# Evaluate full loop and endpoint geometry, not only animation channel values.
mesh_objects=[o for o in scene.objects if o.type=='MESH']
def snapshot(frame):
 scene.frame_set(frame); dg=bpy.context.evaluated_depsgraph_get()
 return [(o.matrix_world@v.co).copy() for src in mesh_objects for o in [src.evaluated_get(dg)] for v in o.data.vertices]
start=snapshot(1); end=snapshot(29)
error=max((a-b).length for a,b in zip(start,end))
assert error<1e-5
assert set(bpy.data.actions.keys())==prior|{'Jog'}
assert not any(n in bpy.data.actions for n in ['Run','Sprint'])
for f in range(1,30):
 scene.frame_set(f)
 assert rig.pose.bones['Root'].location.length<1e-8
 assert rig.pose.bones['Root'].rotation_quaternion.angle<1e-8
 assert abs(rig.pose.bones['Hips'].location.x)+abs(rig.pose.bones['Hips'].location.z)<1e-8
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_hash
scene.frame_set(1); scene.camera=bpy.data.objects['FrontThreeQuarter']
scene['stage_notes']='Approved Stage 6 rig plus in-place Jog. Geometry, weights, skeleton and equipment parenting unchanged.'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_jog.blend'))
# Export only Jog; inspection actions remain retained in the .blend.
for other in list(bpy.data.actions):
 if other!=action:bpy.data.actions.remove(other)
rig.animation_data.action=action
bpy.ops.object.select_all(action='DESELECT')
for o in scene.objects:
 if o.type in {'MESH','ARMATURE','EMPTY'}:o.select_set(True)
scene.frame_end=29
bpy.ops.export_scene.gltf(filepath=str(OUT/'lowpoly_human_jog_validation.glb'),export_format='GLB',use_selection=True,export_yup=False,export_animations=True,export_nla_strips=False,export_frame_range=True,export_force_sampling=True,export_skins=True,export_current_frame=False)
# Blender 3.3 merges rigid equipment channels into an action named Animation.
# Keep those channels in the single Jog clip and normalize its metadata name.
glb=OUT/'lowpoly_human_jog_validation.glb'; raw=glb.read_bytes()
json_size=struct.unpack_from('<I',raw,12)[0]; doc=json.loads(raw[20:20+json_size])
assert len(doc['animations'])==1
doc['animations'][0]['name']='Jog'
payload=json.dumps(doc,separators=(',',':')).encode(); payload+=b' '*((-len(payload))%4)
tail=raw[20+json_size:]
glb.write_bytes(struct.pack('<4sII',b'glTF',2,20+len(payload)+len(tail))+struct.pack('<II',len(payload),0x4e4f534a)+payload+tail)
scene.frame_end=28
scene.render.image_settings.file_format='FFMPEG'; scene.render.ffmpeg.format='MPEG4'; scene.render.ffmpeg.codec='H264'
scene.render.ffmpeg.constant_rate_factor='HIGH'; scene.render.filepath=str(PRE/'Jog_loop.mp4')
bpy.ops.render.render(animation=True)
(PRE/'validation.json').write_text(json.dumps({'source':SOURCE.name,'source_sha256':source_hash,'action':'Jog','fps':30,'keyed_range':[1,29],'playback_range':[1,28],'duration_seconds':28/30,'endpoint_mesh_error_m':error,'root_translation':False,'rig_weight_corrections':None,'samples':samples},indent=2))
print('JOG_COMPLETE endpoint_error=',error)


