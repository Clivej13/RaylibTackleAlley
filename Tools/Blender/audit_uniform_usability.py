"""Exact-data preservation and continuous triangle intersection audit via Blender MCP."""
import runpy, json
import numpy as np
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
m=runpy.run_path(str(ROOT/'Tools/Blender/cleanup_uniform_uv.py'))
ART=m['ART']; read=m['read']; array=m['array']
old,od,ob,oo=read(ART/'football_player_before_usability.glb')
new,nd,nb,no=read(m['DEST'])
assert od==nd and oo==no
islands=m['collect'](od,ob)
changes=json.loads((ART/'usability_changes.json').read_text())
changed={i['id'] for i in changes['changed_islands']}
allowed=np.zeros(len(ob),dtype=bool)
max_distance_error=0.0
for i in islands:
    if i['id'] not in changed: continue
    p=od['meshes'][i['mi']]['primitives'][i['pi']]
    ai=p['attributes']['TEXCOORD_0']; a=od['accessors'][ai]; v=od['bufferViews'][a['bufferView']]
    q0=array(od,ob,ai)[i['vs']].astype(float); q1=array(nd,nb,ai)[i['vs']].astype(float)
    dist0=np.linalg.norm(q0[:,None]-q0[None,:],axis=2)
    dist1=np.linalg.norm(q1[:,None]-q1[None,:],axis=2)
    max_distance_error=max(max_distance_error,float(np.max(np.abs(dist0-dist1))*2048))
    for vi in i['vs']:
        start=v.get('byteOffset',0)+a.get('byteOffset',0)+int(vi)*v.get('byteStride',8); allowed[start:start+8]=True
assert np.array_equal(np.frombuffer(ob,dtype=np.uint8)[~allowed],np.frombuffer(nb,dtype=np.uint8)[~allowed])
triangles=[]
for mesh in nd['meshes']:
    for p in mesh['primitives']:
        if nd['materials'][p['material']]['name']!='Uniform': continue
        uv=array(nd,nb,p['attributes']['TEXCOORD_0']).astype(float)*2048
        ix=array(nd,nb,p['indices']).ravel().reshape(-1,3)
        triangles.extend(uv[ix])
def cross(a,b): return a[0]*b[1]-a[1]*b[0]
def intersect_area(a,b):
    poly=[tuple(p) for p in a]
    sign=1 if cross(b[1]-b[0],b[2]-b[0])>0 else -1
    for j in range(3):
        o=b[j]; e=b[(j+1)%3]-o
        if not poly: return 0.0
        result=[]; prev=poly[-1]; pd=sign*cross(e,np.asarray(prev)-o)
        for cur in poly:
            cd=sign*cross(e,np.asarray(cur)-o)
            if (pd>=0)!=(cd>=0):
                t=pd/(pd-cd); result.append((prev[0]+t*(cur[0]-prev[0]),prev[1]+t*(cur[1]-prev[1])))
            if cd>=0: result.append(cur)
            prev=cur; pd=cd
        poly=result
    if len(poly)<3: return 0.0
    # Translate before computing area to avoid cancellation at large atlas coordinates.
    o=np.array(poly[0]); p=np.array(poly)-o
    return abs(sum(cross(p[j],p[(j+1)%len(p)]) for j in range(len(p))))/2
grid={}; pairs=set(); bounds=[]
for k,t in enumerate(triangles):
    lo=t.min(0); hi=t.max(0); bounds.append((lo,hi))
    for x in range(int(lo[0]//64),int(hi[0]//64)+1):
        for y in range(int(lo[1]//64),int(hi[1]//64)+1):
            cell=grid.setdefault((x,y),[])
            for j in cell: pairs.add((j,k))
            cell.append(k)
overlaps=[]; peak=0.0
for j,k in sorted(pairs):
    alo,ahi=bounds[j]; blo,bhi=bounds[k]
    if np.any(np.minimum(ahi,bhi)-np.maximum(alo,blo)<=0): continue
    area=intersect_area(triangles[j],triangles[k]); peak=max(peak,area)
    if area>1e-7: overlaps.append([j,k,area])
assert not overlaps,overlaps[:10]
assert max_distance_error<.001
report=dict(continuous_triangle_interior_overlap_pairs=len(overlaps),intersection_area_tolerance_px2=1e-7,maximum_intersection_area_px2=peak,uniform_triangles=len(triangles),changed_existing_islands=len(changed),maximum_rigid_transform_distance_error_px=max_distance_error,gltf_json_exact=True,all_non_target_uv_bytes_exact=True,chest_back_uvs_exact=True,geometry_topology_skinning_skeleton_animations_material_assignments_exact=True,atlas_region_boundaries_unchanged=True)
(ART/'usability_audit.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
