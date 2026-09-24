"""Refactor the archived existing stadium through Blender MCP. Units are yards.
Preserves original concourses, scoreboard and light towers. Field is preview-only.
"""
from pathlib import Path
import bpy, math, json, hashlib
from mathutils import Vector, Matrix
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Models'; PRE=ROOT/'Assets/Previews'

def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    protected={str(p.relative_to(ROOT)):digest(p) for p in (ROOT/'Assets').rglob('*') if p.is_file() and 'stadium' not in p.name.lower()}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.import_scene.gltf(filepath=str(ROOT/'Tools/Blender/Source/stadium_original.glb'))
    scene=bpy.context.scene
    root=bpy.data.collections.new('STADIUM');scene.collection.children.link(root)
    groups={}
    for name in ['Stadium_Base','Lower_Bowl','Upper_Bowl','Aisles','Tunnels','Railings','Structural','Stadium_Details']:
        c=bpy.data.collections.new(name);root.children.link(c);groups[name]=c
    def move(o,c):
        for old in list(o.users_collection):old.objects.unlink(o)
        c.objects.link(o)
    retained=[];removed=[]
    for o in list(scene.objects):
        if o.type!='MESH':continue
        if 'Concourse' in o.name or o.name.startswith(('Scoreboard','Light Tower')):
            retained.append(o.name);move(o,groups['Stadium_Base' if 'Concourse' in o.name else 'Stadium_Details'])
            o.data.transform(o.matrix_world);o.matrix_world=Matrix.Identity(4)
        else:removed.append(o.name);bpy.data.objects.remove(o,do_unlink=True)
    def material(name,color):
        m=bpy.data.materials.get(name) or bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
        b=m.node_tree.nodes.get('Principled BSDF');b.inputs['Base Color'].default_value=(*color,1);b.inputs['Roughness'].default_value=.85
        return m
    concrete=material('Bowl Concrete',(.34,.37,.39));seat=material('Bowl Muted Blue',(.075,.20,.29))
    trim=material('Bowl Structural Trim',(.16,.20,.23));metal=material('Bowl Rail Metal',(.065,.085,.10));dark=material('Tunnel Shadow',(.009,.013,.018))
    # Batch disconnected modular solids by section/category, reducing draw calls.
    batches={}
    def box(group,name,center,size,mat,mapper=None):
        key=(group,name,mat.name)
        verts,faces=batches.setdefault(key,([],[]));n=len(verts)
        cx,cy,cz=center;dx,dy,dz=[v/2 for v in size]
        points=[(cx+x*dx,cy+y*dy,cz+z*dz) for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
        verts.extend([mapper(*p) for p in points] if mapper else points)
        faces.extend([tuple(n+i for i in f) for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]])
    # Perimeter paving fixes external bounds at configured 110 x 180 without scaling.
    for sign in [-1,1]:
        box('Stadium_Base','Perimeter_Paving',(sign*53.5,0,.15),(3,180,.3),concrete)
        box('Stadium_Base','Perimeter_Paving',(0,sign*87,.15),(104,6,.3),concrete)
    # Narrow architectural apron; exact field rectangle remains entirely clear.
    for sign in [-1,1]:
        box('Stadium_Base','Runoff_Apron',(sign*(26.6665+3.16675),0,-.12),(6.3335,120,.24),trim)
        box('Stadium_Base','Runoff_Apron',(0,sign*63.5,-.12),(66,7,.24),trim)
    # Original scoreboard columns began above ground: add visible plinths beneath.
    for x in [-8,8]:box('Structural','Scoreboard_Footings',(x,84,1.5),(1.6,1.6,3),concrete)
    aisles=[];tunnels=[]
    def stand(label,mapper,front,half,rows,centers,upperrows=0):
        # Local u runs along the stand; v moves away from the field.
        gaps=[(c,3.2 if c==0 else 2.0) for c in centers]
        intervals=[];start=-half
        for c,w in gaps:intervals.append((start,c-w/2));start=c+w/2
        intervals.append((start,half))
        for j,(a,b) in enumerate(intervals):
            for r in range(rows):
                top=2+(r+1)*.5
                box('Lower_Bowl',f'{label}_Section_{j+1:02}',((a+b)/2,front+(r+.5)*.9,top/2),(b-a,.9,top),concrete,mapper)
                box('Lower_Bowl',f'{label}_Section_{j+1:02}_Seating',((a+b)/2,front+(r+.5)*.9,top+.09),(b-a-.12,.72,.18),seat,mapper)
            box('Structural',f'{label}_Front_Wall',((a+b)/2,front-.22,1),(b-a,.44,2),trim,mapper)
            box('Railings',f'{label}_Front_Rail',((a+b)/2,front-.22,2.9),(b-a,.09,.09),metal,mapper)
            for u in [a+.12,b-.12]:box('Railings',f'{label}_Front_Rail',(u,front-.22,2.45),(.08,.08,.9),metal,mapper)
        back=front+rows*.9;top=2+rows*.5
        for c,w in gaps:
            aisles.append(label+f' {c:+g}')
            for r in range(rows*2):
                z=2+(r+1)*.25
                box('Aisles',label+'_Stairs',(c,front+(r+.5)*.45,z/2),(w,.45,z),concrete,mapper)
            # Stair entry down to field apron, except tunnel centre lanes.
            for r in range(8):
                z=(r+1)*.25
                box('Aisles',label+'_Entry_Stairs',(c,front-3.6+(r+.5)*.45,z/2),(w,.45,z),concrete,mapper)
        # Clear separation band, supported visibly to ground.
        box('Structural',label+'_Concourse',(0,back+1.3,top-.35),(half*2,2.6,.7),concrete,mapper)
        for u in [-half+.4,0,half-.4]:box('Structural',label+'_Concourse_Piers',(u,back+1.3,(top-.7)/2),(.8,2.5,top-.7),trim,mapper)
        box('Railings',label+'_Concourse_Parapet',(0,back+2.45,top+.5),(half*2,.3,1),concrete,mapper)
        # Access openings sit above the lower bowl with dark short recesses,
        # aligned to stair channels and connected to the rear concourse.
        chosen=[c for c in centers if (abs(c)==24 if half>40 else c==0)]
        for c in chosen:
            tunnels.append(label+f' {c:+g}')
            for u in [c-1.8,c+1.8]:box('Tunnels',label+'_Portal_Frame',(u,back+1.0,top+1.55),(.5,2.4,3.1),concrete,mapper)
            box('Tunnels',label+'_Portal_Lintel',(c,back+1.0,top+3.25),(4.1,2.4,.4),concrete,mapper)
            box('Tunnels',label+'_Portal_Recess',(c,back+2.15,top+1.5),(3.1,.10,3),dark,mapper)
        if upperrows:
            uf=back+2.8;uz=top+3.8
            for j,(a,b) in enumerate(intervals):
                for r in range(upperrows):
                    z=uz+(r+1)*.6
                    box('Upper_Bowl',f'{label}_Upper_{j+1:02}',((a+b)/2,uf+(r+.5)*.85,z-.32),(b-a,.85,.64),concrete,mapper)
                    box('Upper_Bowl',f'{label}_Upper_{j+1:02}_Seating',((a+b)/2,uf+(r+.5)*.85,z+.08),(b-a-.12,.68,.16),seat,mapper)
                # Visible end support wedges represented by stepped support ribs.
                for u in [a+.25,b-.25]:
                    for r in range(upperrows):
                        z=uz+(r+1)*.6-.64
                        box('Structural',label+'_Upper_Supports',(u,uf+(r+.5)*.85,z/2),(.5,.85,z),trim,mapper)
            for c,w in gaps:
                for r in range(upperrows*2):
                    z=uz+(r+1)*.3
                    box('Aisles',label+'_Upper_Stairs',(c,uf+(r+.5)*.425,z-.25),(w,.425,.5),concrete,mapper)
            rear=uf+upperrows*.85
            box('Structural',label+'_Rear_Facade',(0,rear+.18,(uz+upperrows*.6)/2),(half*2,.36,uz+upperrows*.6),concrete,mapper)
            box('Railings',label+'_Upper_Parapet',(0,rear+.18,uz+upperrows*.6+.55),(half*2,.36,1.1),trim,mapper)
    # Rotation mappings have positive determinant: outward normals stay correct.
    stand('Sideline_A_West',lambda u,v,z:(-v,u,z),33,58,12,[-48,-24,0,24,48],9)
    stand('Sideline_B_East',lambda u,v,z:(v,-u,z),33,58,12,[-48,-24,0,24,48],7)
    stand('Endzone_A_North',lambda u,v,z:(u,v,z),67,27,12,[-18,0,18])
    stand('Endzone_B_South',lambda u,v,z:(-u,-v,z),67,27,10,[-18,0,18])
    # Open corner plazas join the existing foundations, with deliberate capped ends.
    for sx in [-1,1]:
        for sy in [-1,1]:
            name=f'Corner_{sx:+}_{sy:+}'
            box('Stadium_Base',name+'_Plaza',(sx*39.5,sy*66,.35),(25,16,.7),concrete)
            box('Structural',name+'_Return_Wall',(sx*44,sy*61,2.2),(1,6,4.4),trim)
    for (group,name,matname),(verts,faces) in batches.items():
        mesh=bpy.data.meshes.new(name+'_Mesh');mesh.from_pydata(verts,[],faces);mesh.update()
        o=bpy.data.objects.new(name,mesh);groups[group].objects.link(o);mesh.materials.append(bpy.data.materials[matname])
    # Logical collection hierarchy plus matching exported empty nodes.
    parent=bpy.data.objects.new('STADIUM',None);root.objects.link(parent)
    for name,c in groups.items():
        node=bpy.data.objects.new(name,None);c.objects.link(node);node.parent=parent
        for o in list(c.objects):
            if o!=node:o.parent=node
    # Stand subcollections keep individual seating sections easy to edit.
    for group in ['Lower_Bowl','Upper_Bowl']:
        for label in ['Sideline_A_West','Sideline_B_East','Endzone_A_North','Endzone_B_South']:
            matching=[o for o in groups[group].objects if o.type=='MESH' and o.name.startswith(label)]
            if matching:
                child=bpy.data.collections.new(group+'_'+label);groups[group].children.link(child)
                for o in matching:move(o,child)
    scene.unit_settings.system='IMPERIAL';scene.unit_settings.scale_length=.9144
    parent['units_per_yard']=1.;parent['field_anchor_dimensions']=[53.333,120.]
    stadium=[o for o in root.all_objects if o.type=='MESH']
    for o in stadium:
        o.data.calc_loop_triangles()
        assert o.scale==Vector((1,1,1))
        assert all(p.area>0 for p in o.data.polygons)
    bpy.context.view_layer.update()
    pts=[o.matrix_world@Vector(c) for o in stadium for c in o.bound_box]
    report={'retained':retained,'removed':removed,'mesh_count':len(stadium),'triangles':sum(len(o.data.loop_triangles) for o in stadium),'aisles':aisles,'tunnels':tunnels,'bounds':[[min(p[i] for p in pts),max(p[i] for p in pts)] for i in range(3)],'materials':sorted({m.name for o in stadium for m in o.data.materials})}
    bpy.ops.object.select_all(action='DESELECT')
    for o in root.all_objects:o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(OUT/'stadium.glb'),export_format='GLB',use_selection=True,export_extras=True)
    # Read-only field reference belongs outside STADIUM and is not exported.
    before=set(scene.objects);bpy.ops.import_scene.gltf(filepath=str(OUT/'football_field.glb'))
    field=bpy.data.collections.new('FIELD');scene.collection.children.link(field)
    for o in set(scene.objects)-before:move(o,field)
    equipment=bpy.data.collections.new('FIELD_EQUIPMENT');scene.collection.children.link(equipment)
    scene.render.engine='BLENDER_EEVEE';scene.eevee.use_gtao=True;scene.eevee.gtao_distance=3
    scene.world=bpy.data.worlds.new('Stadium Preview World');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.16,.19,.23,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
    scene.view_settings.view_transform='Standard';scene.view_settings.look='Medium High Contrast'
    bpy.ops.object.light_add(type='SUN');bpy.context.object.name='Preview_Sun';bpy.context.object.data.energy=2;bpy.context.object.rotation_euler=(.5,-.4,-.4)
    bpy.ops.object.camera_add();cam=bpy.context.object;cam.name='Preview_Camera';scene.camera=cam
    scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    def render(name,pos,target,ortho=None):
        cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
        cam.data.type='ORTHO' if ortho else 'PERSP'
        if ortho:cam.data.ortho_scale=ortho
        else:cam.data.lens=24
        scene.render.filepath=str(PRE/name);bpy.ops.render.render(write_still=True)
    render('stadium_preview.png',(135,-170,155),(0,0,1),230)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'stadium.blend'))
    render('stadium_field_level.png',(0,-42,2),(25,30,8))
    scene.render.resolution_x=1600;scene.render.resolution_y=900
    cam.data.sensor_fit='VERTICAL';cam.data.sensor_height=24
    cam.data.lens=24/(2*math.tan(math.radians(55)/2))
    # render() uses 24 mm, so set the exact FOV after positioning here.
    cam.location=(0,-30.75,5.5)
    cam.rotation_euler=(Vector((0,-18.5,1.1))-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='PERSP'
    scene.render.filepath=str(PRE/'stadium_gameplay_preview.png');bpy.ops.render.render(write_still=True)
    assert all(digest(ROOT/p)==h for p,h in protected.items())
    report['protected_asset_hashes_unchanged']=True
    (ROOT/'Tools/Blender/stadium_refactor_validation.json').write_text(json.dumps(report,indent=2))
    print('VALIDATION',json.dumps(report))
if __name__=='__main__':main()
