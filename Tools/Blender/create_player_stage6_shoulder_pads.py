"""Stage 6 shoulder pads only, built on the approved Stage 5 character.
References: shoulder_pads.jpg and shoulder_pads_back.jpg. Blender MCP entrypoint.
"""
from pathlib import Path
import math,json
import bpy,bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets'/'Models'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_stage5.blend'))
scene=bpy.context.scene
def sig(o):
    return (tuple(tuple(v.co) for v in o.data.vertices),tuple(tuple(p.vertices) for p in o.data.polygons),
            tuple(tuple(r) for r in o.matrix_world),tuple(m.name for m in o.data.materials))
existing=[o for o in scene.objects if o.type=='MESH']; before={o.name:sig(o) for o in existing}
collection=bpy.data.collections.new('Stage6_ShoulderPads'); scene.collection.children.link(collection)
def mat(name,col):
    m=bpy.data.materials.new(name); m.use_nodes=True; m.diffuse_color=(*col,1)
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=m.diffuse_color
    p.inputs['Roughness'].default_value=.73
    return m
plate=mat('ShoulderPads_Shell',(.30,.33,.35)); foam=mat('ShoulderPads_Padding',(.075,.085,.095))
vs=[]; fs=[]; mi=[]; groups={}
def add(name,verts,faces,material=0):
    offset=len(vs); vs.extend(verts); fs.extend(tuple(i+offset for i in f) for f in faces)
    mi.extend([material]*len(faces)); groups[name]=list(range(offset,len(vs)))
profile=[(0,-1),(.40,-1),(.76,-.94),(.94,-.60),(1,0),(.94,.57),(.74,.92),(.39,1),
         (0,.96),(-.39,1),(-.74,.92),(-.94,.57),(-1,0),(-.94,-.60),(-.76,-.94),(-.40,-1)]
# The top ring is a true open neck aperture; the underside is open too.
rows=[(1.246,.182,.119,.112),(1.275,.195,.137,.125),(1.345,.219,.159,.143),
      (1.420,.224,.174,.163),(1.485,.220,.177,.167),(1.537,.175,.130,.130),
      (1.576,.090,.079,.086)]
verts=[]
for y,w,f,b in rows:
    for x,z in profile: verts.append((x*w,y,.005+z*(f if z<0 else b)))
for label,indices in [('ChestPlate',list(range(4))+list(range(12,16))),('BackPlate',list(range(4,12)))]:
    faces=[]
    for j in range(len(rows)-1):
        for i in indices:
            if j in (1,2,3,4) and i in (3,4,11,12): continue
            a=j*16+i; b=j*16+(i+1)%16; faces.append((a,b,b+16,a+16))
    add(label,verts,faces)
# Curved low-poly cap arches: maximum mass above the shoulder, open inferior
# edge for upper-arm movement. No ball primitives or sleeve geometry.
for side,label in ((1,'Left'),(-1,'Right')):
    verts=[]; faces=[]
    for x,y,ry,rz in [(.172,1.461,.111,.120),(.220,1.459,.125,.126),
                       (.269,1.446,.117,.124),(.302,1.425,.095,.104),(.320,1.410,.068,.079)]:
        for k in range(9):
            a=-math.pi/2+k*math.pi/8
            verts.append((side*x,y+ry*math.cos(a),.005+rz*math.sin(a)))
    for j in range(4):
        for k in range(8):
            a=j*9+k; faces.append((a,a+1,a+10,a+9))
    add(label+'ShoulderCap',verts,faces)
    # Dark padded roll beneath the outer shell edge.
    pv=[(x,y-.013,z*.99) for x,y,z in verts]
    add(label+'CapPadding',pv,faces,1)
# Small lower padded torso wrap, shaped to the ribcage.
verts=[]
for y,w,f,b in [(1.217,.177,.112,.103),(1.250,.187,.127,.119),(1.279,.197,.141,.129)]:
    for x,z in profile: verts.append((x*w,y,.005+z*(f if z<0 else b)))
faces=[]
for j in range(2):
    for i in range(16):
        a=j*16+i; b=j*16+(i+1)%16; faces.append((a,b,b+16,a+16))
add('LowerPaddedWrap',verts,faces,1)

data=bpy.data.meshes.new('ShoulderPadsMesh'); data.from_pydata(vs,[],fs); data.update()
bm=bmesh.new(); bm.from_mesh(data); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(data); bm.free()
ob=bpy.data.objects.new('ShoulderPads',data); collection.objects.link(ob)
data.materials.append(plate); data.materials.append(foam)
for p,i in zip(data.polygons,mi): p.material_index=i
for name,indices in groups.items():
    g=ob.vertex_groups.new(name=name); g.add(indices,1,'REPLACE')
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
solid=ob.modifiers.new('Padded shell thickness','SOLIDIFY'); solid.thickness=.008; solid.offset=-1; solid.use_even_offset=True
bpy.ops.object.modifier_apply(modifier=solid.name)
ob['purpose']='Reusable shoulder-pad component beneath a future jersey. Chest, back, cap and padding regions named as vertex groups.'
bpy.context.view_layer.update()
assert all(sig(o)==before[o.name] for o in existing),'Existing character changed'
def tree(objects):
    vertices=[]; faces=[]
    for o in objects:
        start=len(vertices); vertices.extend(o.matrix_world@v.co for v in o.data.vertices)
        faces.extend(tuple(start+i for i in p.vertices) for p in o.data.polygons)
    return BVHTree.FromPolygons(vertices,faces)
pad_tree=tree([ob])
checks={}
for name in ['NeckBase','LeftUpperArm','RightUpperArm','LeftShoulder','RightShoulder','LeftChest','RightChest','UpperBack','Belly']:
    checks[name]=len(pad_tree.overlap(tree([bpy.data.objects[name]])))
assert not any(checks.values()),str(checks)
report={'object':ob.name,'triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),
        'dimensions_xyz_m':list(ob.dimensions),'unchanged_meshes':len(existing),'body_intersection_checks':checks}
scene['stage6_report']=json.dumps(report)
scene['stage_notes']='Stage 6 shoulder pads only added to unchanged Stage 5. No jersey, rigging or animations.'
scene.camera=bpy.data.objects['FrontThreeQuarter']
scene.render.filepath=str(OUT/'lowpoly_human_stage6_frontthreequarter.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_stage6.blend'))
for name in ('Front','FrontThreeQuarter','Side','Back'):
    scene.camera=bpy.data.objects[name]; scene.render.filepath=str(OUT/('lowpoly_human_stage6_'+name.lower()+'.png'))
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['FrontThreeQuarter']
print('STAGE6_REPORT '+json.dumps(report))
