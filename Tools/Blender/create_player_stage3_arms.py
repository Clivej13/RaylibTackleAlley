"""Stage 3 only: add faceted arms/hands to the immutable stage-2 body.

Execute via Blender MCP. +Y up, -Z forward, metres. No rig or animation.
"""
from pathlib import Path
import json
import bpy
import bmesh
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets'/'Models'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_stage2.blend'))
scene=bpy.context.scene

def signature(ob):
    return (tuple(tuple(v.co) for v in ob.data.vertices),
            tuple(tuple(p.vertices) for p in ob.data.polygons),
            tuple(tuple(row) for row in ob.matrix_world),
            tuple(m.name for m in ob.data.materials),
            tuple(p.use_smooth for p in ob.data.polygons))

original={ob.name:signature(ob) for ob in scene.objects if ob.type=='MESH'}
collection=bpy.data.collections.new('Stage3_ArmsAndHands')
scene.collection.children.link(collection)
material=bpy.data.materials['Warm limestone']

def make_mesh(name,vertices,faces,side):
    vertices=[(side*x,y,z) for x,y,z in vertices]
    data=bpy.data.meshes.new(name+'_Mesh')
    data.from_pydata(vertices,[],faces); data.update()
    bm=bmesh.new(); bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(data); bm.free()
    ob=bpy.data.objects.new(name,data); collection.objects.link(ob)
    data.materials.append(material)
    ob['stage']=3
    ob['construction']='Hand-shaped polygon loops; flat shading; metres, Y up'
    return ob

# Clockwise section around the long axis, with flat and oblique planes.
P10=[(0,-1),(.60,-.90),(1,-.40),(1,.30),(.65,.84),
     (0,1),(-.65,.80),(-1,.30),(-1,-.40),(-.60,-.90)]
P8=[(-.7,-1),(.7,-1),(1,-.65),(1,.65),(.7,1),(-.7,1),(-1,.65),(-1,-.65)]

def loft(rows,profile=P10):
    # centre XYZ, radius in arm's lateral plane, anterior depth, posterior
    # depth, local in-plane section axis. Z is the second section axis.
    vertices=[]
    for centre,width,front,back,axis in rows:
        centre=Vector(centre); axis=Vector(axis).normalized()
        for u,v in profile:
            p=centre+axis*(u*width)+Vector((0,0,v*(front if v<0 else back)))
            vertices.append(tuple(p))
    n=len(profile)
    faces=[tuple(reversed(range(n)))]
    for j in range(len(rows)-1):
        for i in range(n):
            a=j*n+i; b=j*n+(i+1)%n
            faces.append((a,b,b+n,a+n))
    faces.append(tuple(range((len(rows)-1)*n,len(rows)*n)))
    return vertices,faces

arm_axis=(.86,.51,0)
for side,label in ((1,'Left'),(-1,'Right')):
    # Root is inside the existing shoulder and matches its ten-sided profile.
    # Shoulder vertices, cap faces, and transforms are left untouched.
    upper=[((.221,1.390,.007),.034,.066,.063,(1,0,0)),
           ((.238,1.357,.007),.037,.064,.059,arm_axis),
           ((.272,1.306,.005),.047,.067,.058,arm_axis),
           ((.312,1.240,.003),.046,.061,.052,arm_axis),
           ((.349,1.180,.003),.035,.047,.046,arm_axis),
           ((.366,1.151,.005),.031,.039,.041,arm_axis),
           ((.375,1.132,.005),.030,.036,.040,arm_axis)]
    make_mesh(label+'UpperArm',*loft(upper),side)
    fore=[((.366,1.151,.005),.031,.039,.041,arm_axis),
          ((.380,1.121,.003),.034,.044,.041,arm_axis),
          ((.403,1.081,-.001),.039,.048,.041,arm_axis),
          ((.433,1.030,-.006),.035,.041,.034,arm_axis),
          ((.466,.974,-.011),.027,.030,.025,arm_axis),
          ((.489,.930,-.014),.021,.024,.021,arm_axis),
          ((.496,.916,-.015),.020,.023,.021,arm_axis)]
    make_mesh(label+'Forearm',*loft(fore),side)

    # Palm normal lies in XY, so the palm faces inward/downward. Finger spread
    # lies along Z. Four individually readable fingers avoid a mitten silhouette.
    wrist=Vector((.493,.922,-.015))
    down=Vector((.46,-.888,-.015)).normalized()
    normal=Vector((.888,.46,0)).normalized()
    def point(t,z=0,thickness=0):
        return tuple(wrist+down*t+Vector((0,0,z))+normal*thickness)
    palm=[(point(0),.020,.023,.021,normal),
          (point(.022),.020,.034,.030,normal),
          (point(.057),.019,.041,.036,normal),
          (point(.087),.016,.038,.035,normal)]
    hv,hf=loft(palm,P8)
    components={'Palm':list(range(len(hv)))}
    def add_component(name,rows):
        vs,fs=loft(rows,P8); offset=len(hv)
        hv.extend(vs); hf.extend(tuple(i+offset for i in f) for f in fs)
        components[name]=list(range(offset,offset+len(vs)))
    for name,z,length,radius in [('Index',-.029,.067,.0088),
            ('Middle',-.009,.076,.0092),('Ring',.011,.071,.0088),
            ('Little',.029,.055,.0073)]:
        add_component(name,[
            (point(.080,z),.015,radius,radius,normal),
            (point(.106,z),.014,radius,radius,normal),
            (point(.080+length*.74,z,-.004),.012,radius*.88,radius*.88,normal),
            (point(.080+length,z,-.008),.009,radius*.72,radius*.72,normal)])
    # Thumb branches forward from thenar base, leaving a visible web gap.
    add_component('Thumb',[
        (point(.027,-.028,.002),.018,.016,.016,normal),
        (point(.047,-.052,.005),.016,.014,.014,normal),
        (point(.069,-.063,.003),.013,.012,.012,normal),
        (point(.090,-.063,0),.010,.010,.010,normal)])
    hand=make_mesh(label+'Hand',hv,hf,side)
    for name,indices in components.items():
        group=hand.vertex_groups.new(name=name); group.add(indices,1,'REPLACE')
    hand['hand_design']='Palm, four separate faceted fingers and opposed thumb; no nails or small knuckle detail. Components overlap at roots.'

assert len(collection.objects)==6
assert all(signature(bpy.data.objects[name])==value for name,value in original.items()), 'Existing body changed'
bpy.context.view_layer.update()

def stats(objects):
    points=[ob.matrix_world@v.co for ob in objects for v in ob.data.vertices]
    return {'dimensions_xyz_m':[round(max(v[i] for v in points)-min(v[i] for v in points),4) for i in range(3)],
            'polygons':sum(len(ob.data.polygons) for ob in objects),
            'triangles':sum(len(p.vertices)-2 for ob in objects for p in ob.data.polygons)}
all_meshes=[ob for ob in scene.objects if ob.type=='MESH']
report={'added':list(ob.name for ob in collection.objects),'new_geometry':stats(list(collection.objects)),
        'whole_model':stats(all_meshes),'preserved_stage2_meshes':len(original),
        'a_pose_hand_span_m':stats(all_meshes)['dimensions_xyz_m'][0],
        'shoulder_transition':'New upper-arm root loops fitted inside unmodified shoulder caps; no gaps, no shoulder edits.',
        'hands':'Four separate simplified fingers plus separated thumb per hand; six components grouped in each Hand mesh.'}
scene['stage3_report']=json.dumps(report)
scene['stage_notes']='Stage 3: arms and hands only. All 21 stage-2 meshes preserved exactly. No head, rig or animation.'

def aim(ob,target):
    back=(ob.location-Vector(target)).normalized()
    right=Vector((0,1,0)).cross(back).normalized()
    up=back.cross(right).normalized()
    ob.rotation_euler=Matrix((right,up,back)).transposed().to_euler()

views=[('Front',(0,.82,-4)),('FrontThreeQuarter',(2.5,1.05,-4)),
       ('Side',(4,.82,0)),('Back',(0,.82,4))]
scene.render.resolution_x=1000; scene.render.resolution_y=1100
scene.render.resolution_percentage=100
for name,pos in views:
    cam=bpy.data.objects[name]; cam.location=pos; cam.data.ortho_scale=1.82
    aim(cam,(0,.815,0))
scene.camera=bpy.data.objects['FrontThreeQuarter']
scene.render.filepath=str(OUT/'lowpoly_human_stage3_frontthreequarter.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_stage3.blend'))
for name,_ in views:
    scene.camera=bpy.data.objects[name]
    scene.render.filepath=str(OUT/('lowpoly_human_stage3_'+name.lower()+'.png'))
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['FrontThreeQuarter']
scene.render.filepath=str(OUT/'lowpoly_human_stage3_frontthreequarter.png')
print('STAGE3_REPORT '+json.dumps(report))
