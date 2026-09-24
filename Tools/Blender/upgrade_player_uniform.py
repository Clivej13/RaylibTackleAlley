"""Refactor saved approved player assets via Blender MCP; never regenerate actions.

Run after upstream body/animation generators, then export_football_player.py.
Original geometry and weights are retained. Feet remain editable reference meshes,
excluded from export by uniform_reference. +Y up, -Z forward.
"""
from pathlib import Path
import bpy, json, math, hashlib, struct, sys
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models'
PRE = OUT / 'UniformPreviews'
NEW = ('Pants', 'Socks', 'Cleat_L', 'Cleat_R')

def digest(value):
    return hashlib.sha256(repr(value).encode()).hexdigest()

def signatures():
    r = bpy.data.objects['PlayerRig']
    return {
        'skeleton': digest([(b.name, b.parent.name if b.parent else None, tuple(map(tuple,b.matrix_local)), b.length, b.use_deform) for b in r.data.bones]),
        'actions': {a.name: digest([(f.data_path,f.array_index,f.extrapolation,[(tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation,k.handle_left_type,k.handle_right_type) for k in f.keyframe_points]) for f in a.fcurves]) for a in bpy.data.actions},
        'meshes': {o.name: digest(([tuple(v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons], [[(g.group,g.weight) for g in v.groups] for v in o.data.vertices],tuple(map(tuple,o.matrix_world)))) for o in bpy.data.objects if o.type=='MESH' and o.name not in NEW+('Undershirt','Jersey')},
        'equipment_materials': {n:[m.name for m in bpy.data.objects[n].data.materials] for n in ('Helmet','Facemask','Visor','ChinStrap','ShoulderPads')},
    }

def material(name, color):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color=(*color,1); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1); p.inputs['Roughness'].default_value=.82
    return m

def mesh(name, verts, faces, weights, mats, indices=None):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    o=bpy.data.objects.new(name,data); bpy.context.scene.collection.objects.link(o)
    for m in mats:data.materials.append(m)
    if indices:
        for p,i in zip(data.polygons,indices):p.material_index=i
    for i,ws in enumerate(weights):
        for n,w in ws.items():
            if w>1e-7:(o.vertex_groups.get(n) or o.vertex_groups.new(name=n)).add([i],w,'REPLACE')
    o.parent=bpy.data.objects['PlayerRig']; mod=o.modifiers.new('Existing player skin','ARMATURE'); mod.object=o.parent
    return o

def shell(name, sources, mat):
    verts=[]; faces=[]; weights=[]
    for source in sources:
        o=bpy.data.objects[source]; offset=len(verts)
        o.data.materials.clear(); o.data.materials.append(mat)
        for v in o.data.vertices:
            p=v.co.copy(); amount=.003
            if 'Thigh' in source: amount=.008 + .006*max(0,-v.normal.z)
            if 'Knee' in source: amount=.004 + .007*max(0,-v.normal.z)
            if name=='Socks':amount=.0015
            p+=v.normal*amount
            verts.append(tuple(p)); weights.append({o.vertex_groups[g.group].name:g.weight for g in v.groups})
        faces.extend(tuple(offset+i for i in p.vertices) for p in o.data.polygons)
    return mesh(name,verts,faces,weights,[mat])

def cleat(side, mats):
    sign=1 if side=='L' else -1; cx=sign*.176
    verts=[]; faces=[]; indices=[]
    # Eight-sided plan outline: broad forefoot, clipped toe, narrower heel.
    outline=[(-.042,-.225),(.042,-.225),(.067,-.19),(.065,-.07),(.05,.095),(-.05,.095),(-.065,-.07),(-.067,-.19)]
    def rings(points, levels, material_index):
        start=len(verts); n=len(points)
        for level,scale in levels:
            for x,z in points:
                y=level(z) if callable(level) else level
                verts.append((cx+x*scale,y,z))
        faces.append(tuple(start+i for i in reversed(range(n)))); indices.append(material_index)
        for j in range(len(levels)-1):
            for i in range(n):faces.append((start+j*n+i,start+j*n+(i+1)%n,start+(j+1)*n+(i+1)%n,start+(j+1)*n+i)); indices.append(material_index)
        faces.append(tuple(start+(len(levels)-1)*n+i for i in range(n)));indices.append(material_index)
    rings(outline,[(.012,1),(.029,1)],1)
    rings(outline,[(.028,1), (lambda z:.065 if z<-.1 else .112,.94), (lambda z:.077 if z<-.1 else .132,.65)],0)
    # Six short broad studs, within the original foot's ground-contact envelope.
    for x,z in [(-.038,-.182),(.038,-.182),(-.04,-.085),(.04,-.085),(-.029,.063),(.029,.063)]:
        points=[(x+math.cos(i*math.tau/6)*.012,z+math.sin(i*math.tau/6)*.012) for i in range(6)]
        rings(points,[(0,1),(.013,1)],1)
    o=mesh('Cleat_'+side,verts,[tuple(reversed(f)) for f in faces],[{'Foot.'+side:1} for _ in verts],mats,indices)
    foot=bpy.data.objects[('Left' if side=='L' else 'Right')+'Foot']
    foot.hide_render=True; foot.hide_set(True); foot['uniform_reference']=True
    return o

def pants_mesh(mat):
    verts=[]; faces=[]; weights=[]
    def ramp(x,a,b):
        t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
    def tube(rows,side=None):
        start=len(verts);count=12
        for y,cx,rx,rz in rows:
            for i in range(count):
                t=math.tau*i/count;x=cx+rx*math.cos(t);z=.01+rz*math.sin(t)
                verts.append((x,y,z))
                if side:
                    if y>.79:
                        h=ramp(y,.79,.965);ws={'UpperLeg.'+side:1-h,'Hips':h}
                    else:
                        h=ramp(y,.47,.59);ws={'LowerLeg.'+side:1-h,'UpperLeg.'+side:h}
                else:
                    h=(1-ramp(y,.82,.94))*ramp(abs(x),.025,.10)*.75
                    ws={'Hips':1-h,'UpperLeg.'+('L' if x>=0 else 'R'):h}
                weights.append(ws)
        for j in range(len(rows)-1):
            for i in range(count):faces.append((start+j*count+i,start+j*count+(i+1)%count,start+(j+1)*count+(i+1)%count,start+(j+1)*count+i))
    tube([(1.065,0,.187,.128),(.995,0,.188,.135),(.91,0,.184,.146),(.85,0,.16,.125),(.80,0,.082,.078)])
    for side,sign in [('L',1),('R',-1)]:
        tube([(y,sign*x,rx,rz) for y,x,rx,rz in [(.93,.104,.089,.108),(.85,.117,.104,.12),(.74,.129,.101,.115),(.63,.14,.087,.1),(.55,.151,.065,.083),(.48,.155,.058,.067)]],side)
    for n in ['Pelvis','Groin','Glutes','LeftThigh','RightThigh','LeftKnee','RightKnee']:
        o=bpy.data.objects[n];o.hide_render=True;o.hide_set(True);o['uniform_reference']=True
    return mesh('Pants',verts,faces,weights,[mat])

def install():
    for n in NEW:
        if n in bpy.data.objects:bpy.data.objects.remove(bpy.data.objects[n],do_unlink=True)
    pants=material('Pants',(.32,.35,.38)); socks=material('Socks',(.78,.80,.82))
    shoe=material('Cleats',(.035,.042,.052)); sole=material('Cleats_Sole',(.15,.17,.19))
    pants_mesh(pants)
    shell('Socks',['LeftCalf','RightCalf','LeftAnkle','RightAnkle'],socks)
    cleat('L',[shoe,sole]); cleat('R',[shoe,sole])

def export(path):
    s=bpy.context.scene; r=bpy.data.objects['PlayerRig']; action=r.animation_data.action
    # Source .blend was saved with all approved actions intact. Isolate only this
    # in-memory export, matching the existing canonical exporter behavior.
    for other in list(bpy.data.actions):
        if other!=action:bpy.data.actions.remove(other)
    bpy.ops.object.select_all(action='DESELECT')
    for o in s.objects:
        if o.type in {'MESH','ARMATURE','EMPTY'} and not o.get('uniform_reference') and o.name!='Football_Preview':o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_yup=False,export_animations=True,export_nla_strips=False,export_frame_range=True,export_force_sampling=True,export_skins=True,export_current_frame=False)
    raw=path.read_bytes(); size=struct.unpack_from('<I',raw,12)[0]; doc=json.loads(raw[20:20+size])
    assert len(doc['animations'])==1
    doc['animations'][0]['name']=action.name
    nodes={n['name']:n for n in doc['nodes']}
    assert all('skin' in nodes[n] for n in NEW)
    assert {'Pants','Socks','Cleats','Cleats_Sole'}<={m['name'] for m in doc['materials']}
    assert len(doc['skins'][0]['joints'])==24
    payload=json.dumps(doc,separators=(',',':')).encode();payload+=b' '*(-len(payload)%4); tail=raw[20+size:]
    path.write_bytes(struct.pack('<4sII',b'glTF',2,20+len(payload)+len(tail))+struct.pack('<II',len(payload),0x4e4f534a)+payload+tail)

def validate():
    r=bpy.data.objects['PlayerRig']; s=bpy.context.scene
    report={}; saved=r.animation_data.action; frame=s.frame_current
    for n in NEW:
        o=bpy.data.objects[n]
        for v in o.data.vertices:
            ws={o.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>1e-7}
            assert abs(sum(ws.values())-1)<1e-5
            assert not (any(n.endswith('.L') for n in ws) and any(n.endswith('.R') for n in ws))
            if n.startswith('Cleat'):assert ws=={'Foot.'+n[-1]:1.0}
    for name in ('Pose_Neutral','Jog','Run','Sprint'):
        if name not in bpy.data.actions:continue
        a=bpy.data.actions[name];r.animation_data.action=a; peak=0
        for f in range(int(a.frame_range[0]),int(a.frame_range[1])+1):
            s.frame_set(f);dg=bpy.context.evaluated_depsgraph_get()
            for side in ('L','R'):
                o=bpy.data.objects['Cleat_'+side]; ev=o.evaluated_get(dg)
                transform=r.pose.bones['Foot.'+side].matrix@r.data.bones['Foot.'+side].matrix_local.inverted()
                peak=max(peak,max((ev.data.vertices[v.index].co-transform@v.co).length for v in o.data.vertices))
            for n in NEW:
                assert all(math.isfinite(c) for v in bpy.data.objects[n].evaluated_get(dg).data.vertices for c in v.co)
        assert peak<1e-5
        report[name]={'frames_checked':int(a.frame_range[1]-a.frame_range[0])+1,'max_cleat_attachment_error':peak}
    r.animation_data.action=saved;s.frame_set(frame)
    return report

def render_views(tag, rest=False):
    s=bpy.context.scene;r=bpy.data.objects['PlayerRig'];old=r.data.pose_position
    if rest:r.data.pose_position='REST'
    s.render.image_settings.file_format='PNG';s.render.resolution_x=720;s.render.resolution_y=800;s.render.resolution_percentage=100;s.eevee.taa_render_samples=32
    for view in (('Front','Side','Back') if rest else ('FrontThreeQuarter','Side')):
        s.camera=bpy.data.objects[view];s.render.filepath=str(PRE/(tag+'_'+view+'.png'));bpy.ops.render.render(write_still=True)
    r.data.pose_position=old

def main():
    PRE.mkdir(exist_ok=True);report={}
    for stem in ('rigged','jog','run','sprint'):
        path=OUT/('lowpoly_human_'+stem+'.blend');bpy.ops.wm.open_mainfile(filepath=str(path));bpy.context.preferences.filepaths.save_version=0
        before=signatures(); install();after=signatures();assert before==after,'Original geometry, rig or animation changed'
        checks=validate(); tris=sum(sum(len(p.vertices)-2 for p in bpy.data.objects[n].data.polygons) for n in NEW)
        report[stem]={'preservation':before,'animation_checks':checks,'additional_triangles':tris,'original_feet_excluded_from_export':True}
        bpy.ops.wm.save_as_mainfile(filepath=str(path))
        if stem=='rigged':render_views('Standing',True)
        else:
            export(OUT/('lowpoly_human_'+stem+'_validation.glb'))
            bpy.context.scene.frame_set({'jog':5,'run':5,'sprint':4}[stem]);render_views(stem.title())
        print('UNIFORM_COMPLETE',stem,tris,flush=True)
    (PRE/'validation.json').write_text(json.dumps(report,indent=2))

if __name__=='__main__':main()
