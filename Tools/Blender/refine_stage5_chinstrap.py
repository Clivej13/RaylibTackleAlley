"""Replace Stage 5 ChinStrap with a cup, two broad bands and side snaps.
Reference: Assets/References/Helmet.png. Run using Blender MCP.
"""
from pathlib import Path
import math,json,struct
import bpy,bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets'/'Models'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_stage5.blend'))
scene=bpy.context.scene
strap=bpy.data.objects['ChinStrap']
def signature(o):
    return (tuple(tuple(v.co) for v in o.data.vertices),tuple(tuple(f.vertices) for f in o.data.polygons),
            tuple(tuple(r) for r in o.matrix_world),tuple(m.name for m in o.data.materials))
protected=[o for o in scene.objects if o.type=='MESH' and o!=strap]
before={o.name:signature(o) for o in protected}
vs=[]; fs=[]; groups={}
# Broad, shallow cup with a squared lower lip, wrapping under the chin.
for y,z in [(1.673,-.107),(1.647,-.102),(1.623,-.071)]:
    for x in (-.060,-.033,0,.033,.060):
        vs.append((x,y+abs(x)*.06,z+abs(x)*.18))
for j in range(2):
    for i in range(4):
        a=j*5+i; fs.append((a,a+1,a+6,a+5))
groups['ChinCup']=list(range(15))
for side,label in ((1,'Left'),(-1,'Right')):
    # One continuous 25 mm padded band per side; no branching or strings.
    path=[(side*.053,1.647,-.094),(side*.080,1.655,-.088),
          (side*.111,1.679,-.063),(side*.132,1.700,-.031),
          (side*.139,1.718,-.006)]
    pts=[Vector(p) for p in path]; start=len(vs)
    for j,p in enumerate(pts):
        tangent=(pts[min(j+1,len(pts)-1)]-pts[max(0,j-1)]).normalized()
        outward=Vector((p.x,0,p.z-.012)).normalized()
        width=tangent.cross(outward).normalized()*.0125
        vs.extend([tuple(p-width),tuple(p+width)])
    for j in range(len(pts)-1):
        a=start+j*2; fs.append((a,a+1,a+3,a+2))
    groups[label+'SideBand']=list(range(start,len(vs)))
    # Low-relief octagonal snap at the lower helmet side/ear location.
    start=len(vs); center=Vector(path[-1]); center.x+=side*.002
    for k in range(8):
        a=2*math.pi*k/8
        vs.append(tuple(center+Vector((0,.015*math.cos(a),.015*math.sin(a)))))
    fs.append(tuple(range(start,start+8)))
    groups[label+'HelmetSnap']=list(range(start,start+8))
data=bpy.data.meshes.new('ChinStrap_CupAndTwoBands')
inv=strap.matrix_world.inverted()
data.from_pydata([inv@Vector(p) for p in vs],[],fs); data.update()
bm=bmesh.new(); bm.from_mesh(data); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(data); bm.free()
strap.data=data
mat=bpy.data.materials.new('ChinStrap_PaddedNeutral'); mat.use_nodes=True
mat.diffuse_color=(.62,.63,.62,1)
p=mat.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=mat.diffuse_color
p.inputs['Roughness'].default_value=.8
data.materials.append(mat)
strap.vertex_groups.clear()
for name,indices in groups.items():
    g=strap.vertex_groups.new(name=name); g.add(indices,1,'REPLACE')
bpy.ops.object.select_all(action='DESELECT'); strap.select_set(True); bpy.context.view_layer.objects.active=strap
mod=strap.modifiers.new('Four millimetre padded thickness','SOLIDIFY'); mod.thickness=.004; mod.offset=0
bpy.ops.object.modifier_apply(modifier=mod.name)
strap['design']='One faceted chin cup, exactly two 25 mm wide padded side bands, octagonal lower-ear mounting snaps. No cords or branching straps.'
bpy.context.view_layer.update()
assert all(signature(o)==before[o.name] for o in protected)
def tree(objects):
    verts=[]; faces=[]
    for o in objects:
        start=len(verts); verts.extend(o.matrix_world@v.co for v in o.data.vertices)
        faces.extend(tuple(start+i for i in f.vertices) for f in o.data.polygons)
    return BVHTree.FromPolygons(verts,faces)
headtree=tree(list(bpy.data.collections['Stage4_Head'].objects))
overlaps=len(tree([strap]).overlap(headtree))
assert overlaps==0,'Chin strap clips head'
report={'cup':1,'side_bands':2,'band_width_mm':25,'thickness_mm':4,
        'triangles':sum(len(f.vertices)-2 for f in strap.data.polygons),'head_intersections':overlaps,
        'unchanged_other_meshes':len(protected),'reference':'Assets/References/Helmet.png'}
scene['chinstrap_report']=json.dumps(report)
for o in scene.objects: o.select_set(o.type=='MESH' or o.name=='HelmetAssemblyRoot')
scene.camera=bpy.data.objects['FrontThreeQuarter']
scene.render.filepath=str(OUT/'lowpoly_human_stage5_frontthreequarter.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_stage5.blend'))
bpy.ops.export_scene.gltf(filepath=str(OUT/'lowpoly_human_stage5.glb'),export_format='GLB',use_selection=True,
    export_yup=False,export_animations=False,export_cameras=False,export_lights=False)
raw=(OUT/'lowpoly_human_stage5.glb').read_bytes(); n=struct.unpack_from('<I',raw,12)[0]
doc=json.loads(raw[20:20+n]); assert len(doc['meshes'])==39
for name,suffix in [('Front','front'),('Back','back'),('FrontThreeQuarter','frontthreequarter'),('Side','side'),('HelmetCloseThreeQuarter','helmet_closeup')]:
    scene.camera=bpy.data.objects[name]; scene.render.filepath=str(OUT/('lowpoly_human_stage5_'+suffix+'.png'))
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['FrontThreeQuarter']
print('CHINSTRAP_REPORT '+json.dumps(report))
