"""Refactor existing stadium edges/access. Run via Blender MCP. One unit = yard.
Input snapshot preserves the preceding seating-bowl refactor. Field is read-only.
"""
from pathlib import Path
import bpy, math, json, hashlib
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Models'; PRE=ROOT/'Assets/Previews'

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    protected={str(p.relative_to(ROOT)):sha(p) for p in (ROOT/'Assets').rglob('*') if p.is_file() and 'stadium' not in p.name.lower() and p.name!='field_equipment.glb'}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.import_scene.gltf(filepath=str(ROOT/'Tools/Blender/Source/stadium_before_access.glb'))
    scene=bpy.context.scene
    stadium_root=bpy.data.objects['STADIUM']
    stadium=bpy.data.collections.new('STADIUM');scene.collection.children.link(stadium)
    def move(o,c):
        for old in list(o.users_collection):old.objects.unlink(o)
        c.objects.link(o)
    move(stadium_root,stadium)
    groups={};nodes={}
    for o in list(scene.objects):
        if o.type=='EMPTY' and o.parent==stadium_root:
            c=bpy.data.collections.new(o.name);stadium.children.link(c);groups[o.name]=c;nodes[o.name]=o;move(o,c)
    for o in list(scene.objects):
        if o.type=='MESH':move(o,groups[o.parent.name])
    removed=[]
    for o in list(stadium.all_objects):
        if o.type=='MESH' and (o.name.endswith(('_Front_Wall','_Front_Rail','_Entry_Stairs')) or o.name=='Runoff_Apron'):
            removed.append(o.name);bpy.data.objects.remove(o,do_unlink=True)
    retained=[o.name for o in stadium.all_objects if o.type=='MESH']
    equipment=bpy.data.collections.new('FIELD_EQUIPMENT');scene.collection.children.link(equipment)
    eqroot=bpy.data.objects.new('FIELD_EQUIPMENT',None);equipment.objects.link(eqroot)
    def group(name,equip=False):
        if name in groups:return
        c=bpy.data.collections.new(name);(equipment if equip else stadium).children.link(c);groups[name]=c
        node=bpy.data.objects.new(name,None);c.objects.link(node);node.parent=eqroot if equip else stadium_root;nodes[name]=node
    for name in ['Field_Boundary','Player_Tunnels','Sideline_Zones']:group(name)
    for name in ['Benches','Sideline_Props','Endzone_Equipment']:group(name,True)
    def mat(name,color):
        m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
        b=m.node_tree.nodes['Principled BSDF'];b.inputs['Base Color'].default_value=(*color,1);b.inputs['Roughness'].default_value=.86
        return m
    concrete=mat('Edge Warm Concrete',(.43,.43,.39));paving=mat('Apron Graphite',(.14,.18,.19))
    zone=mat('Team Zone Slate',(.22,.29,.32));padding=mat('Boundary Blue Padding',(.035,.11,.18))
    metal=bpy.data.materials['Bowl Rail Metal'];dark=bpy.data.materials['Tunnel Shadow']
    stripe=mat('Access Safety Ochre',(.68,.46,.12));benchmat=mat('Bench Pale Grey',(.58,.63,.62));gold=mat('Goalpost Yellow',(.96,.67,.06))
    batches={}
    def solid(g,name,verts,faces,m):
        key=(g,name,m.name);vs,fs=batches.setdefault(key,([],[]));n=len(vs);vs.extend(verts);fs.extend(tuple(n+i for i in f) for f in faces)
    cube_faces=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    def box(g,name,center,size,m):
        cx,cy,cz=center;dx,dy,dz=[s/2 for s in size]
        vs=[(cx+x*dx,cy+y*dy,cz+z*dz) for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
        solid(g,name,vs,cube_faces,m)
    def beam(g,name,a,b,r,m,sides=8):
        a,b=Vector(a),Vector(b);d=(b-a).normalized();u=d.cross(Vector((0,0,1)))
        if u.length<.01:u=d.cross(Vector((0,1,0)))
        u.normalize();v=d.cross(u);vs=[]
        for p in (a,b):
            for i in range(sides):vs.append(tuple(p+r*(math.cos(i*2*math.pi/sides)*u+math.sin(i*2*math.pi/sides)*v)))
        fs=[tuple(reversed(range(sides))),tuple(range(sides,2*sides))]
        fs += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
        solid(g,name,vs,fs,m)
    def rail(g,name,a,b,z,m=metal):
        ax,ay=a;bx,by=b
        for h in [.5,1.05]:beam(g,name,(ax,ay,z+h),(bx,by,z+h),.045,m,6)
        count=max(1,math.ceil(math.hypot(bx-ax,by-ay)/3))
        for i in range(count+1):
            t=i/count;beam(g,name,(ax+(bx-ax)*t,ay+(by-ay)*t,z),(ax+(bx-ax)*t,ay+(by-ay)*t,z+1.09),.045,m,6)
    # Apron touches only the OUTSIDE of the unchanged 53.333 x 120 rectangle.
    for side in [-1,1]:
        box('Field_Boundary','Sideline_Runoff',(side*(26.6665+3.16675),0,-.12),(6.3335,120,.24),paving)
        box('Field_Boundary','Endzone_Runoff',(0,side*63.5,-.12),(66,7,.24),paving)
    # Continuous field-facing retaining edge: no spectator stairs or gates onto apron.
    for side,label in [(-1,'Home_West'),(1,'Away_East')]:
        for i,(a,b) in enumerate([(-58,58)]):
            box('Field_Boundary',label+'_Retaining_Wall',(side*32.72,(a+b)/2,1),( .56,b-a,2),concrete)
            box('Field_Boundary',label+'_Wall_Padding',(side*32.40,(a+b)/2,.80),(.08,b-a-.08,1.50),padding)
            rail('Railings',label+'_Front_Guard',(side*32.72,a+.08),(side*32.72,b-.08),2)
        # Flat inset team bays, staff strip and uncluttered benches.
        box('Sideline_Zones',label+'_Team_Pad',(side*30.38,0,.035),(2.76,40,.07),zone)
        for y in [-20,20]:box('Sideline_Zones',label+'_Zone_Edge',(side*30.38,y,.077),(2.76,.09,.012),stripe)
        box('Sideline_Zones',label+'_Staff_Lane_Edge',(side*31.95,0,.012),(.09,42,.024),stripe)
        for y in [-11,0,11]:
            name=label+f'_Bench_{y:+g}'
            box('Benches',name+'_Seat',(side*30.0,y,.60),(.65,6,.16),benchmat)
            box('Benches',name+'_Back',(side*30.34,y,.98),(.10,6,.60),benchmat)
            for yy in [y-2.4,y+2.4]:box('Benches',name+'_Legs',(side*30,yy,.295),(.46,.16,.45),metal)
        for y in [-25,25]:
            box('Sideline_Props',label+f'_Equipment_Box_{y:+g}',(side*30.15,y,.5),(1.1,1.5,1),padding)
        # End barriers define controlled team area but preserve rear staff passage.
        for y in [-22,22]:rail('Railings',label+'_Team_Barrier',(side*29.2,y),(side*31.2,y),0)
    # Existing end stands are now behind a continuous padded retaining face.
    for sign in [-1,1]:
        box('Field_Boundary',f'End_{sign:+}_Retaining_Wall',(0,sign*66.72,1),(54,.56,2),concrete)
        box('Field_Boundary',f'End_{sign:+}_Padding',(0,sign*66.40,.8),(53.9,.08,1.5),padding)
        rail('Railings',f'End_{sign:+}_Front_Guard',(-26.9,sign*66.72),(26.9,sign*66.72),2)
    # Two major field-access portals occupy opposite existing open corners.
    # Floor is the original corner plaza, Z=.7. Broad 1:8 ramp meets apron at Z=0.
    for sign,label in [(1,'NorthEast_Player_Entrance'),(-1,'SouthWest_Player_Entrance')]:
        cx=sign*33.5
        for x in [cx-3,cx+3]:box('Player_Tunnels',label+'_Side_Walls',(x,sign*68.5,2.65),(.6,6,3.9),concrete)
        box('Player_Tunnels',label+'_Roof',(cx,sign*68.5,4.8),(6.6,6,.4),concrete)
        box('Player_Tunnels',label+'_Shadow',(cx,sign*71.42,2.6),(5.4,.12,3.8),dark)
        box('Player_Tunnels',label+'_Header',(cx,sign*65.43,4.25),(5.4,.14,.65),padding)
        # Side return attaches the portal architecture to the existing stand end.
        box('Player_Tunnels',label+'_Return',(sign*28.55,sign*71,2.05),(3.3,.6,2.7),concrete)
        lo,hi=sorted([sign*59.9,sign*65.5]);zlo,zhi=(0,.7) if sign>0 else (.7,0)
        vs=[(cx-2.7,lo,-.12),(cx+2.7,lo,-.12),(cx+2.7,hi,-.12),(cx-2.7,hi,-.12),(cx-2.7,lo,zlo),(cx+2.7,lo,zlo),(cx+2.7,hi,zhi),(cx-2.7,hi,zhi)]
        solid('Player_Tunnels',label+'_Ramp',vs,cube_faces,zone)
        for x in [cx-2.8,cx+2.8]:
            beam('Railings',label+'_Ramp_Rail',(x,lo,zlo+1.05),(x,hi,zhi+1.05),.055,metal)
            for t in [0,.5,1]:
                yy=lo+(hi-lo)*t;zz=zlo+(zhi-zlo)*t
                beam('Railings',label+'_Ramp_Rail',(x,yy,zz),(x,yy,zz+1.05),.055,metal)
        # Lowered approach connects ramp toe back to sideline apron.
        box('Field_Boundary',label+'_Approach',(cx,sign*58.45,-.12),(5.4,2.9,.24),paving)
    # Two end-line goals: 10-foot crossbar top, 18 ft 6 in clear upright gap.
    for sign,label in [(1,'GoalPost_Home'),(-1,'GoalPost_Away')]:
        end=sign*60;support=sign*62;radius=.065;barz=10/3-radius;ux=18.5/6+radius
        beam('Endzone_Equipment',label,(0,support,0),(0,support,barz),.12,gold)
        beam('Endzone_Equipment',label,(0,support,barz),(0,end,barz),.12,gold)
        beam('Endzone_Equipment',label,(-ux,end,barz),(ux,end,barz),radius,gold)
        for x in [-ux,ux]:beam('Endzone_Equipment',label,(x,end,barz),(x,end,10/3+35/3),radius,gold)
        box('Endzone_Equipment',label+'_Base_Pad',(0,support,.8),(.55,.55,1.6),padding)
    # The original raised corner plaza would bury the ramps: cut only their footprint.
    # Boolean applied to the two local plazas; all seating and other foundations retained.
    for sign in [-1,1]:
        o=bpy.data.objects.get(f'Corner_{sign:+}_{sign:+}_Plaza')
        assert o is not None
        bpy.ops.mesh.primitive_cube_add(size=1,location=(sign*33.5,sign*61.3,.4))
        cutter=bpy.context.object;cutter.dimensions=(5.8,8.4,2)
        bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        mod=o.modifiers.new('Player_Ramp_Clearance','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
        bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(cutter,do_unlink=True)
    for (g,name,m),(verts,faces) in batches.items():
        mesh=bpy.data.meshes.new(name+'_Mesh');mesh.from_pydata(verts,[],faces);mesh.update()
        o=bpy.data.objects.new(name,mesh);groups[g].objects.link(o);o.parent=nodes[g];mesh.materials.append(bpy.data.materials[m])
    # Preserve yard-valued coordinates. Append the authored field mesh without glTF unit conversion.
    field=bpy.data.collections.new('FIELD');scene.collection.children.link(field)
    with bpy.data.libraries.load(str(OUT/'football_field.blend'),link=False) as (src,dst):dst.objects=['FootballField']
    for o in dst.objects:
        if o:field.objects.link(o)
    bpy.context.view_layer.update();f=field.objects['FootballField']
    assert abs(f.dimensions.x-53.333)<1e-5 and abs(f.dimensions.y-120)<1e-5
    assert f.location.length==0 and f.scale==Vector((1,1,1))
    scene.unit_settings.system='IMPERIAL';scene.unit_settings.scale_length=.9144
    stadium_root['units_per_yard']=1.;eqroot['units_per_yard']=1.
    scene['field_reference_note']='Authored field appended without import-unit scaling; source assets unchanged.'
    # Game assembly retains distinct STADIUM and FIELD_EQUIPMENT roots; no field exported.
    def export(path,collections):
        bpy.ops.object.select_all(action='DESELECT')
        for c in collections:
            for o in c.all_objects:o.select_set(True)
        bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_extras=True)
    export(OUT/'stadium.glb',[stadium,equipment])
    export(OUT/'field_equipment.glb',[equipment])
    # Preview-only lighting and cameras.
    scene.render.engine='BLENDER_EEVEE';scene.eevee.use_gtao=True;scene.eevee.gtao_distance=3
    scene.world=bpy.data.worlds.new('Stadium Preview World');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.16,.19,.23,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
    scene.view_settings.view_transform='Standard';scene.view_settings.look='Medium High Contrast'
    bpy.ops.object.light_add(type='SUN');bpy.context.object.name='Preview_Sun';bpy.context.object.data.energy=2;bpy.context.object.rotation_euler=(.5,-.4,-.4)
    bpy.ops.object.camera_add();cam=bpy.context.object;cam.name='Preview_Camera';scene.camera=cam
    scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    def render(name,pos,target,ortho=None):
        cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO' if ortho else 'PERSP'
        if ortho:cam.data.ortho_scale=ortho
        else:cam.data.lens=24
        scene.render.filepath=str(PRE/name);bpy.ops.render.render(write_still=True)
    render('stadium_preview.png',(135,-170,155),(0,0,1),230)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'stadium.blend'))
    render('stadium_field_level.png',(22,32,2.4),(32,63,2))
    render('stadium_sideline_preview.png',(12,-32,7),(31,5,1.5))
    scene.render.resolution_x=1600;scene.render.resolution_y=900
    cam.data.sensor_fit='VERTICAL';cam.data.sensor_height=24;cam.data.lens=24/(2*math.tan(math.radians(55)/2))
    cam.location=(0,-30.75,5.5);cam.rotation_euler=(Vector((0,-18.5,1.1))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(PRE/'stadium_gameplay_preview.png');bpy.ops.render.render(write_still=True)
    bpy.context.view_layer.update()
    def stats(c):
        obs=[o for o in c.all_objects if o.type=='MESH']
        for o in obs:
            o.data.calc_loop_triangles();assert all(p.area>1e-10 for p in o.data.polygons),o.name
            assert o.scale==Vector((1,1,1)),o.name
        pts=[o.matrix_world@Vector(v) for o in obs for v in o.bound_box]
        return {'meshes':len(obs),'triangles':sum(len(o.data.loop_triangles) for o in obs),'bounds':[[min(p[i] for p in pts),max(p[i] for p in pts)] for i in range(3)]}
    assert all(sha(ROOT/p)==h for p,h in protected.items())
    report={'retained_objects':retained,'removed_objects':removed,'locally_cut_plazas':['Corner_+1_+1_Plaza','Corner_-1_-1_Plaza'],'stadium':stats(stadium),'equipment':stats(equipment),'field_dimensions':list(f.dimensions),'field_location':list(f.location),'protected_asset_hashes':protected,'all_protected_assets_unchanged':True,'player_tunnels':[[33.5,65.5],[-33.5,-65.5]],'sideline_stairs':[],'materials_added':[m.name for m in [concrete,paving,zone,padding,stripe,benchmat,gold]]}
    (ROOT/'Tools/Blender/stadium_access_validation.json').write_text(json.dumps(report,indent=2))
    print('VALIDATED',json.dumps({k:v for k,v in report.items() if k not in ['protected_asset_hashes','retained_objects']}))
if __name__=='__main__':main()
