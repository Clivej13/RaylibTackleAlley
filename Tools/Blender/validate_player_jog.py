"""Read-only validation of source preservation, Jog and the exported GLB via MCP."""
from pathlib import Path
import bpy,json,struct,math
from mathutils import Matrix
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Models'
def signature():
 r=bpy.data.objects['PlayerRig']
 return {'bones':[(b.name,b.parent.name if b.parent else None,tuple(b.head_local),tuple(b.tail_local),b.roll if hasattr(b,'roll') else tuple(v for row in b.matrix_local for v in row)) for b in r.data.bones],
 'meshes':{o.name:([tuple(v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons],[[ (g.group,g.weight) for g in v.groups] for v in o.data.vertices],o.parent.name if o.parent else None,o.parent_bone) for o in bpy.data.objects if o.type=='MESH'}}
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_rigged.blend')); before=signature()
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_jog.blend')); assert signature()==before
r=bpy.data.objects['PlayerRig']; s=bpy.context.scene
assert r.animation_data.action.name=='Jog'
assert set(bpy.data.actions.keys())=={'Jog','Pose_Neutral','Pose_Athletic','Pose_KneeRaise','Pose_ArmsForward','Pose_CrossBody','Pose_TorsoTwist','Pose_HeadLean','Pose_ShoulderDip'}
assert tuple(r.animation_data.action.frame_range)==(1,29)
assert s.render.fps==30
mirror=Matrix.Diagonal((-1,1,1,1)); symmetry=0; ground=1; head_angle=0
matrices={}; helmet={}
for f in range(1,30):
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
for f in range(1,15):
 for part in ['UpperLeg','LowerLeg','Foot','UpperArm','LowerArm','Hand']:
  a=mirror@matrices[f][part+'.L']@mirror; b=matrices[f+14][part+'.R']
  symmetry=max(symmetry,max(abs(a[i][j]-b[i][j]) for i in range(4) for j in range(4)))
assert symmetry<1e-5,symmetry
raw=(OUT/'lowpoly_human_jog_validation.glb').read_bytes(); size,kind=struct.unpack_from('<II',raw,12)
doc=json.loads(raw[20:20+size]); animations=doc.get('animations',[])
assert len(animations)==1
clip=animations[0]
# Blender 3.3 may label its merged active-action export Animation. Normalize
# through export_nla_strips=True in generation if this assertion fails.
assert clip['name']=='Jog',clip['name']
targets=[(c['target']['node'],c['target']['path']) for c in clip['channels']]
assert len(targets)==len(set(targets))
duration=max(doc['accessors'][p['input']]['max'][0] for p in clip['samplers'])-min(doc['accessors'][p['input']]['min'][0] for p in clip['samplers'])
assert abs(duration-28/30)<1e-5,duration
result={'unchanged_geometry_weights_hierarchy':True,'actions_in_blend':9,'glb_animations':[clip['name']],'duration_seconds':duration,'mirrored_half_cycle_matrix_error':symmetry,'minimum_foot_y':ground,'helmet_rigid_attachment':True,'root_identity_all_frames':True}
(OUT/'JogPreviews'/'asset_validation.json').write_text(json.dumps(result,indent=2)); print(json.dumps(result))
