"""Stage 1: original, standalone anatomical lower-body blockout.

Run with Blender MCP run_blender_script. No source assets or generators read.
Coordinates are X lateral, Y up, -Z forward; metres throughout.
"""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets' / 'Models'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
body = bpy.data.collections.new('Stage1_AnatomicalRegions')
scene.collection.children.link(body)
studio = bpy.data.collections.new('PreviewStudio')
scene.collection.children.link(studio)

def material(name, color):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Roughness'].default_value = .8
    return m

clay = material('Warm limestone', (.48, .36, .25))
joint = material('Joint limestone', (.40, .30, .22))
pelvic = material('Pelvic limestone', (.53, .41, .30))

def mesh(name, vertices, faces, mat=clay):
    me = bpy.data.meshes.new(name + '_Mesh')
    me.from_pydata(vertices, [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    body.objects.link(ob)
    ob.data.materials.append(mat)
    # Recalculate outward normals on each independent closed volume.
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    ob.select_set(False)
    ob['stage'] = 1
    ob['technique'] = 'Original manually specified polygonal anatomical sections'
    return ob

# Twelve-sided non-circular section: front ridge, broad oblique planes,
# flatter lateral walls and paired posterior planes. Width/depth vary per loop.
PROFILE = [(0,-1),(.55,-.87),(.91,-.48),(1,0),(.86,.58),(.48,.92),
           (0,1),(-.48,.92),(-.86,.58),(-1,0),(-.91,-.48),(-.55,-.87)]

def volume(name, rows, side=1, mat=clay):
    # Each row: height, lateral centre, half width, front depth, rear depth, Z centre.
    vs = []
    for y, x, w, front, back, z in rows:
        for px, pz in PROFILE:
            vs.append((side*(x+px*w), y, z+pz*(front if pz<0 else back)))
    n = len(PROFILE)
    fs = [tuple(reversed(range(n)))]
    for j in range(len(rows)-1):
        for i in range(n):
            a=j*n+i; b=j*n+(i+1)%n
            fs.append((a,b,b+n,a+n))
    fs.append(tuple(range((len(rows)-1)*n,len(rows)*n)))
    return mesh(name, vs, fs, mat)

volume('Pelvis', [(.835,0,.105,.078,.064,0),(.89,0,.153,.100,.085,0),
    (.965,0,.177,.111,.091,0),(1.025,0,.169,.096,.086,0),
    (1.065,0,.145,.080,.073,0)], mat=pelvic)
volume('Groin', [(.778,0,.025,.024,.025,-.012),(.811,0,.050,.053,.039,-.022),
    (.860,0,.077,.064,.045,-.030),(.901,0,.082,.054,.039,-.026)], mat=pelvic)

# Glutes remain one named region, with two separately built lobes and a cleft.
lobes=[]
for s in (-1,1):
    lobes.append(volume('GluteLobe', [(.802,.084,.043,.025,.045,.055),
        (.839,.091,.068,.042,.083,.061),(.895,.090,.079,.048,.093,.060),
        (.949,.085,.074,.042,.078,.052),(.995,.076,.060,.033,.048,.042)],s,pelvic))
bpy.ops.object.select_all(action='DESELECT')
for ob in lobes: ob.select_set(True)
bpy.context.view_layer.objects.active=lobes[0]
bpy.ops.object.join()
lobes[0].name='Glutes'
lobes[0].select_set(False)

for side, label in ((1,'Left'),(-1,'Right')):
    volume(label+'Thigh', [(.535,.151,.050,.062,.056,.004),
        (.574,.148,.063,.078,.069,.006),(.630,.142,.080,.095,.082,.008),
        (.735,.129,.091,.114,.094,.010),(.827,.112,.087,.109,.092,.009),
        (.882,.104,.075,.086,.074,.008),(.923,.103,.059,.063,.056,.010)],side)
    volume(label+'Knee', [(.475,.157,.045,.047,.044,.003),
        (.499,.155,.052,.061,.048,-.002),(.529,.153,.054,.074,.048,-.004),
        (.552,.151,.051,.067,.050,0),(.567,.149,.048,.054,.050,.004)],side,joint)
    volume(label+'Calf', [(.155,.174,.032,.032,.036,.012),
        (.203,.173,.036,.039,.045,.019),(.274,.169,.046,.046,.066,.025),
        (.351,.163,.066,.053,.087,.025),(.401,.159,.067,.055,.082,.021),
        (.448,.157,.055,.049,.065,.012),(.489,.157,.043,.043,.043,.005)],side)
    volume(label+'Ankle', [(.065,.176,.037,.049,.047,.005),
        (.100,.176,.040,.043,.042,.010),(.134,.175,.037,.034,.039,.013),
        (.163,.174,.031,.032,.036,.013),(.185,.173,.032,.035,.038,.016)],side,joint)
    # Longitudinal foot sections: heel, arch, high instep, ball and blunt toes.
    # Inner sole lifts along the arch; heel and forefoot touch Y=0.
    sections=[(.094,.035,.008,.070),(.064,.043,0,.099),
        (.008,.044,0,.121),(-.051,.046,.015,.113),
        (-.115,.057,.007,.074),(-.181,.062,0,.052),
        (-.215,.054,.006,.041)]
    vs=[]
    for z,w,sole,top in sections:
        for dx,y in [(-.78*w,sole),(.78*w,0 if sole<.01 else .004),
            (w,.027),(.81*w,top*.78),(.25*w,top),
            (-.42*w,top*.95),(-w,.035)]:
            vs.append((side*(.176+dx),y,z))
    n=7
    fs=[tuple(reversed(range(n)))]
    for j in range(len(sections)-1):
        for i in range(n):
            a=j*n+i; b=j*n+(i+1)%n
            fs.append((a,b,b+n,a+n))
    fs.append(tuple(range((len(sections)-1)*n,len(sections)*n)))
    mesh(label+'Foot',vs,fs)

scene['stage_notes']='Stage 1 only. Intended full height 1.85 m. +Y up; -Z forward. Separate overlapping editable anatomical volumes, not final welded topology.'
scene['intended_full_height_m']=1.85

def aim(ob, target):
    backward=(ob.location-Vector(target)).normalized()
    right=Vector((0,1,0)).cross(backward).normalized()
    up=backward.cross(right).normalized()
    ob.rotation_euler=Matrix((right,up,backward)).transposed().to_euler()

def area(name, pos, power, size):
    data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
    ob=bpy.data.objects.new(name,data); studio.objects.link(ob); ob.location=pos; aim(ob,(0,.55,0))

area('Key',(-2,3,-3),350,3)
area('Fill',(2,1.5,-1),150,2.5)
area('Rim',(0,2,2),250,2)
scene.world=bpy.data.worlds.new('Slate studio')
scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.055,.070,.09,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
scene.render.engine='BLENDER_EEVEE'
scene.eevee.use_gtao=True
scene.eevee.gtao_distance=.12
scene.eevee.gtao_factor=1.2
scene.eevee.taa_render_samples=96
scene.view_settings.view_transform='Standard'
scene.view_settings.look='Medium High Contrast'
scene.view_settings.exposure=0
scene.view_settings.gamma=1
scene.render.image_settings.file_format='PNG'
scene.render.resolution_x=600
scene.render.resolution_y=820
scene.render.resolution_percentage=100
views=[('Front',(0,.54,-3)),('FrontThreeQuarter',(2,.85,-3)),('Side',(3,.54,0))]
cams=[]
for name,pos in views:
    data=bpy.data.cameras.new(name); data.type='ORTHO'; data.ortho_scale=1.24
    ob=bpy.data.objects.new(name,data); studio.objects.link(ob); ob.location=pos; aim(ob,(0,.53,0)); cams.append(ob)
scene.camera=cams[1]
# Make the saved interactive viewport respect the requested Y-up convention.
for screen in bpy.data.screens:
    for ar in screen.areas:
        if ar.type=='VIEW_3D':
            ar.spaces.active.region_3d.view_rotation=cams[1].rotation_euler.to_quaternion()
            ar.spaces.active.region_3d.view_distance=1.7
            ar.spaces.active.region_3d.view_location=(0,.53,0)
            ar.spaces.active.overlay.show_floor=False
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_stage1.blend'))

if __name__=='__main__':
    from array import array
    panels=[]
    for cam in cams:
        scene.camera=cam
        path=OUT/('lowpoly_human_stage1_'+cam.name.lower()+'.png')
        scene.render.filepath=str(path)
        bpy.ops.render.render(write_still=True)
        im=bpy.data.images.load(str(path),check_existing=False)
        pixels=array('f',[0])*(600*820*4)
        im.pixels.foreach_get(pixels)
        panels.append(pixels)
        bpy.data.images.remove(im)
    combined=array('f',[0])*(1800*820*4)
    for y in range(820):
        for k,pixels in enumerate(panels):
            start=(y*1800+k*600)*4
            combined[start:start+2400]=pixels[y*2400:(y+1)*2400]
    im=bpy.data.images.new('Stage1: Front | Front three-quarter | Side',width=1800,height=820)
    im.pixels.foreach_set(combined)
    im.filepath_raw=str(OUT/'lowpoly_human_stage1_preview.png')
    im.file_format='PNG'; im.save()
    meshes=list(body.objects)
    allv=[ob.matrix_world@v.co for ob in meshes for v in ob.data.vertices]
    report={'regions':{ob.name:{'vertices':len(ob.data.vertices),'polygons':len(ob.data.polygons)} for ob in meshes},
        'bounds_m':{axis:[min(v[i] for v in allv),max(v[i] for v in allv)] for i,axis in enumerate('XYZ')},
        'polygons':sum(len(ob.data.polygons) for ob in meshes),
        'triangles':sum(len(p.vertices)-2 for ob in meshes for p in ob.data.polygons)}
    print('STAGE1_REPORT '+json.dumps(report))
