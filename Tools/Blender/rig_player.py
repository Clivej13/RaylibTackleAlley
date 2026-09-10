"""Rebuild Stage 6 rig and static inspection actions via Blender MCP. No manual steps.
Preserves source geometry, flat normals, materials, and equipment components.
World convention: +Y up, -Z forward; bone local Y follows its length.
"""
from pathlib import Path
import bpy, math, json, hashlib
from mathutils import Vector, Quaternion
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Models'
PRE=OUT/'RigPreviews'; PRE.mkdir(exist_ok=True)
source=OUT/'lowpoly_human_stage6.blend'
source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source))
scene=bpy.context.scene
meshes=[o for o in scene.objects if o.type=='MESH']
signatures={o.name:([tuple(v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons]) for o in meshes}
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
bpy.ops.object.select_all(action='DESELECT')
data=bpy.data.armatures.new('PlayerSkeleton'); rig=bpy.data.objects.new('PlayerRig',data)
scene.collection.objects.link(rig); rig.show_in_front=True
bpy.context.view_layer.objects.active=rig; rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
def bone(n,h,t,p=None,deform=True):
 b=data.edit_bones.new(n); b.head=h; b.tail=t; b.use_deform=deform
 b.align_roll(Vector((0,0,1)))
 if p: b.parent=data.edit_bones[p]
bone('Root',(0,0,0),(0,.20,0),deform=False)
bone('Hips',(0,.94,.01),(0,1.065,.01),'Root')
bone('Spine',(0,1.065,.01),(0,1.275,.01),'Hips')
bone('Chest',(0,1.275,.01),(0,1.55,.01),'Spine')
bone('Neck',(0,1.55,.01),(0,1.635,.018),'Chest')
bone('Head',(0,1.635,.018),(0,1.84,.018),'Neck')
for s,side in [(1,'L'),(-1,'R')]:
 bone('Clavicle.'+side,(s*.045,1.50,.007),(s*.221,1.390,.007),'Chest')
 bone('UpperArm.'+side,(s*.221,1.390,.007),(s*.366,1.151,.005),'Clavicle.'+side)
 bone('LowerArm.'+side,(s*.366,1.151,.005),(s*.493,.922,-.015),'UpperArm.'+side)
 bone('Hand.'+side,(s*.493,.922,-.015),(s*.548,.805,-.017),'LowerArm.'+side)
 bone('UpperLeg.'+side,(s*.104,.89,.01),(s*.153,.529,-.004),'Hips')
 bone('LowerLeg.'+side,(s*.153,.529,-.004),(s*.175,.12,.01),'UpperLeg.'+side)
 bone('Foot.'+side,(s*.175,.12,.01),(s*.176,.045,-.18),'LowerLeg.'+side)
bpy.ops.object.mode_set(mode='OBJECT')
def ramp(x,a,b):
 t=max(0,min(1,(x-a)/(b-a))); return t*t*(3-2*t)
def mix(a,b,t): return {a:1-t,b:t}
def torso(y):
 if y<1.15:return mix('Hips','Spine',ramp(y,1.015,1.15))
 return mix('Spine','Chest',ramp(y,1.17,1.34))
def weights(o,p):
 n=o.name; x,y,z=p; side='L' if x>=0 else 'R'
 if n in ('Pelvis','Groin','Glutes'):
  t=(1-ramp(y,.82,.94))*ramp(abs(x),.025,.10)*.75
  return mix('Hips','UpperLeg.'+side,t)
 if n in ('LowerBack','Belly','LeftChest','RightChest','UpperBack'): return torso(y)
 if n=='NeckBase':
  if y<1.59:return mix('Chest','Neck',ramp(y,1.55,1.59))
  return mix('Neck','Head',ramp(y,1.59,1.63))
 for prefix,side in [('Left','L'),('Right','R')]:
  if not n.startswith(prefix):continue
  part=n[len(prefix):]
  if part in ('Thigh','Knee','Calf','Ankle','Foot'):
   if part=='Foot':return {'Foot.'+side:1}
   if y>.79:return mix('UpperLeg.'+side,'Hips',ramp(y,.79,.965))
   if y>.40:return mix('LowerLeg.'+side,'UpperLeg.'+side,ramp(y,.47,.59))
   return mix('Foot.'+side,'LowerLeg.'+side,ramp(y,.09,.19))
  if part in ('Shoulder','UpperArm','Forearm','Hand'):
   if part=='Hand':return {'Hand.'+side:1}
   if y>1.34:return mix('UpperArm.'+side,'Clavicle.'+side,ramp(y,1.34,1.47))
   if y>1.02:return mix('LowerArm.'+side,'UpperArm.'+side,ramp(y,1.105,1.195))
   return mix('Hand.'+side,'LowerArm.'+side,ramp(y,.91,.965))
 return {'Head':1}
def skin(o,fn):
 o.parent=rig
 groups={b.name:o.vertex_groups.get(b.name) or o.vertex_groups.new(name=b.name) for b in data.bones if b.use_deform}
 for v in o.data.vertices:
  for n,w in fn(v).items():
   if w>1e-7:groups[n].add([v.index],w,'REPLACE')
 mod=o.modifiers.new('Player linear skin','ARMATURE'); mod.object=rig
 mod.use_deform_preserve_volume=False
equipment=['Helmet','Facemask','Visor','ChinStrap']
assembly=bpy.data.objects.new('HelmetAssembly',None); scene.collection.objects.link(assembly)
bpy.context.view_layer.update()
assembly.parent=rig; assembly.parent_type='BONE'; assembly.parent_bone='Head'
bpy.context.view_layer.update(); assembly.matrix_world.identity()
assembly['detachable_equipment']=True
for o in meshes:
 if o.name in equipment:
  world=o.matrix_world.copy(); o.parent=assembly; o.matrix_world=world
 elif o.name=='ShoulderPads':
  # Each disconnected shell region is rigid: caps follow clavicles, plates chest.
  regions={g.name:{v.index for v in o.data.vertices if any(e.group==g.index for e in v.groups)} for g in o.vertex_groups}
  def padweight(v):
   for prefix,side in [('Left','L'),('Right','R')]:
    if v.index in regions[prefix+'ShoulderCap'] or v.index in regions[prefix+'CapPadding']:return {'Clavicle.'+side:1}
   return {'Chest':1}
  skin(o,padweight); o['equipment']='Rigid chest plates and articulated rigid clavicle caps; detachable mesh'
 else:skin(o,lambda v,o=o:weights(o,o.matrix_world@v.co))
rig['axes']='+Y up; -Z forward; +X character left; local Y along bone; local Z aligned to world +Z where possible'
rig['source']=source.name; rig['pose_usage']='Single-frame deformation tests only; no gameplay animations'
def rot(n,axis,deg):
 p=rig.pose.bones[n]; local=data.bones[n].matrix_local.to_3x3().inverted()@Vector(axis)
 p.rotation_quaternion=Quaternion(local,math.radians(deg))@p.rotation_quaternion
poses={
 'Pose_Neutral':[],
 'Pose_Athletic':[('UpperLeg.L','x',38),('UpperLeg.R','x',38),('LowerLeg.L','x',-65),('LowerLeg.R','x',-65),('Foot.L','x',27),('Foot.R','x',27),('Spine','x',-12),('UpperArm.L','x',35),('UpperArm.R','x',35),('LowerArm.L','x',65),('LowerArm.R','x',65)],
 'Pose_KneeRaise':[('UpperLeg.L','x',85),('LowerLeg.L','x',-105),('Foot.L','x',20),('UpperLeg.R','x',12),('LowerLeg.R','x',-20),('Foot.R','x',8)],
 'Pose_ArmsForward':[('Clavicle.L','y',18),('Clavicle.R','y',-18),('UpperArm.L','x',82),('UpperArm.R','x',82),('LowerArm.L','x',25),('LowerArm.R','x',25)],
 'Pose_CrossBody':[('Clavicle.L','y',25),('UpperArm.L','x',85),('UpperArm.L','y',65),('LowerArm.L','x',55),('Clavicle.R','y',-18),('UpperArm.R','x',-20)],
 'Pose_TorsoTwist':[('Spine','y',18),('Chest','y',25),('Head','y',-18)],
 'Pose_HeadLean':[('Chest','z',5),('Neck','z',12),('Head','z',10),('Head','y',30),('Clavicle.L','z',-6)],
 'Pose_ShoulderDip':[('Spine','z',10),('Chest','z',8),('Clavicle.L','z',-12),('Clavicle.R','z',-8),('Head','z',-12)]}
axes={'x':(1,0,0),'y':(0,1,0),'z':(0,0,1)}
report={'source':source.name,'source_sha256':source_hash,'bones':{b.name:b.parent.name if b.parent else None for b in data.bones},'poses':{},'manual_weight_painting':'None; deterministic anatomical weight ramps and rigid equipment regions.'}
scene.render.resolution_x=640; scene.render.resolution_y=720; scene.render.resolution_percentage=100
scene.eevee.taa_render_samples=32
scene.frame_start=scene.frame_end=1
for name,changes in poses.items():
 rig.animation_data_create(); rig.animation_data.action=None
 for p in rig.pose.bones:
  p.rotation_mode='QUATERNION'; p.rotation_quaternion=(1,0,0,0); p.location=(0,0,0); p.scale=(1,1,1)
 for n,axis,deg in changes:rot(n,axes[axis],deg)
 bpy.context.view_layer.update()
 # Translate hips only, leaving the root at the world origin. Supporting sole Y=0.
 feet=['RightFoot'] if name=='Pose_KneeRaise' else ['LeftFoot','RightFoot']
 dg=bpy.context.evaluated_depsgraph_get()
 low=min((o.matrix_world@v.co).y for fn in feet for o in [bpy.data.objects[fn].evaluated_get(dg)] for v in o.data.vertices)
 rig.pose.bones['Hips'].location.y=-low
 action=bpy.data.actions.new(name); action.use_fake_user=True; action['purpose']='STATIC DEFORMATION INSPECTION ONLY'; rig.animation_data.action=action
 for p in rig.pose.bones:
  for prop in ('location','rotation_quaternion','scale'):p.keyframe_insert(data_path=prop,frame=1,group=p.name)
 bpy.context.view_layer.update(); dg=bpy.context.evaluated_depsgraph_get()
 report['poses'][name]={'support_sole_y':min((o.matrix_world@v.co).y for fn in feet for o in [bpy.data.objects[fn].evaluated_get(dg)] for v in o.data.vertices),'hip_offset_y':-low}
 for view in ('Front','FrontThreeQuarter'):
  scene.camera=bpy.data.objects[view]; scene.render.filepath=str(PRE/(name+'_'+view+'.png'))
  bpy.ops.render.render(write_still=True)
for o in meshes:
 assert signatures[o.name]==([tuple(v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons]),o.name
 if o.name not in equipment:
  ids={g.index for g in o.vertex_groups if g.name in data.bones and data.bones[g.name].use_deform}
  assert all(abs(sum(g.weight for g in v.groups if g.group in ids)-1)<1e-5 for v in o.data.vertices),o.name
assert len(data.bones)==20
assert all(abs(v['support_sole_y'])<1e-5 for v in report['poses'].values())
assert hashlib.sha256(source.read_bytes()).hexdigest()==source_hash
rig.animation_data.action=bpy.data.actions['Pose_Neutral']; scene.frame_set(1)
scene.camera=bpy.data.objects['FrontThreeQuarter']
scene['rig_validation']='Geometry unchanged; normalized linear skin weights; rigid helmet; rigid pad regions; 8 single-frame test actions; root at origin.'
scene['stage_notes']='Rigged Stage 6. Separate overlapping source anatomical meshes preserved. Inspect joint seams before animation lock.'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_rigged.blend'))
(PRE/'validation.json').write_text(json.dumps(report,indent=2))
print('RIG_COMPLETE',json.dumps(report))

