"""Read-only validation of source preservation, Sprint and the exported GLB via MCP."""
from pathlib import Path
import bpy,json,struct,math
from mathutils import Matrix
import sys
sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_helmet_skinning import head_relative_vertices
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Models'
def signature():
 r=bpy.data.objects['PlayerRig']
 return {'bones':[(b.name,b.parent.name if b.parent else None,tuple(b.head_local),tuple(b.tail_local),b.roll if hasattr(b,'roll') else tuple(v for row in b.matrix_local for v in row)) for b in r.data.bones],
 'meshes':{o.name:([tuple(v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons],[[ (g.group,g.weight) for g in v.groups] for v in o.data.vertices],o.parent.name if o.parent else None,o.parent_bone) for o in bpy.data.objects if o.type=='MESH'}}
def action_signature(a):
 return [(f.data_path,f.array_index,[(tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation) for k in f.keyframe_points],[(m.type) for m in f.modifiers]) for f in a.fcurves]
def metrics(name, intervals):
 r=bpy.data.objects['PlayerRig']; r.animation_data.action=bpy.data.actions[name]; s=bpy.context.scene
 feet=[]; knees=[]; hips=[]; arms=[]
 for f in range(1,intervals+1):
  s.frame_set(f)
  feet.append(r.pose.bones['Foot.L'].head.z)
  knees.append(r.pose.bones['LowerLeg.L'].head.y-r.pose.bones['UpperLeg.L'].head.y)
  hips.append(r.pose.bones['Hips'].location.y)
  arms.append(r.pose.bones['UpperArm.L'].rotation_quaternion.angle)
 return dict(ankle_fore_aft_excursion=max(feet)-min(feet), knee_height_relative_hip=max(knees), hips_bob=max(hips)-min(hips), upper_arm_rotation_range=max(arms)-min(arms))
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_run.blend')); before=signature()
run_signature=action_signature(bpy.data.actions['Run']); jog_signature=action_signature(bpy.data.actions['Jog']); run_metrics=metrics('Run',24)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_sprint.blend')); assert signature()==before
assert action_signature(bpy.data.actions['Run'])==run_signature
assert action_signature(bpy.data.actions['Jog'])==jog_signature
sprint_metrics=metrics('Sprint',20)
assert sprint_metrics['ankle_fore_aft_excursion']>run_metrics['ankle_fore_aft_excursion']
assert sprint_metrics['knee_height_relative_hip']>run_metrics['knee_height_relative_hip']
r=bpy.data.objects['PlayerRig']; s=bpy.context.scene
assert r.animation_data.action.name=='Sprint'
assert set(bpy.data.actions.keys())=={'Jog','Run','Sprint','Pose_Neutral','Pose_Athletic','Pose_KneeRaise','Pose_ArmsForward','Pose_CrossBody','Pose_TorsoTwist','Pose_HeadLean','Pose_ShoulderDip'}
assert tuple(r.animation_data.action.frame_range)==(1,21)
assert s.render.fps==30
mirror=Matrix.Diagonal((-1,1,1,1)); symmetry=0; ground=1; head_angle=0
matrices={}; helmet={}
for f in range(1,22):
 s.frame_set(f); dg=bpy.context.evaluated_depsgraph_get()
 assert r.matrix_world==Matrix.Identity(4)
 assert r.pose.bones['Root'].matrix_basis==Matrix.Identity(4)
 assert abs(r.pose.bones['Hips'].location.x)+abs(r.pose.bones['Hips'].location.z)<1e-8
 matrices[f]={p.name:p.matrix.copy()@p.bone.matrix_local.inverted() for p in r.pose.bones}
 for name, rel in head_relative_vertices(r,dg).items():
  if f==1:helmet[name]=rel
  assert max((a-b).length for a,b in zip(rel,helmet[name]))<1e-5
 vals=[]
 for name in ['LeftFoot','RightFoot']:
  o=bpy.data.objects[name].evaluated_get(dg); vals.append(min((o.matrix_world@v.co).y for v in o.data.vertices))
 ground=min(ground,*vals)
 assert min(vals)>-1e-5
for f in range(1,11):
 for part in ['UpperLeg','LowerLeg','Foot','UpperArm','LowerArm','Hand']:
  a=mirror@matrices[f][part+'.L']@mirror; b=matrices[f+10][part+'.R']
  symmetry=max(symmetry,max(abs(a[i][j]-b[i][j]) for i in range(4) for j in range(4)))
assert symmetry<1e-5,symmetry
raw=(OUT/'lowpoly_human_sprint_validation.glb').read_bytes(); size,kind=struct.unpack_from('<II',raw,12)
doc=json.loads(raw[20:20+size]); animations=doc.get('animations',[])
assert len(animations)==1
clip=animations[0]
# Blender 3.3 may label its merged active-action export Animation. Normalize
# through export_nla_strips=True in generation if this assertion fails.
assert clip['name']=='Sprint',clip['name']
targets=[(c['target']['node'],c['target']['path']) for c in clip['channels']]
assert len(targets)==len(set(targets))
duration=max(doc['accessors'][p['input']]['max'][0] for p in clip['samplers'])-min(doc['accessors'][p['input']]['min'][0] for p in clip['samplers'])
assert abs(duration-20/30)<1e-5,duration
result={'jog_action_unchanged':True,'run_action_unchanged':True,'run_metrics':run_metrics,'sprint_metrics':sprint_metrics,'unchanged_geometry_weights_hierarchy':True,'actions_in_blend':11,'glb_animations':[clip['name']],'duration_seconds':duration,'mirrored_half_cycle_matrix_error':symmetry,'minimum_foot_y':ground,'helmet_rigid_attachment':True,'root_identity_all_frames':True}
(OUT/'SprintPreviews'/'asset_validation.json').write_text(json.dumps(result,indent=2)); print(json.dumps(result))

# Quarter-frame clearance and pad rigidity check, including interpolated poses.
minimum=0; pad_error=0; reference=None
for i in range(81):
 s.frame_set(1+i//4,subframe=(i%4)/4); dg=bpy.context.evaluated_depsgraph_get()
 for name in ['LeftFoot','RightFoot']:
  o=bpy.data.objects[name].evaluated_get(dg)
  minimum=min(minimum,min((o.matrix_world@v.co).y for v in o.data.vertices))
 o=bpy.data.objects['ShoulderPads'].evaluated_get(dg)
 inv=r.pose.bones['Chest'].matrix.inverted()
 verts=[inv@(o.matrix_world@v.co) for v in o.data.vertices]
 if reference is None: reference=verts
 pad_error=max(pad_error,max((a-b).length for a,b in zip(reference,verts)))
assert pad_error<1e-5
assert minimum>-.00001
result.update(subframe_minimum_sole_y=minimum,pads_chest_relative_vertex_error=pad_error)
(OUT/'SprintPreviews'/'asset_validation.json').write_text(json.dumps(result,indent=2))

# Reopened endpoint evaluation, all mesh vertices including equipment.
def snapshot(frame):
 s.frame_set(frame); dg=bpy.context.evaluated_depsgraph_get()
 return [(o.matrix_world@v.co).copy() for src in s.objects if src.type=='MESH' for o in [src.evaluated_get(dg)] for v in o.data.vertices]
endpoint_error=max((a-b).length for a,b in zip(snapshot(1),snapshot(21)))
assert endpoint_error<1e-6
result['endpoint_mesh_error_m']=endpoint_error
# Rebuilt blend bytes change with equipment rigging; compare approved content.
from validate_helmet_preservation import main as validate_preservation
validate_preservation()
result['approved_content_and_csharp_sources_preserved']=True
(OUT/'SprintPreviews'/'asset_validation.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result))
