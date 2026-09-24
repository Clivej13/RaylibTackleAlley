"""Run through Blender MCP. Add a uniform atlas without resampling the source GLB.

Blender unwraps temporary meshes; the GLB writer copies every original corner
attribute verbatim and appends only UV/seam data, material and PNG. Original
nodes, skins, animations and their binary accessors are retained exactly.
"""
from pathlib import Path
import bpy, json, struct, copy, math, hashlib
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
DEST = OUT / 'football_player.glb'
ART = OUT / 'UniformAtlas'
ART.mkdir(exist_ok=True)
DT = {5120:'i1',5121:'u1',5122:'<i2',5123:'<u2',5125:'<u4',5126:'<f4'}
NC = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}

def read_glb(path):
    raw = path.read_bytes()
    n = struct.unpack_from('<I',raw,12)[0]
    return json.loads(raw[20:20+n]), bytearray(raw[28+n:])

doc, binary = read_glb(DEST)
if any(m.get('name') == 'Uniform' for m in doc['materials']):
    # Deterministic reruns start with the preserved source, not prior UV splits.
    doc, binary = read_glb(ART / 'football_player_before_atlas.glb')
else:
    (ART / 'football_player_before_atlas.glb').write_bytes(DEST.read_bytes())
original = copy.deepcopy(doc)
original_binary = bytes(binary)

def values(ai, d=None, b=None):
    d = doc if d is None else d
    b = binary if b is None else b
    a = d['accessors'][ai]; v = d['bufferViews'][a['bufferView']]
    dt = np.dtype(DT[a['componentType']]); nc = NC[a['type']]
    return np.ndarray((a['count'],nc),dtype=dt,buffer=b,
        offset=v.get('byteOffset',0)+a.get('byteOffset',0),
        strides=(v.get('byteStride',dt.itemsize*nc),dt.itemsize)).copy()

def blob(data, target=None):
    binary.extend(b'\0' * ((-len(binary)) % 4))
    v = {'buffer':0,'byteOffset':len(binary),'byteLength':len(data)}
    if target: v['target'] = target
    binary.extend(data); doc['bufferViews'].append(v)
    return len(doc['bufferViews'])-1

def accessor(data, template=None, kind=None):
    data = np.ascontiguousarray(data)
    a = copy.deepcopy(template) if template else {'componentType':5126,'type':kind}
    a.pop('byteOffset',None); a.pop('sparse',None)
    a.update(bufferView=blob(data.tobytes(),34962),count=len(data))
    if 'min' in a: a['min'] = data.min(axis=0).tolist()
    if 'max' in a: a['max'] = data.max(axis=0).tolist()
    doc['accessors'].append(a); return len(doc['accessors'])-1

# Bounds in Blender UV coordinates, bottom left origin. Generous dedicated
# chest/back panels; independent left/right sleeves, socks and helmet sides.
regions = {
 'Chest':(0,.59,.32,1), 'Back':(.32,.59,.64,1),
 'Sleeve_L':(.64,.79,.82,1), 'Sleeve_R':(.82,.79,1,1),
 'Helmet_L':(.64,.59,.82,.79), 'Helmet_R':(.82,.59,1,.79),
 'Trousers_front':(0,.26,.25,.59), 'Trousers_back':(.25,.26,.5,.59),
 'Sock_L':(.5,.26,.65,.59), 'Sock_R':(.65,.26,.8,.59),
 'Helmet_crown':(.8,.26,1,.59),
 'Undershirt':(0,0,.35,.26), 'Jersey_sides_trim':(.35,0,.55,.26), 'Sock_underlayers':(.55,0,1,.26),
}
groups = {k:[] for k in regions}
changed = []
node_names = {n['mesh']:n.get('name','') for n in doc['nodes'] if 'mesh' in n}
target_materials = {'Jersey','Jersey_Sleeves','Pants','Socks','Helmet','Undershirt'}
for mi,m in enumerate(doc['meshes']):
    for pi,p in enumerate(m['primitives']):
        mat = doc['materials'][p['material']]['name']
        if mat not in target_materials: continue
        assert p.get('mode',4)==4 and not p.get('targets')
        ix = values(p['indices']).ravel().astype(int)
        pos = values(p['attributes']['POSITION'])
        uv = np.zeros((len(ix),2),dtype='<f4')
        item = {'mi':mi,'pi':pi,'ix':ix,'uv':uv,'pos':pos,'name':node_names[mi]}
        changed.append(item)
        for t in range(0,len(ix),3):
            q = pos[ix[t:t+3]]; c = q.mean(axis=0)
            normal = np.cross(q[1]-q[0],q[2]-q[0])
            if mat=='Jersey':
                region=('Chest' if normal[2]<0 else 'Back') if abs(normal[2])>max(abs(normal[0]),abs(normal[1])) else 'Jersey_sides_trim'
                if np.max(np.abs(q[:,0]))<.081 and np.min(q[:,1])>1.53:
                    region='Jersey_sides_trim'
            elif mat=='Jersey_Sleeves': region='Sleeve_L' if c[0]>0 else 'Sleeve_R'
            elif mat=='Pants': region='Trousers_front' if normal[2]<0 else 'Trousers_back'
            elif mat=='Helmet':
                region=('Helmet_crown' if abs(normal[1])>abs(normal[0])*1.15
                        else ('Helmet_L' if c[0]>0 else 'Helmet_R'))
            elif mat=='Undershirt': region='Undershirt'
            elif node_names[mi]=='Socks': region='Sock_L' if c[0]>0 else 'Sock_R'
            else: region='Sock_underlayers'
            groups[region].append((item,t))

bpy.ops.wm.read_factory_settings(use_empty=True)
svg = ['<svg xmlns="http://www.w3.org/2000/svg" width="2048" height="2048" viewBox="0 0 2048 2048">',
       '<rect width="2048" height="2048" fill="#222"/>']
uv_triangles=[]
for name, tris in groups.items():
    if not tris: continue
    verts=[]; faces=[]; lookup={}
    for item,t in tris:
        face=[]
        for vi in item['ix'][t:t+3]:
            co=tuple(float(v) for v in item['pos'][vi])
            # Welding only in this disposable unwrap mesh joins hard-normal
            # splits; the actual asset retains every original normal/weight.
            key=(item['mi'],co)
            if key not in lookup: lookup[key]=len(verts); verts.append((co[0],-co[2],co[1]))
            face.append(lookup[key])
        faces.append(face)
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    ob=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active=ob; ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(75),island_margin=.025,area_weight=0,
                             correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    x0,y0,x1,y1=regions[name]; pad=12/2048
    raw=np.array([tuple(v.uv) for v in mesh.uv_layers.active.data])
    if name in ('Chest','Back'):
        # Orthographic cloth panels keep center chest/back continuous and art
        # upright. Curvature naturally compresses toward the side seam.
        sign=-1 if name=='Chest' else 1
        raw=np.array([(sign*mesh.vertices[loop.vertex_index].co.x,
                       mesh.vertices[loop.vertex_index].co.z) for loop in mesh.loops])
    lo=raw.min(axis=0); hi=raw.max(axis=0)
    scale=min((x1-x0-2*pad)/(hi[0]-lo[0]),(y1-y0-2*pad)/(hi[1]-lo[1]))
    origin=np.array([(x0+x1)/2,(y0+y1)/2])-(lo+hi)*scale/2
    for poly,(item,t) in zip(mesh.polygons,tris):
        result=raw[list(poly.loop_indices)]*scale+origin
        item['uv'][t:t+3]=result
        uv_triangles.append(result)
        pts=' '.join(f'{u*2048:.2f},{(1-v)*2048:.2f}' for u,v in result)
        svg.append(f'<polygon points="{pts}" fill="none" stroke="#ddd" stroke-width=".65"/>')
    svg.append(f'<rect x="{x0*2048}" y="{(1-y1)*2048}" width="{(x1-x0)*2048}" height="{(y1-y0)*2048}" fill="none" stroke="#ffb040" stroke-width="3"/>')
    svg.append(f'<text x="{x0*2048+6}" y="{(1-y1)*2048+22}" fill="#ffb040" font-size="20">{name}</text>')
    bpy.data.objects.remove(ob,do_unlink=True)
svg.append('</svg>')
(ART/'uniform_uv_layout.svg').write_text('\n'.join(svg))
(ART/'atlas_regions.json').write_text(json.dumps(regions,indent=2))

# Blender's labeled color grid is a diagnostic image only, with no team art.
image=bpy.data.images.new('Uniform_reference',width=2048,height=2048)
image.generated_type='COLOR_GRID'; image.filepath_raw=str(ART/'uniform_reference.png')
image.file_format='PNG'; image.save()
doc.setdefault('images',[]).append({'name':'Uniform_reference','mimeType':'image/png',
                                  'bufferView':blob((ART/'uniform_reference.png').read_bytes())})
doc.setdefault('samplers',[]).append({'magFilter':9729,'minFilter':9987,'wrapS':33071,'wrapT':33071})
doc.setdefault('textures',[]).append({'sampler':len(doc['samplers'])-1,'source':len(doc['images'])-1})
uniform=len(doc['materials'])
doc['materials'].append({'name':'Uniform','pbrMetallicRoughness':{
    'baseColorFactor':[1,1,1,1],'baseColorTexture':{'index':len(doc['textures'])-1,'texCoord':0},
    'metallicFactor':0,'roughnessFactor':.75},'doubleSided':True})
for item in changed:
    p=doc['meshes'][item['mi']]['primitives'][item['pi']]
    old=copy.deepcopy(p)
    # Deduplicate identical corners including UVs; split only at atlas seams.
    source=item['ix']; uv=item['uv']; keys={}; take=[]; indices=[]
    for i,(vi,co) in enumerate(zip(source,uv)):
        key=(int(vi),co.tobytes())
        if key not in keys: keys[key]=len(take); take.append(i)
        indices.append(keys[key])
    for semantic,ai in old['attributes'].items():
        arr=values(ai)
        p['attributes'][semantic]=accessor(arr[source[take]],doc['accessors'][ai])
        assert np.array_equal(values(p['attributes'][semantic])[indices],arr[source])
    p['attributes']['TEXCOORD_0']=accessor(uv[take],kind='VEC2')
    # glTF UV origin is at the top of a PNG.
    a=p['attributes']['TEXCOORD_0']; corrected=values(a); corrected[:,1]=1-corrected[:,1]
    p['attributes']['TEXCOORD_0']=accessor(corrected,kind='VEC2')
    p['indices']=accessor(np.array(indices,dtype='<u4').reshape(-1,1),
                         {'componentType':5125,'type':'SCALAR'})
    doc['bufferViews'][doc['accessors'][p['indices']]['bufferView']]['target']=34963
    p['material']=uniform

# Pixel-center interior overlap test: excludes shared edges, detects UV folds.
coverage=np.zeros((2048,2048),dtype=np.uint16)
for tri in uv_triangles:
    tri=np.asarray(tri)*2048
    low=np.maximum(np.floor(tri.min(axis=0)).astype(int),0)
    high=np.minimum(np.ceil(tri.max(axis=0)).astype(int),2047)
    xx,yy=np.meshgrid(np.arange(low[0],high[0]+1)+.5,np.arange(low[1],high[1]+1)+.5)
    a,b,c=tri; den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
    if abs(den)<1e-9: continue
    u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
    v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
    coverage[low[1]:high[1]+1,low[0]:high[0]+1]+=((u>1e-5)&(v>1e-5)&(u+v<1-1e-5))
overlap=int(np.count_nonzero(coverage>1))
assert overlap==0, f'Overlapping UV pixels: {overlap}'
for key in ('nodes','skins','animations'):
    assert doc[key]==original[key], key
assert bytes(binary[:len(original_binary)])==original_binary
doc['buffers'][0]['byteLength']=len(binary)
payload=json.dumps(doc,separators=(',',':')).encode(); payload+=b' '*((-len(payload))%4)
binary.extend(b'\0'*((-len(binary))%4))
data=struct.pack('<III',0x46546c67,2,28+len(payload)+len(binary))
data+=struct.pack('<II',len(payload),0x4e4f534a)+payload
data+=struct.pack('<II',len(binary),0x004e4942)+binary
DEST.write_bytes(data)
report={'uniform_material':'Uniform','atlas_size':2048,'uniform_objects':sorted(set(x['name'] for x in changed)),
        'overlapping_interior_pixels':overlap,'geometry_normals_weights_joints':'Exact per-triangle-corner equality',
        'nodes_skins_animations':'Exact JSON and original binary accessor preservation',
        'animations':[{'name':a['name'],'channels':len(a['channels'])} for a in doc['animations']],
        'source_sha256':hashlib.sha256((ART/'football_player_before_atlas.glb').read_bytes()).hexdigest(),
        'output_sha256':hashlib.sha256(data).hexdigest()}
(ART/'validation.json').write_text(json.dumps(report,indent=2))

# Reload the delivered GLB for a usable editable source and reference renders.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(DEST))
assert bpy.data.materials.get('Uniform')
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'football_player_uniform.blend'))
for o in bpy.data.objects:
    if o.type=='ARMATURE': o.data.pose_position='REST'
scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'
scene.eevee.use_gtao=True; scene.eevee.gtao_distance=3
scene.world=bpy.data.worlds.new('UniformPreviewWorld'); scene.world.color=(.3,.3,.3)
scene.view_settings.view_transform='Standard'; scene.view_settings.look='Medium High Contrast'
scene.render.resolution_x=720; scene.render.resolution_y=900; scene.render.resolution_percentage=100
for loc,power,size in [((3,4,5),700,5),((-3,1,3),450,4),((0,-4,4),550,4)]:
    bpy.ops.object.light_add(type='AREA',location=loc)
    light=bpy.context.object; light.data.energy=power; light.data.shape='DISK'; light.data.size=size
    light.rotation_euler=(Vector((0,0,1))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(); cam=bpy.context.object; scene.camera=cam; cam.data.type='ORTHO'; cam.data.ortho_scale=2.12
for label,loc in [('front',(0,4,1.05)),('back',(0,-4,1.05)),('side',(4,0,1.05))]:
    cam.location=loc; cam.rotation_euler=(Vector((0,0,.98))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(ART/f'checker_{label}.png'); bpy.ops.render.render(write_still=True)
print(json.dumps(report,indent=2))
