"""Validate geometry, preserved inputs, quarter-frame collisions and tackle mechanics."""
from pathlib import Path
import sys,json,hashlib,math,struct
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
from create_lunge_tackle import CONFIG,ROOT,OUT,PRE,stem
from validate_set_wrap import signature,action_signature,snapshot
from validate_helmet_skinning import head_relative_vertices

def main():
 preserved=json.loads((PRE/'preservation.json').read_text())
 assert all(hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==h for p,h in preserved.items())
 bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_run.blend'))
 baseline=signature();actions={a.name:action_signature(a) for a in bpy.data.actions};entries={}
 for f in [1,13]:bpy.context.scene.frame_set(f);entries[f]=snapshot()
 results={}
 for name,(yaw,push,entry_frame) in CONFIG.items():
  bpy.ops.wm.open_mainfile(filepath=str(OUT/(stem(name)+'.blend')))
  s=bpy.context.scene;r=bpy.data.objects['PlayerRig'];s.frame_set(1)
  assert signature()==baseline
  assert all(action_signature(bpy.data.actions[n])==v for n,v in actions.items())
  entry_error=max((a-b).length for a,b in zip(entries[entry_frame],snapshot()))
  torso=['Belly','LowerBack','UpperBack','LeftChest','RightChest','Pelvis','Glutes','ShoulderPads']
  pairs=[]
  for side in ['Left','Right']:
   for arm in ['Forearm','Hand']:pairs += [(side+arm,t) for t in torso+['LeftThigh','RightThigh','LeftKnee','RightKnee','Helmet','Facemask'] if t in bpy.data.objects]
   pairs += [(side+'UpperArm',t) for t in torso if t!='ShoulderPads']
  for a in ['Thigh','Knee','Calf','Ankle','Foot']:
   for b in ['Thigh','Knee','Calf','Ankle','Foot']:pairs.append(('Left'+a,'Right'+b))
  for a in ['Forearm','Hand']:
   for b in ['Forearm','Hand']:pairs.append(('Left'+a,'Right'+b))
  hits=[];head_pitch=[];chest_pitch=[];soles=[];feet=[];helmet=None;helmet_error=0
  body_angles=[];torso_angles=[];pelvis_angles=[];hip_travel=[];airborne=[]
  for tick in range(145):
   s.frame_set(1+tick//4,subframe=(tick%4)/4);dg=bpy.context.evaluated_depsgraph_get()
   assert r.matrix_world==Matrix.Identity(4) and r.pose.bones['Root'].matrix_basis==Matrix.Identity(4)
   up=Vector((0,1,0))
   for bone,values in [('Chest',torso_angles),('Hips',pelvis_angles)]:
    rotation=r.pose.bones[bone].matrix@r.data.bones[bone].matrix_local.inverted()
    values.append(180-math.degrees((rotation.to_3x3()@up).angle(up)))
   shoulder=(r.pose.bones['UpperArm.L'].head+r.pose.bones['UpperArm.R'].head)*.5
   body_angles.append(180-math.degrees((shoulder-r.pose.bones['Hips'].head).angle(up)))
   hip_travel.append(math.hypot(r.pose.bones['Hips'].location.x,r.pose.bones['Hips'].location.z))
   for bone,values in [('Chest',chest_pitch),('Head',head_pitch)]:
    rot=r.pose.bones[bone].matrix@r.data.bones[bone].matrix_local.inverted();values.append(math.degrees(math.atan2(rot[2][1],rot[1][1])))
   rel=head_relative_vertices(r,dg)
   if helmet is None:helmet=rel
   helmet_error=max(helmet_error,max((a-b).length for n in rel for a,b in zip(rel[n],helmet[n])))
   cache={};floor=[]
   for n in set(n for pair in pairs for n in pair):
    o=bpy.data.objects[n].evaluated_get(dg);verts=[o.matrix_world@v.co for v in o.data.vertices];polys=[list(p.vertices) for p in o.data.polygons]
    if n.endswith('UpperArm'):
     bone=r.pose.bones['UpperArm.'+('L' if n.startswith('Left') else 'R')];axis=(bone.tail-bone.head).normalized()
     polys=[ids for ids in polys if all((verts[j]-bone.head).dot(axis)>.08 for j in ids)]
    cache[n]=BVHTree.FromPolygons(verts,polys)
    if n.endswith('Foot'):soles.append(min(v.y for v in verts));floor.append(min(v.y for v in verts))
   if min(floor)>.02 and tick>=40:airborne.append(1+tick/4)
   for a,b in pairs:
    if cache[a].overlap(cache[b]):hits.append([1+tick/4,a,b])
   feet.append({side:r.pose.bones['Foot.'+side].head.copy() for side in ['L','R']})
  drift=max((row[push]-feet[24][push]).length for row in feet[24:41])
  def extension(f):
   s.frame_set(f);return (r.pose.bones['Foot.'+push].head-r.pose.bones['UpperLeg.'+push].head).length
  gain=extension(11)-extension(7)
  def span(f):
   s.frame_set(f);return (r.pose.bones['Hand.L'].head-r.pose.bones['Hand.R'].head).length
  open_span=span(18);closed_span=span(30)
  raw=(OUT/(stem(name)+'.glb')).read_bytes();size=struct.unpack_from('<I',raw,12)[0];doc=json.loads(raw[20:20+size]);assert len(doc['animations'])==1 and doc['animations'][0]['name']==name
  clip=doc['animations'][0];duration=max(doc['accessors'][q['input']]['max'][0] for q in clip['samplers'])-min(doc['accessors'][q['input']]['min'][0] for q in clip['samplers'])
  result={'duration_seconds':duration,'entry_mesh_error_m':entry_error,'head_pitch_degrees':[min(head_pitch),max(head_pitch)],'chest_pitch_degrees':[min(chest_pitch),max(chest_pitch)],'minimum_sole_y_m':min(soles),'plant_bone':push,'plant_drift_m':drift,'push_extension_gain_m':gain,'contact_wrist_span_m':open_span,'wrap_wrist_span_m':closed_span,'helmet_relative_error_m':helmet_error,'intersection_events':hits,'collision_scope':'Nonadjacent arms/torso/legs, opposite legs and arms, hands/forearms versus helmet; proximal shoulder joints excluded. Quarter-frame samples.','root_identity':True,'bounded_hips_transfer_m':.14,'preserved_geometry_skeleton_weights_actions_and_source_files':True,'ball_free':not any('football' in o.name.lower() for o in s.objects)}
  results[name]=result
  result.update({'angle_reference':'180 upright, 90 horizontal; inclination measured in 3D, independent of diagonal yaw','peak_torso_angle_degrees':min(torso_angles),'peak_pelvis_to_shoulders_angle_degrees':min(body_angles),'peak_pelvis_angle_degrees':min(pelvis_angles),'bounded_hips_transfer_m':max(hip_travel),'airborne_sampled_frames':[min(airborne),max(airborne)] if airborne else [],'plant_interval_frames':[7,11]})
  print(name,json.dumps(result),flush=True)
 (PRE/'validation.json').write_text(json.dumps(results,indent=2))
 for name,v in results.items():
  assert abs(v['duration_seconds']-.6)<1e-6 and v['entry_mesh_error_m']<1e-5
  assert min(v['head_pitch_degrees'])>-5 and max(v['head_pitch_degrees'])<10
  # Linear quaternion interpolation between exact keyed anchors stays within
  # two millimetres during the plant (same tolerance as TackleReady).
  assert v['plant_drift_m']<.002 and v['push_extension_gain_m']>.015
  assert 95<v['peak_torso_angle_degrees']<125 and v['peak_pelvis_to_shoulders_angle_degrees']<135
  assert v['peak_pelvis_angle_degrees']<130 and .45<v['bounded_hips_transfer_m']<.55
  assert len(v['airborne_sampled_frames'])==2
  assert v['wrap_wrist_span_m']<v['contact_wrist_span_m']*.6
  assert v['helmet_relative_error_m']<1e-5 and v['ball_free']
  assert v['minimum_sole_y_m']>-.005,(name,'floor',v['minimum_sole_y_m'])
  assert not v['intersection_events'],(name,v['intersection_events'][:10])
 for name in results:results[name]['automated_validation']='PASS'
 (PRE/'validation.json').write_text(json.dumps(results,indent=2))
 print('ALL_LUNGE_VALIDATION_PASSED',flush=True)
if __name__=='__main__':main()
