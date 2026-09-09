"""Stage 4 head only; run using Blender MCP. Source body is never edited.

The authored mesh coordinates already use +Y up, so GLB export disables
Blender's usual Z-up to Y-up conversion. Studio objects are excluded.
"""
from pathlib import Path
import json
import bpy
import bmesh
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets'/'Models'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_stage3.blend'))
scene=bpy.context.scene

def signature(ob):
    return (tuple(tuple(v.co) for v in ob.data.vertices),
            tuple(tuple(p.vertices) for p in ob.data.polygons),
            tuple(tuple(row) for row in ob.matrix_world),
            tuple(m.name for m in ob.data.materials),
            tuple(p.use_smooth for p in ob.data.polygons))

original={o.name:signature(o) for o in scene.objects if o.type=='MESH'}
collection=bpy.data.collections.new('Stage4_Head')
scene.collection.children.link(collection)
skin=bpy.data.materials['Warm limestone'].copy(); skin.name='Skin'
eyes=bpy.data.materials.new('Eyes'); eyes.use_nodes=True
eyes.diffuse_color=(.19,.14,.10,1)
shader=eyes.node_tree.nodes.get('Principled BSDF')
shader.inputs['Base Color'].default_value=eyes.diffuse_color
shader.inputs['Roughness'].default_value=.9

def mesh(name,vs,fs,material=skin):
    used=sorted({i for face in fs for i in face}); remap={a:b for b,a in enumerate(used)}
    data=bpy.data.meshes.new(name+'_Mesh')
    data.from_pydata([vs[i] for i in used],[],[tuple(remap[i] for i in f) for f in fs]); data.update()
    bm=bmesh.new(); bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(data); bm.free()
    ob=bpy.data.objects.new(name,data); collection.objects.link(ob); data.materials.append(material)
    ob['stage']=4
    return ob

profile=[(0,-1),(.44,-.99),(.78,-.87),(.96,-.49),(1,0),(.94,.49),
         (.72,.86),(.38,.99),(0,1),(-.38,.99),(-.72,.86),(-.94,.49),
         (-1,0),(-.96,-.49),(-.78,-.87),(-.44,-.99)]
# Y, half width, anterior radius, posterior radius, Z centre.
rows=[(1.613,.044,.038,.048,.010),
      (1.642,.052,.071,.059,.004),
      (1.659,.062,.082,.067,.003),
      (1.681,.070,.084,.075,.003),
      (1.706,.078,.087,.083,.003),
      (1.723,.079,.078,.086,.003),
      (1.737,.078,.087,.088,.003),
      (1.768,.077,.087,.089,.004),
      (1.805,.070,.076,.081,.006),
      (1.831,.052,.055,.061,.009),
      (1.847,.027,.029,.033,.011)]
vertices=[]
for y,w,front,back,z in rows:
    for px,pz in profile:
        vertices.append((w*px,y,z+pz*(front if pz<0 else back)))
lower=[tuple(reversed(range(16)))]; upper=[]
for j in range(len(rows)-1):
    for i in range(16):
        a=j*16+i; b=j*16+(i+1)%16
        (lower if j<5 else upper).append((a,b,b+16,a+16))
vertices.append((0,1.850,.011)); tip=len(vertices)-1
for i in range(16): upper.append((160+i,160+(i+1)%16,tip))
mesh('LowerSkullJaw',vertices,lower)
mesh('UpperSkullCranium',vertices,upper)

# Low-relief angular nose: bridge, side planes, small tip and underside.
nv=[(-.010,1.737,-.080),(.010,1.737,-.080),
    (-.012,1.704,-.088),(.012,1.704,-.088),
    (-.009,1.704,-.113),(.009,1.704,-.113),
    (-.014,1.698,-.086),(.014,1.698,-.086)]
mesh('NosePlane',nv,[(0,1,5,4),(0,4,2),(1,3,5),(2,4,6),(3,7,5),(4,5,7,6),(0,2,6,7,3,1)])

# Subdued narrow eye planes sit within the shallow, modelled orbital band.
# No whites, pupils, glossy eyeballs, or realistic eye detail.
for side,label in ((1,'Left'),(-1,'Right')):
    coords=[(.019,1.725,-.0770),(.045,1.725,-.0750),
            (.047,1.720,-.0750),(.020,1.720,-.0770)]
    mesh(label+'EyeArea',[(side*x,y,z) for x,y,z in coords],[(0,1,2,3)],eyes)
    # Small flattened ears, embedded at roots and faceted around the helix.
    ear=[(.073,1.728,.006),(.081,1.734,.008),(.090,1.728,.010),
         (.093,1.711,.011),(.088,1.691,.010),(.079,1.687,.006),
         (.073,1.696,.004),(.083,1.711,-.004),
         (.078,1.711,.023)]
    fs=[]
    for i in range(7):
        fs.extend([(i,(i+1)%7,7),((i+1)%7,i,8)])
    mesh(label+'Ear',[(side*x,y,z) for x,y,z in ear],fs)

# A thin neutral mouth line follows the face's faceted anterior contour.
mv=[(-.025,1.677,-.0811),(0,1.678,-.0810),(.025,1.677,-.0811),
    (.022,1.6755,-.0813),(0,1.6757,-.0815),(-.022,1.6755,-.0813)]
mesh('MouthIndication',mv,[(0,1,4,5),(1,2,3,4)],eyes)

import runpy
runpy.run_path(str(ROOT/'Tools'/'Blender'/'refine_stage4_head.py'))['refine_head'](collection)
runpy.run_path(str(ROOT/'Tools'/'Blender'/'refine_stage4_head.py'))['refine_head_second'](collection)
scene['head_mass_refined']=True
scene['head_mass_refined_second']=True

assert all(signature(bpy.data.objects[n])==s for n,s in original.items()), 'Body changed'
bpy.context.view_layer.update()
def stats(objects):
    points=[ob.matrix_world@v.co for ob in objects for v in ob.data.vertices]
    return {'dimensions_xyz_m':[round(max(p[i] for p in points)-min(p[i] for p in points),4) for i in range(3)],
            'triangles':sum(len(p.vertices)-2 for o in objects for p in o.data.polygons)}
all_meshes=[o for o in scene.objects if o.type=='MESH']
report={'added':[o.name for o in collection.objects],'head':stats(list(collection.objects)),
        'whole_model':stats(all_meshes),'preserved_body_meshes':len(original),
        'skull_width_m':stats([bpy.data.objects['LowerSkullJaw'],bpy.data.objects['UpperSkullCranium']])['dimensions_xyz_m'][0],
        'head_width_with_ears_m':stats(list(collection.objects))['dimensions_xyz_m'][0],
        'head_height_m':stats(list(collection.objects))['dimensions_xyz_m'][1],'body_preserved_exactly':True}
scene['stage4_report']=json.dumps(report)
scene['stage_notes']='Stage 4 head only. All Stage 3 body meshes unchanged; +Y up, -Z forward. No hair, gear, rig, animation.'

def aim(ob,target):
    back=(ob.location-Vector(target)).normalized(); right=Vector((0,1,0)).cross(back).normalized()
    up=back.cross(right).normalized()
    ob.rotation_euler=Matrix((right,up,back)).transposed().to_euler()

views=[('Front',(0,.93,-4)),('FrontThreeQuarter',(2.5,1.10,-4)),
       ('Side',(4,.93,0)),('Back',(0,.93,4))]
scene.render.resolution_x=1200; scene.render.resolution_y=1400
scene.render.resolution_percentage=100
for name,pos in views:
    cam=bpy.data.objects[name]; cam.location=pos; cam.data.ortho_scale=2.02
    aim(cam,(0,.925,0))
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            region=area.spaces.active.region_3d
            region.view_rotation=bpy.data.objects['FrontThreeQuarter'].rotation_euler.to_quaternion()
            region.view_location=(0,.925,0); region.view_distance=2.65
for ob in scene.objects: ob.select_set(ob.type=='MESH')
bpy.context.view_layer.objects.active=bpy.data.objects['UpperSkullCranium']
scene.camera=bpy.data.objects['FrontThreeQuarter']
scene.render.filepath=str(OUT/'lowpoly_human_stage4_frontthreequarter.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_stage4.blend'))
bpy.ops.export_scene.gltf(filepath=str(OUT/'lowpoly_human_stage4.glb'),export_format='GLB',
    use_selection=True,export_yup=False,export_animations=False,export_cameras=False,export_lights=False)
for name,_ in views:
    scene.camera=bpy.data.objects[name]
    scene.render.filepath=str(OUT/('lowpoly_human_stage4_'+name.lower()+'.png'))
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['FrontThreeQuarter']
scene.render.filepath=str(OUT/'lowpoly_human_stage4_frontthreequarter.png')
print('STAGE4_REPORT '+json.dumps(report))
