"""Read-only deformation/clearance audit of saved clothed returner clips via MCP."""
from pathlib import Path
import sys, json, math
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils.bvhtree import BVHTree
from create_returner_taunts import CONFIG, OUT, PRE

def main():
 report={}
 for ident,name,stem in CONFIG:
  bpy.ops.wm.open_mainfile(filepath=str(OUT/('football_player_taunt_'+stem+'.blend')))
  s=bpy.context.scene;r=bpy.data.objects['PlayerRig'];low=1e6;hits=[]
  assert r.animation_data.action.name==name and s.frame_end==157 and s.render.fps==60
  pairs=[(a,b) for a in ('LeftHand','LeftForearm') for b in ('Jersey','Helmet','RightHand','RightForearm','FootballPreview')]
  for frame in range(1,158,2):
   s.frame_set(frame);dg=bpy.context.evaluated_depsgraph_get();trees={}
   for n in {n for pair in pairs for n in pair}|{'Cleat_L','Cleat_R'}:
    ob=bpy.data.objects[n].evaluated_get(dg); mesh=ob.to_mesh();verts=[ob.matrix_world@v.co for v in mesh.vertices]
    assert all(math.isfinite(x) for v in verts for x in v)
    trees[n]=BVHTree.FromPolygons(verts,[p.vertices[:] for p in mesh.polygons])
    if n.startswith('Cleat'):low=min(low,min(v.y for v in verts))
    ob.to_mesh_clear()
   for a,b in pairs:
    overlaps=trees[a].overlap(trees[b])
    if overlaps:hits.append({'frame':frame,'pair':[a,b],'triangles':len(overlaps)})
  report[ident]={'minimum_cleat_y':low,'clearance_intersections':hits,'finite_sampled_geometry':True}
 (PRE/'clearance_validation.json').write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps(report))
 assert all(not x['clearance_intersections'] for x in report.values()), 'Inspect reported hand/forearm intersections'
 assert all(x['minimum_cleat_y'] > -.005 for x in report.values()), 'Cleat penetrates ground'
if __name__=='__main__':main()
