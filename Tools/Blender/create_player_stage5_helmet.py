"""Fresh Stage 5 from approved Stage 4 and Helmet.png; execute via Blender MCP.
No previous Stage 5 geometry or generator is read. Metres, +Y up, -Z forward.
"""
from pathlib import Path
import math,json,struct
import bpy,bmesh
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets'/'Models'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_stage4.blend'))
scene=bpy.context.scene
def signature(o):
    return (tuple(tuple(v.co) for v in o.data.vertices),tuple(tuple(f.vertices) for f in o.data.polygons),
        tuple(tuple(r) for r in o.matrix_world),tuple(m.name for m in o.data.materials),
        tuple((f.use_smooth,f.material_index) for f in o.data.polygons))
body=[o for o in scene.objects if o.type=='MESH']
original={o.name:signature(o) for o in body}
assembly=bpy.data.collections.new('HelmetAssembly'); scene.collection.children.link(assembly)
root=bpy.data.objects.new('HelmetAssemblyRoot',None); assembly.objects.link(root)
root.location=(0,1.79,.020); root.empty_display_size=.04
root['purpose']='Move/detach all four children together; hide via HelmetAssembly collection.'
def material(name,color,metal,rough):
    m=bpy.data.materials.new(name); m.use_nodes=True; m.diffuse_color=(*color,1)
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=m.diffuse_color
    p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    return m
shellmat=material('Helmet',(.47,.49,.51),.05,.49)
maskmat=material('Facemask',(.065,.074,.085),.25,.4)
visormat=material('Visor',(.012,.021,.027),.12,.19)
visormat.node_tree.nodes.get('Principled BSDF').inputs['Transmission'].default_value=.12
def mesh(name,verts,faces,mat,thickness=0):
    used=sorted({i for f in faces for i in f}); remap={a:b for b,a in enumerate(used)}
    me=bpy.data.meshes.new(name+'Mesh'); me.from_pydata([verts[i] for i in used],[],[tuple(remap[i] for i in f) for f in faces]); me.update()
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(me); bm.free()
    ob=bpy.data.objects.new(name,me); assembly.objects.link(ob); me.materials.append(mat)
    if thickness:
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
        mod=ob.modifiers.new('Shell wall','SOLIDIFY'); mod.thickness=thickness; mod.offset=-1; mod.use_even_offset=True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.context.view_layer.update(); world=ob.matrix_world.copy(); ob.parent=root; ob.matrix_world=world
    return ob
# Fresh elliptical sections. Rear-most volume is high; lower rear pulls inward
# and rises at the neck cutout. Front radius is smaller than rear radius.
# Y, halfwidth, front radius, rear radius, centre Z.
rows=[(1.924,.020,.020,.025,.032),(1.914,.063,.061,.069,.034),
      (1.889,.100,.095,.108,.035),(1.857,.122,.117,.128,.034),
      (1.816,.136,.135,.132,.027),(1.776,.140,.137,.133,.019),
      (1.737,.134,.128,.128,.015),(1.681,.121,.112,.114,.011)]
N=20; hv=[]; hf=[]
for j,(y,w,front,rear,z) in enumerate(rows):
    for i in range(N):
        a=i*2*math.pi/N; c=math.cos(a)
        yy=y
        if j==5: yy+=.012*max(c,0)
        if j==7: yy+=.025*max(-c,0)-.008*max(c,0)
        zz=z-c*(front if c>=0 else rear)
        if j==7: zz+=.018*max(-c,0)
        hv.append((w*math.sin(a),yy,zz))
hf.append(tuple(reversed(range(N))))
for j in range(7):
    for i in range(N):
        if j>=5 and not 3<=i<17: continue
        if j==6 and i in (4,15): continue
        a=j*N+i; b=j*N+(i+1)%N; hf.append((a,b,b+N,a+N))
helmet=mesh('Helmet',hv,hf,shellmat,.006)

def tube(vs,fs,points,r):
    pts=[Vector(p) for p in points]; start=len(vs); n=6
    for j,p in enumerate(pts):
        tangent=(pts[min(j+1,len(pts)-1)]-pts[max(0,j-1)]).normalized()
        ref=Vector((0,1,0)) if abs(tangent.y)<.9 else Vector((1,0,0))
        u=tangent.cross(ref).normalized(); v=tangent.cross(u).normalized()
        for k in range(n):
            a=k*2*math.pi/n; vs.append(tuple(p+r*(math.cos(a)*u+math.sin(a)*v)))
    fs.append(tuple(start+i for i in reversed(range(n))))
    for j in range(len(pts)-1):
        for i in range(n):
            a=start+j*n+i; b=start+j*n+(i+1)%n; fs.append((a,b,b+n,a+n))
    fs.append(tuple(start+(len(pts)-1)*n+i for i in range(n)))
mv=[]; mf=[]
# Compact nose-line rail and chin rail, sweeping into temple/jaw mounts.
tube(mv,mf,[(-.111,1.742,-.054),(-.120,1.743,-.098),(-.089,1.744,-.141),
            (-.047,1.743,-.156),(0,1.742,-.158),(.047,1.743,-.156),
            (.089,1.744,-.141),(.120,1.743,-.098),(.111,1.742,-.054)],.007)
tube(mv,mf,[(-.104,1.687,-.048),(-.119,1.674,-.095),(-.089,1.657,-.140),
            (-.044,1.650,-.160),(0,1.649,-.164),(.044,1.650,-.160),
            (.089,1.657,-.140),(.119,1.674,-.095),(.104,1.687,-.048)],.007)
for s in (-1,1):
    tube(mv,mf,[(s*.093,1.783,-.085),(s*.103,1.767,-.119),(s*.089,1.744,-.141),(s*.089,1.657,-.140)],.0065)
    tube(mv,mf,[(s*.120,1.743,-.098),(s*.122,1.708,-.098),(s*.119,1.674,-.095)],.006)
    tube(mv,mf,[(s*.108,1.742,-.045),(s*.117,1.742,-.064)],.010)
    tube(mv,mf,[(s*.101,1.687,-.039),(s*.111,1.687,-.056)],.010)
mask=mesh('Facemask',mv,mf,maskmat)
# Wrapped smoked panel close to eyes; clipped within the cage silhouette.
cols=[(-.107,-.058),(-.090,-.099),(-.060,-.128),(-.030,-.140),
      (0,-.143),(.030,-.140),(.060,-.128),(.090,-.099),(.107,-.058)]
vv=[]; vf=[]
for y in (1.781,1.748,1.707):
    for x,z in cols: vv.append((x,y,z))
for j in range(2):
    for i in range(8):
        a=j*9+i; vf.append((a,a+1,a+10,a+9))
visor=mesh('Visor',vv,vf,visormat,.0025)

# Shallow faceted chin cup; outer surface wraps forward and under the chin.
# All strap/cup pieces form one separate ChinStrap object.
strapmat=material('ChinStrap',(.34,.35,.36),0,.78)
cv=[]; cf=[]
for y,z in [(1.666,-.100),(1.643,-.094),(1.626,-.065)]:
    for x in (-.050,-.028,0,.028,.050):
        cv.append((x,y+abs(x)*.035,z+abs(x)*.16))
for j in range(2):
    for i in range(4):
        a=j*5+i; cf.append((a,a+1,a+6,a+5))

def ribbon(points,width=.012):
    pts=[Vector(p) for p in points]; start=len(cv)
    for j,p in enumerate(pts):
        t=(pts[min(j+1,len(pts)-1)]-pts[max(0,j-1)]).normalized()
        w=t.cross(Vector((0,0,1))).normalized()*width*.5
        cv.extend([tuple(p-w),tuple(p+w)])
    for j in range(len(pts)-1):
        a=start+j*2; cf.append((a,a+1,a+3,a+2))
for s in (-1,1):
    ribbon([(s*.045,1.647,-.087),(s*.078,1.655,-.079),
            (s*.108,1.683,-.056),(s*.125,1.704,-.032)])
    ribbon([(s*.076,1.656,-.079),(s*.114,1.694,-.047),
            (s*.138,1.735,-.004),(s*.137,1.757,.020)],.010)
strap=mesh('ChinStrap',cv,cf,strapmat,.003)
strap['design']='Faceted under-chin cup with paired split webbing straps to lower and upper helmet side anchors.'
bpy.context.view_layer.update()
assert all(signature(o)==original[o.name] for o in body),'Approved body changed'
def bvh(objects):
    vs=[]; fs=[]
    for o in objects:
        start=len(vs); vs.extend(o.matrix_world@v.co for v in o.data.vertices)
        fs.extend(tuple(start+i for i in f.vertices) for f in o.data.polygons)
    return BVHTree.FromPolygons(vs,fs),vs
headtree,hp=bvh(list(bpy.data.collections['Stage4_Head'].objects))
report={}
for o in (helmet,mask,visor,strap):
    tr,pts=bvh([o]); intersections=len(tr.overlap(headtree))
    assert intersections==0,o.name+' intersects head'
    report[o.name]={'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),
        'dimensions_xyz_m':[round(v,5) for v in o.dimensions],
        'head_intersections':intersections,'sampled_clearance_m':round(min([headtree.find_nearest(p)[3] for p in pts]+[tr.find_nearest(p)[3] for p in hp]),5)}
report['preserved_body_meshes']=len(body)
report['reference']='Assets/References/Helmet.png'
scene['stage5_report']=json.dumps(report)
scene['stage_notes']='Stage 5 fitted to updated Stage 4 chin: fuller rising crown/rear, compact cage, smoked visor and separate chin cup/straps. Character unchanged.'
def aim(cam,target):
    back=(cam.location-Vector(target)).normalized(); right=Vector((0,1,0)).cross(back).normalized(); up=back.cross(right).normalized()
    cam.rotation_euler=Matrix((right,up,back)).transposed().to_euler()
for name,pos in [('Front',(0,.96,-4)),('FrontThreeQuarter',(2.5,1.12,-4)),('Side',(4,.96,0)),('Back',(0,.96,4))]:
    cam=bpy.data.objects[name]; cam.location=pos; cam.data.ortho_scale=2.12; aim(cam,(0,.96,0))
close=bpy.data.objects.new('HelmetCloseThreeQuarter',bpy.data.cameras.new('HelmetCloseThreeQuarter'))
bpy.data.collections['PreviewStudio'].objects.link(close)
close.location=(.85,1.90,-1.1); close.data.type='ORTHO'; close.data.ortho_scale=.49; aim(close,(0,1.785,.005))
scene.render.resolution_x=1200; scene.render.resolution_y=1400
scene.camera=bpy.data.objects['FrontThreeQuarter']
scene.render.filepath=str(OUT/'lowpoly_human_stage5_frontthreequarter.png')
for o in scene.objects: o.select_set(o.type=='MESH' or o==root)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_stage5.blend'))
bpy.ops.export_scene.gltf(filepath=str(OUT/'lowpoly_human_stage5.glb'),export_format='GLB',use_selection=True,
    export_yup=False,export_animations=False,export_cameras=False,export_lights=False)
data=(OUT/'lowpoly_human_stage5.glb').read_bytes(); size=struct.unpack_from('<I',data,12)[0]
doc=json.loads(data[20:20+size]); assert len(doc['meshes'])==len(body)+4
assert not doc.get('animations') and not doc.get('skins')
for name,suffix in [('Front','front'),('FrontThreeQuarter','frontthreequarter'),('Side','side'),('Back','back'),('HelmetCloseThreeQuarter','helmet_closeup')]:
    scene.camera=bpy.data.objects[name]; scene.render.filepath=str(OUT/('lowpoly_human_stage5_'+suffix+'.png'))
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['FrontThreeQuarter']
scene.render.filepath=str(OUT/'lowpoly_human_stage5_frontthreequarter.png')
print('STAGE5_REPORT '+json.dumps(report))
