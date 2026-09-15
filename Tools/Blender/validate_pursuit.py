"""Sample pursuit clearance and carriage at quarter frames through Blender MCP.
Adjacent anatomical pieces intentionally overlap at joints and are excluded.
"""
from pathlib import Path
import bpy, json, hashlib
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Models'
preserved=json.loads((ROOT/'Tools/Blender/pursuit_preservation_hashes.json').read_text(encoding='utf-8-sig'))
assert all(hashlib.sha256(Path(x['Path']).read_bytes()).hexdigest().upper()==x['Hash'] for x in preserved)
for clip, count in [('Jog',28),('Run',24)]:
 bpy.ops.wm.open_mainfile(filepath=str(OUT/f'lowpoly_human_{clip.lower()}.blend'))
 scene=bpy.context.scene; rig=bpy.data.objects['PlayerRig']
 pairs=[]
 torso=['Belly','LowerBack','UpperBack','LeftChest','RightChest','Pelvis','Glutes','ShoulderPads']
 for side in ['Left','Right']:
  for arm in ['Forearm','Hand']:
   pairs += [(side+arm,t) for t in torso+['LeftThigh','RightThigh']]
 for a in ['Calf','Ankle','Foot']:
  for b in ['Calf','Ankle','Foot']:pairs.append(('Left'+a,'Right'+b))
 hits=[]; feet=[]; elbows=[]; flare=[]; wrists=[]; height=[]; floor=[]
 for i in range(count*4+1):
  scene.frame_set(1+i//4,subframe=(i%4)/4)
  dg=bpy.context.evaluated_depsgraph_get(); cache={}
  for name in set(n for pair in pairs for n in pair):
   o=bpy.data.objects[name].evaluated_get(dg)
   verts=[o.matrix_world@v.co for v in o.data.vertices]
   cache[name]=BVHTree.FromPolygons(verts,[list(p.vertices) for p in o.data.polygons])
   if name.endswith('Foot'):floor.append(min(v.y for v in verts))
  for a,b in pairs:
   if cache[a].overlap(cache[b]):hits.append([1+i/4,a,b])
  chest=rig.pose.bones['Chest'].matrix.inverted()
  e=chest@rig.pose.bones['LowerArm.L'].head; w=chest@rig.pose.bones['Hand.L'].head
  elbows.append(e.x); flare.append(w.x-e.x); wrists.append(w.z)
  feet.append(rig.pose.bones['Foot.L'].head.x-rig.pose.bones['Foot.R'].head.x)
  height.append(rig.pose.bones['Hips'].head.y)
 result={'sample_spacing_frames':.25,'collision_scope':'Forearms/hands versus torso, pads and thighs; opposite lower legs/feet. Adjacent joint overlaps excluded.',
 'intersection_events':hits,'minimum_sole_y_m':min(floor),'foot_track_width_m':[min(feet),max(feet)],'hips_height_m':[min(height),max(height)],
 'minimum_elbow_lateral_distance_m':min(elbows),'maximum_hand_outward_flare_m':max(flare),'wrist_fore_aft_excursion_m':max(wrists)-min(wrists),
 'other_model_files_including_sprint_preserved':True,'ball_free':not any('football' in o.name.lower() for o in scene.objects)}
 (OUT/(clip+'Previews')/'pursuit_validation.json').write_text(json.dumps(result,indent=2))
 print(clip,json.dumps(result))
 assert not hits, hits[:5]
 assert min(floor)>-.002
 assert max(flare)<.005
 assert min(feet)>.15 and max(feet)<.3
