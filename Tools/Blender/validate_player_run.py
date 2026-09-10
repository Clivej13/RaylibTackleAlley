"""Read-only validation of source preservation, Run and the exported GLB via MCP."""
from pathlib import Path
import bpy,json,struct,math
from mathutils import Matrix
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
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_jog.blend')); before=signature()
jog_signature=action_signature(bpy.data.actions['Jog']); jog_metrics=metrics('Jog',28)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_run.blend')); assert signature()==before
assert action_signature(bpy.data.actions['Jog'])==jog_signature
run_metrics=metrics('Run',24)
assert run_metrics['ankle_fore_aft_excursion']>jog_metrics['ankle_fore_aft_excursion']
assert run_metrics['knee_height_relative_hip']>jog_metrics['knee_height_relative_hip']
r=bpy.data.objects['PlayerRig']; s=bpy.context.scene
assert r.animation_data.action.name=='Run'
assert set(bpy.data.actions.keys())=={'Jog','Run','Pose_Neutral','Pose_Athletic','Pose_KneeRaise','Pose_ArmsForward','Pose_CrossBody','Pose_TorsoTwist','Pose_HeadLean','Pose_ShoulderDip'}
assert tuple(r.animation_data.action.frame_range)==(1,25)
assert s.render.fps==30
mirror=Matrix.Diagonal((-1,1,1,1)); symmetry=0; ground=1; head_angle=0
matrices={}; helmet={}
for f in range(1,26):
 s.frame_set(f); dg=bpy.context.evaluated_depsgraph_get()
 assert r.matrix_world==Matrix.Identity(4)
 assert r.pose.bones['Root'].matrix_basis==Matrix.Identity(4)
 assert abs(r.pose.bones['Hips'].location.x)+abs(r.pose.bones['Hips'].location.z)<1e-8
 matrices[f]={p.name:p.matrix.copy()@p.bone.matrix_local.inverted() for p in r.pose.bones}
 for name in ['Helmet','Facemask','Visor','ChinStrap']:
  o=bpy.data.objects[name]; rel=r.pose.bones['Head'].matrix.inverted()@o.matrix_world
  if f==1:helmet[name]=rel.copy()
  assert max(abs(rel[i][j]-helmet[name][i][j]) for i in range(4) for j in range(4))<1e-5
 vals=[]
 for name in ['LeftFoot','RightFoot']:
  o=bpy.data.objects[name].evaluated_get(dg); vals.append(min((o.matrix_world@v.co).y for v in o.data.vertices))
 ground=min(ground,*vals)
 assert min(vals)>-1e-5
for f in range(1,13):
 for part in ['UpperLeg','LowerLeg','Foot','UpperArm','LowerArm','Hand']:
  a=mirror@matrices[f][part+'.L']@mirror; b=matrices[f+12][part+'.R']
  symmetry=max(symmetry,max(abs(a[i][j]-b[i][j]) for i in range(4) for j in range(4)))
assert symmetry<1e-5,symmetry
raw=(OUT/'lowpoly_human_run_validation.glb').read_bytes(); size,kind=struct.unpack_from('<II',raw,12)
doc=json.loads(raw[20:20+size]); animations=doc.get('animations',[])
assert len(animations)==1
clip=animations[0]
# Blender 3.3 may label its merged active-action export Animation. Normalize
# through export_nla_strips=True in generation if this assertion fails.
assert clip['name']=='Run',clip['name']
targets=[(c['target']['node'],c['target']['path']) for c in clip['channels']]
assert len(targets)==len(set(targets))
duration=max(doc['accessors'][p['input']]['max'][0] for p in clip['samplers'])-min(doc['accessors'][p['input']]['min'][0] for p in clip['samplers'])
assert abs(duration-24/30)<1e-5,duration
result={'jog_action_unchanged':True,'jog_metrics':jog_metrics,'run_metrics':run_metrics,'unchanged_geometry_weights_hierarchy':True,'actions_in_blend':10,'glb_animations':[clip['name']],'duration_seconds':duration,'mirrored_half_cycle_matrix_error':symmetry,'minimum_foot_y':ground,'helmet_rigid_attachment':True,'root_identity_all_frames':True}
(OUT/'RunPreviews'/'asset_validation.json').write_text(json.dumps(result,indent=2)); print(json.dumps(result))

# Quarter-frame clearance and pad rigidity check, including interpolated poses.
minimum=0; pad_error=0; reference=None
for i in range(97):
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
assert minimum>-.002
result.update(subframe_minimum_sole_y=minimum,pads_chest_relative_vertex_error=pad_error)
(OUT/'RunPreviews'/'asset_validation.json').write_text(json.dumps(result,indent=2))
