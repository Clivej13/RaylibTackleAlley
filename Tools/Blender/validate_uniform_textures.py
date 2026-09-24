"""Run via Blender MCP after create_uniform_textures.py. Never writes the input GLB."""
from pathlib import Path
import bpy, json, hashlib
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Textures/Uniforms'
PRE=OUT/'Validation'; PRE.mkdir(exist_ok=True)
source=ROOT/'Assets/Models/football_player.glb'
digest=hashlib.sha256(source.read_bytes()).hexdigest()
regions=json.loads((ROOT/'Assets/Models/UniformAtlas/atlas_regions.json').read_text())
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
def signature():
    h=hashlib.sha256()
    for o in sorted(bpy.data.objects,key=lambda o:o.name):
        if o.type=='MESH':
            h.update(o.name.encode())
            h.update(np.array([v.co[:] for v in o.data.vertices],dtype=np.float64).tobytes())
            h.update(np.array([l.vertex_index for l in o.data.loops]).tobytes())
            for uv in o.data.uv_layers: h.update(np.array([v.uv[:] for v in uv.data]).tobytes())
            h.update(str([(m.name if m else '') for m in o.data.materials]).encode())
            h.update(np.array([p.material_index for p in o.data.polygons]).tobytes())
    return h.hexdigest()
before=signature()
mat=bpy.data.materials['Uniform']
node=next(n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE')
node.image=bpy.data.images.load(str(OUT/'test_uniform.png'))
node.image.colorspace_settings.name='sRGB'
coverage=np.zeros((2048,2048),dtype=np.uint16)
stats={k:dict(triangles=0,positive_uv_winding=0,negative_uv_winding=0,min_rectangle_gutter_px=2048) for k in regions}
bad=[]; degenerate=0
for ob in bpy.data.objects:
    if ob.type!='MESH' or not ob.data.uv_layers: continue
    me=ob.data; me.calc_loop_triangles()
    uv=me.uv_layers.active.data
    for tri in me.loop_triangles:
        if me.materials[tri.material_index]!=mat: continue
        q=np.array([uv[i].uv[:] for i in tri.loops]); center=q.mean(axis=0)
        names=[k for k,(x0,y0,x1,y1) in regions.items() if x0<=center[0]<=x1 and y0<=center[1]<=y1]
        if len(names)!=1: bad.append(ob.name); continue
        name=names[0]; x0,y0,x1,y1=regions[name]
        gutter=float(min((q[:,0]-x0).min(),(x1-q[:,0]).min(),(q[:,1]-y0).min(),(y1-q[:,1]).min())*2048)
        st=stats[name]; st['triangles']+=1; st['min_rectangle_gutter_px']=min(st['min_rectangle_gutter_px'],gutter)
        if gutter<-.001: bad.append(ob.name+':'+name)
        a,b,c=q*2048
        den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(den)<1e-9: degenerate+=1; continue
        st['positive_uv_winding' if den>0 else 'negative_uv_winding']+=1
        lo=np.maximum(np.floor((q*2048).min(axis=0)).astype(int),0)
        hi=np.minimum(np.ceil((q*2048).max(axis=0)).astype(int),2047)
        xx,yy=np.meshgrid(np.arange(lo[0],hi[0]+1)+.5,np.arange(lo[1],hi[1]+1)+.5)
        u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
        v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
        coverage[lo[1]:hi[1]+1,lo[0]:hi[0]+1]+=((u>1e-5)&(v>1e-5)&(u+v<1-1e-5))
report={'source_sha256':digest,'regions':stats,'overlapping_triangle_interior_pixels_2048':int((coverage>1).sum()),'region_crossings':bad,'degenerate_uv_triangles':degenerate,'mesh_uv_material_assignment_signature_unchanged':signature()==before,'runtime_game_tested':False}
for o in bpy.data.objects:
    if o.type=='ARMATURE': o.data.pose_position='REST'
s=bpy.context.scene; s.render.engine='BLENDER_EEVEE'; s.eevee.taa_render_samples=48
s.world=bpy.data.worlds.new('ValidationWorld'); s.world.color=(.28,.28,.28)
s.view_settings.view_transform='Standard'; s.view_settings.look='Medium High Contrast'
s.render.resolution_x=800; s.render.resolution_y=1000; s.render.resolution_percentage=100
s.render.image_settings.file_format='PNG'
for loc in [(3,4,5),(-3,1,3),(0,-4,4)]:
    bpy.ops.object.light_add(type='AREA',location=loc)
    o=bpy.context.object; o.name='Validation light'; o.data.energy=450; o.data.size=5
    o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
views=[('front',(0,4,1.05)),('back',(0,-4,1.05)),('left',(4,0,1.05)),('right',(-4,0,1.05)),('front_three_quarter',(3,4,2)),('rear_three_quarter',(-3,-4,2))]
for name,loc in views:
    bpy.ops.object.camera_add(location=loc); cam=bpy.context.object; cam.name='Validation_'+name
    cam.rotation_euler=(Vector((0,0,.98))-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'; cam.data.ortho_scale=2.12; s.camera=cam
    s.render.filepath=str(PRE/(name+'.png')); bpy.ops.render.render(write_still=True)
s.camera=bpy.data.objects['Validation_front_three_quarter']
node.image.filepath='//../test_uniform.png'
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(PRE/'uniform_validation.blend'))
assert signature()==before
assert hashlib.sha256(source.read_bytes()).hexdigest()==digest
report['input_glb_unchanged']=True
(PRE/'validation.json').write_text(json.dumps(report,indent=2))
print('VALIDATION_REPORT',json.dumps(report))
