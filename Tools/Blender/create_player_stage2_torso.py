"""Extend the approved stage-1 blend without rebuilding any lower-body mesh.

Run through Blender MCP. All measurements in metres; +Y up, -Z forward.
Torso regions share matching surface boundaries but remain separate objects.
"""
from pathlib import Path
import bpy
import bmesh
import json
import hashlib
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets' / 'Models'
SOURCE = OUT / 'lowpoly_human_stage1.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene = bpy.context.scene

def signature(ob):
    return {'vertices': [tuple(v.co) for v in ob.data.vertices],
            'faces': [tuple(p.vertices) for p in ob.data.polygons],
            'matrix': [list(row) for row in ob.matrix_world],
            'materials': [m.name for m in ob.data.materials]}

original = {ob.name: signature(ob) for ob in scene.objects if ob.type == 'MESH'}
collection = bpy.data.collections.new('Stage2_TorsoRegions')
scene.collection.children.link(collection)
mat = bpy.data.materials['Warm limestone']

def mesh(name, vs, fs):
    used = sorted({i for f in fs for i in f})
    mapping = {old:new for new,old in enumerate(used)}
    data = bpy.data.meshes.new(name + '_Mesh')
    data.from_pydata([vs[i] for i in used], [], [tuple(mapping[i] for i in f) for f in fs])
    data.update()
    bm = bmesh.new(); bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data); bm.free()
    ob = bpy.data.objects.new(name, data)
    collection.objects.link(ob)
    data.materials.append(mat)
    ob['stage'] = 2
    ob['construction'] = 'Flat-shaded anatomical polygon sections; separate editable region'
    return ob

# Sixteen vertices: broad anterior planes, obliques, lats, scapular planes.
# Index 0 is sternum, 4 is left flank, 8 is spine, 12 is right flank.
profile = [(0,-.96),(.40,-1),(.76,-.94),(.94,-.60),(1,0),
           (.94,.57),(.74,.92),(.39,1),(0,.94),(-.39,1),
           (-.74,.92),(-.94,.57),(-1,0),(-.94,-.60),(-.76,-.94),(-.40,-1)]
# Height, half-width, front depth, back depth, fore/aft offset.
rows = [(1.045,.150,.081,.074,0), (1.080,.146,.084,.077,0),
        (1.135,.149,.089,.081,.001), (1.205,.162,.101,.090,.002),
        (1.275,.181,.114,.101,.003), (1.325,.193,.128,.110,.004),
        (1.390,.201,.137,.114,.005), (1.450,.199,.128,.108,.006),
        (1.490,.185,.110,.100,.007), (1.535,.116,.079,.079,.010),
        (1.565,.065,.056,.059,.012)]
vs=[]
for y,w,front,back,z in rows:
    for px,pz in profile:
        vs.append((px*w,y,z+pz*(front if pz<0 else back)))
regions = {n:[] for n in ('LowerBack','Belly','LeftChest','RightChest','UpperBack')}
for j in range(len(rows)-1):
    for i in range(16):
        front = i<4 or i>=12
        name = ('Belly' if front else 'LowerBack') if j<4 else (
            ('LeftChest' if i<4 else 'RightChest') if front else 'UpperBack')
        a=j*16+i; b=j*16+(i+1)%16
        regions[name].append((a,b,b+16,a+16))
for name,faces in regions.items(): mesh(name,vs,faces)

def closed_sections(name, sections, section_profile):
    vertices=[]
    for y,x,w,front,back,z in sections:
        for px,pz in section_profile:
            vertices.append((x+px*w,y,z+pz*(front if pz<0 else back)))
    n=len(section_profile)
    faces=[tuple(reversed(range(n)))]
    for j in range(len(sections)-1):
        for i in range(n):
            a=j*n+i; b=j*n+(i+1)%n
            faces.append((a,b,b+n,a+n))
    faces.append(tuple(range((len(sections)-1)*n,len(sections)*n)))
    return mesh(name,vertices,faces)

shoulder_profile=[(0,-1),(.60,-.90),(1,-.40),(1,.30),(.65,.84),
                  (0,1),(-.65,.80),(-1,.30),(-1,-.40),(-.60,-.90)]
for side,label in ((1,'Left'),(-1,'Right')):
    sections=[(1.375,.221,.032,.063,.060,.007),
              (1.402,.224,.036,.081,.074,.007),
              (1.452,.218,.042,.092,.084,.007),
              (1.493,.200,.043,.079,.074,.007),
              (1.523,.181,.029,.052,.053,.009)]
    ob=closed_sections(label+'Shoulder',sections,shoulder_profile)
    if side<0:
        for v in ob.data.vertices: v.co.x *= -1
        bm=bmesh.new(); bm.from_mesh(ob.data)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(ob.data); bm.free()
    group=ob.vertex_groups.new(name='Stage3_UpperArmAttachment')
    group.add(list(range(10)),1,'REPLACE')
    ob['attachment']='Planar ten-vertex inferior cap at Y=1.375; remove cap when attaching arm.'

neck=closed_sections('NeckBase',[(1.550,0,.070,.059,.062,.012),
    (1.575,0,.061,.053,.058,.012),(1.605,0,.057,.052,.056,.013),
    (1.630,0,.057,.052,.056,.013)],profile)
g=neck.vertex_groups.new(name='FutureHeadAttachment')
g.add(list(range(48,64)),1,'REPLACE')
neck['attachment']='Planar top perimeter at Y=1.630; neck base only.'

assert len(collection.objects)==8
assert all(signature(bpy.data.objects[name])==saved for name,saved in original.items()), 'Lower body changed'
scene['stage_notes']='Stage 2 torso added to unchanged stage 1. Eight separate torso regions; matching unwelded boundaries. No arms/head/rig.'
scene['lower_body_preservation_verified']=True
scene['intended_full_height_m']=1.85

def aim(ob,target):
    back=(ob.location-Vector(target)).normalized()
    right=Vector((0,1,0)).cross(back).normalized()
    up=back.cross(right).normalized()
    ob.rotation_euler=Matrix((right,up,back)).transposed().to_euler()

# Studio changes affect presentation only, not inherited meshes or materials.
studio=bpy.data.collections['PreviewStudio']
for name,pos,energy in [('Key',(-2,3,-3),240),('Fill',(2,2,-2),120),('Rim',(-1,2.5,3),280)]:
    ob=bpy.data.objects[name]; ob.location=pos; ob.data.energy=energy; aim(ob,(0,1,0))
scene.view_settings.look='Medium High Contrast'
scene.view_settings.exposure=-.45
scene.render.resolution_x=760
scene.render.resolution_y=1080
scene.render.resolution_percentage=100
scene.eevee.taa_render_samples=128
views=[('Front',(0,.82,-4)),('FrontThreeQuarter',(2.5,1.05,-4)),
       ('Side',(4,.82,0)),('Back',(0,.82,4))]
cameras=[]
for name,pos in views:
    ob=bpy.data.objects.get(name)
    if ob is None:
        data=bpy.data.cameras.new(name); ob=bpy.data.objects.new(name,data); studio.objects.link(ob)
    ob.data.type='ORTHO'; ob.data.ortho_scale=1.80
    ob.location=pos; aim(ob,(0,.815,0)); cameras.append(ob)
scene.camera=cameras[1]
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            region=area.spaces.active.region_3d
            region.view_rotation=cameras[1].rotation_euler.to_quaternion()
            region.view_location=(0,.815,0); region.view_distance=2.35
            area.spaces.active.overlay.show_floor=False
bpy.context.view_layer.update()

def stats(objects):
    points=[ob.matrix_world@v.co for ob in objects for v in ob.data.vertices]
    return {'dimensions_m':[round(max(v[i] for v in points)-min(v[i] for v in points),4) for i in range(3)],
            'polygons':sum(len(ob.data.polygons) for ob in objects),
            'triangles':sum(len(p.vertices)-2 for ob in objects for p in ob.data.polygons)}
report={'regions_added':list(regions)+['LeftShoulder','RightShoulder','NeckBase'],
        'torso':stats(list(collection.objects)),
        'whole_model':stats([ob for ob in scene.objects if ob.type=='MESH']),
        'shoulder_width_m':.520,'waist_width_m':.292,'chest_width_m':.402,
        'chest_depth_m':.251,'lower_body_unchanged':True,
        'pelvis_connection':'No pelvis edits. New torso starts at Y=1.045, overlapping the original pelvis by 20 mm.'}
scene['stage2_report']=json.dumps(report)
scene.render.filepath=str(OUT/'lowpoly_human_stage2_frontthreequarter.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_stage2.blend'))
for cam in cameras:
    scene.camera=cam
    scene.render.filepath=str(OUT/('lowpoly_human_stage2_'+cam.name.lower()+'.png'))
    bpy.ops.render.render(write_still=True)
print('STAGE2_REPORT '+json.dumps(report))
