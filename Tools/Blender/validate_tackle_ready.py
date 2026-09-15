"""Reopen each baked clip and validate geometry, footwork, loops and export via MCP."""
from pathlib import Path
import sys, json, hashlib, math, struct
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
from create_tackle_ready import CLIPS, asset_stem, ROOT, OUT, PRE
from validate_helmet_skinning import head_relative_vertices

def signature():
 r=bpy.data.objects['PlayerRig']
 return {'bones':[(b.name,b.parent.name if b.parent else None,list(v for row in b.matrix_local for v in row),b.length) for b in r.data.bones],
 'meshes':{o.name:([tuple(v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons],[[ (g.group,g.weight) for g in v.groups] for v in o.data.vertices],o.parent.name if o.parent else None,o.parent_bone) for o in bpy.data.objects if o.type=='MESH'}}

def action_signature(a):
 return [(f.data_path,f.array_index,[(tuple(k.co),k.interpolation) for k in f.keyframe_points]) for f in a.fcurves]

def snapshot():
 dg=bpy.context.evaluated_depsgraph_get()
 return [o.matrix_world@v.co for src in bpy.context.scene.objects if src.type=='MESH' for o in [src.evaluated_get(dg)] for v in o.data.vertices]

def main():
 preserved=json.loads((ROOT/'Tools/Blender/tackle_ready_preservation.json').read_text())
 assert all(hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==h for p,h in preserved.items())
 bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_rigged.blend'))
 baseline=signature();actions={a.name:action_signature(a) for a in bpy.data.actions}
 results={}
 for name,(count,direction,stride) in CLIPS.items():
  bpy.ops.wm.open_mainfile(filepath=str(OUT/(asset_stem(name)+'.blend')))
  assert signature()==baseline
  assert all(action_signature(bpy.data.actions[n])==v for n,v in actions.items())
  scene=bpy.context.scene;rig=bpy.data.objects['PlayerRig']
  assert rig.animation_data.action.name==name
  assert not any('football' in o.name.lower() for o in scene.objects)
  assert not any(p.constraints for p in rig.pose.bones)
  torso=['Belly','LowerBack','UpperBack','LeftChest','RightChest','Pelvis','Glutes','ShoulderPads']
  pairs=[]
  for side in ['Left','Right']:
   for arm in ['Forearm','Hand']:pairs += [(side+arm,t) for t in torso+['LeftThigh','RightThigh','LeftKnee','RightKnee']]
   pairs += [(side+'UpperArm',t) for t in torso if t!='ShoulderPads']
  for a in ['Thigh','Knee','Calf','Ankle','Foot']:
   for b in ['Thigh','Knee','Calf','Ankle','Foot']:pairs.append(('Left'+a,'Right'+b))
  heights=[];widths=[];support=[];sole=[];hits=[];foot_centers=[];hip_samples=[];helm=None;helm_error=0;heading=0;knee_width=[];knee_forward=[];torso_pitch=[];head_pitch=[]
  for tick in range(count*4+1):
   scene.frame_set(1+tick//4,subframe=(tick%4)/4)
   dg=bpy.context.evaluated_depsgraph_get()
   assert rig.matrix_world==Matrix.Identity(4)
   assert rig.pose.bones['Root'].matrix_basis==Matrix.Identity(4)
   hip_samples.append(list(rig.pose.bones['Hips'].location))
   assert abs(rig.pose.bones['Hips'].location.x)+abs(rig.pose.bones['Hips'].location.z)<.22
   for n in ['Hips','Chest','Head']:
    rot=rig.pose.bones[n].matrix @ rig.data.bones[n].matrix_local.inverted()
    heading=max(heading,abs(math.atan2(rot[0][2],rot[2][2])))
   knee_width.append(rig.pose.bones['LowerLeg.L'].head.x-rig.pose.bones['LowerLeg.R'].head.x)
   for side in ['L','R']:
    knee_forward.append(rig.pose.bones['UpperLeg.'+side].head.z-rig.pose.bones['LowerLeg.'+side].head.z)
   for bone,values in [('Chest',torso_pitch),('Head',head_pitch)]:
    rotation=rig.pose.bones[bone].matrix @ rig.data.bones[bone].matrix_local.inverted()
    values.append(math.degrees(math.atan2(rotation[2][1],rotation[1][1])))
   rel=head_relative_vertices(rig,dg)
   if helm is None:helm=rel
   helm_error=max(helm_error,max((a-b).length for n in rel for a,b in zip(rel[n],helm[n])))
   cache={};floors=[]
   for n in set(n for pair in pairs for n in pair):
    o=bpy.data.objects[n].evaluated_get(dg);verts=[o.matrix_world@v.co for v in o.data.vertices]
    polygons=[list(p.vertices) for p in o.data.polygons]
    if n.endswith('UpperArm'):
     bone=rig.pose.bones['UpperArm.'+('L' if n.startswith('Left') else 'R')]
     axis=(bone.tail-bone.head).normalized()
     polygons=[ids for ids in polygons if all((verts[j]-bone.head).dot(axis)>.08 for j in ids)]
    cache[n]=BVHTree.FromPolygons(verts,polygons)
    if n.endswith('Foot'):floors.append(min(v.y for v in verts))
   for a,b in pairs:
    if cache[a].overlap(cache[b]):hits.append([1+tick/4,a,b])
   sole.extend(floors);support.append(min(floors))
   heights.append(rig.pose.bones['Hips'].head.y)
   widths.append(rig.pose.bones['Foot.L'].head.x-rig.pose.bones['Foot.R'].head.x)
   foot_centers.append({s:list(rig.pose.bones['Foot.'+s].head) for s in ['L','R']})
  scene.frame_set(1);start=snapshot();scene.frame_set(count+1);end=snapshot()
  error=max((a-b).length for a,b in zip(start,end))
  raw=(OUT/(asset_stem(name)+'.glb')).read_bytes();size=struct.unpack_from('<I',raw,12)[0];doc=json.loads(raw[20:20+size])
  assert len(doc['animations'])==1 and doc['animations'][0]['name']==name
  clip=doc['animations'][0]
  duration=max(doc['accessors'][s['input']]['max'][0] for s in clip['samplers'])-min(doc['accessors'][s['input']]['min'][0] for s in clip['samplers'])
  assert abs(duration-count/60)<1e-6
  result={'knee_width_m':[min(knee_width),max(knee_width)],'knee_forward_of_hip_m':[min(knee_forward),max(knee_forward)],'torso_pitch_degrees':[min(torso_pitch),max(torso_pitch)],'head_pitch_degrees':[min(head_pitch),max(head_pitch)],'shoulder_joint_width_m':.442,'duration_seconds':duration,'sample_spacing_frames':.25,'hips_height_m':[min(heights),max(heights)],'ankle_base_width_m':[min(widths),max(widths)],
   'minimum_sole_y_m':min(sole),'maximum_lower_sole_y_m':max(support),'maximum_heading_error_degrees':math.degrees(heading),'endpoint_mesh_error_m':error,
   'helmet_relative_mesh_error_m':helm_error,'intersection_events':hits,'collision_scope':'Distal upper arms versus torso, excluding proximal 8 cm shoulder joint; forearms/hands versus torso/pads/thighs/knees; all opposite leg parts. Adjacent anatomical joints excluded.',
   'root_identity':True,'geometry_weights_skeleton_preserved':True,'ball_free':True,'source_actions_preserved':True,'travel_direction_xz':direction,
   'support_travel_speed_mps':stride/(count/60),'foot_samples':foot_centers}
  result['hips_excursion_xyz_m']=[max(q[i] for q in hip_samples)-min(q[i] for q in hip_samples) for i in range(3)]
  if stride:
   projections=[q[0]*direction[0]+q[2]*direction[1] for q in hip_samples]
   result['directional_hips_excursion_m']=max(projections)-min(projections)
   result['virtual_step_distance_m']=stride
   lead='R' if direction[0]<0 or direction[1]>0 else 'L';push='L' if lead=='R' else 'R'
   result['lead_bone']=lead;result['push_bone']=push
   # Add the locomotion displacement back to check the planted world foot.
   samples=[]
   for tick,q in enumerate(foot_centers):
    t=tick/(count*4);samples.append([q[push][0]+direction[0]*stride*t,q[push][1],q[push][2]+direction[1]*stride*t])
   result['push_world_anchor_drift_m']=max(math.dist(q,samples[0]) for q in samples[:int(count*4*.43)+1])
   assert result['directional_hips_excursion_m']>.16
   assert result['push_world_anchor_drift_m']<.002
  else:
   assert result['hips_excursion_xyz_m'][0]>.09 and result['hips_excursion_xyz_m'][1]>.03
  (PRE/name/'validation.json').write_text(json.dumps(result,indent=2))
  results[name]={k:v for k,v in result.items() if k!='foot_samples'}
  print(name,json.dumps(results[name]))
  assert error<1e-5 and helm_error<1e-5 and heading<1e-5
  assert min(sole)>-.001 and max(support)<.003,(name,'ground',min(sole),max(support))
  assert min(widths)>.47 and max(widths)<.90
  assert min(heights)>.60 and max(heights)<.72
  assert min(knee_width)>.38 and max(knee_width)<.80
  # A trailing push knee can be behind the hip; anatomical bend is checked
  # directly rather than imposing the old stationary-foot knee position.
  bends=[]
  for frame in range(1,count+2):
   scene.frame_set(frame)
   for side in ['L','R']:
    a=rig.pose.bones['UpperLeg.'+side];b=rig.pose.bones['LowerLeg.'+side]
    bends.append(math.degrees((a.tail-a.head).angle(b.tail-b.head)))
  assert min(bends)>20 and max(bends)<135,(name,'knee_bend',min(bends),max(bends))
  result['knee_bend_degrees']=[min(bends),max(bends)]
  assert all(-18<p<-13 for p in torso_pitch)
  assert all(2<p<4 for p in head_pitch)
  assert not hits,hits[:10]
  result['validation']='PASS'
  (PRE/name/'validation.json').write_text(json.dumps(result,indent=2))
  results[name]={k:v for k,v in result.items() if k!='foot_samples'}
 (PRE/'validation.json').write_text(json.dumps({'existing_assets_and_csharp_preserved':True,'clips':results},indent=2))
 print('ALL_TACKLE_READY_VALIDATION_PASSED')

if __name__=='__main__':main()
