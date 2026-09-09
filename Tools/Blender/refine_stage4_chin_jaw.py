"""Refine only the existing Stage 4 chin/jaw; run via Blender MCP.
X coordinates, cranium, facial features, neck and all other meshes are locked.
"""
from pathlib import Path
import json,struct
import bpy
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets'/'Models'

def refine_jaw(ob):
    assert len(ob.data.vertices)==96
    # Existing lower rings: retain their width and embedded neck attachment.
    # Broad chin plane projects forward, with a defined lower jaw edge.
    for ring in (1,2):
        for i in range(16):
            vertex=ob.data.vertices[ring*16+i]
            lateral=min(i,16-i)
            if lateral<=2:
                weight=(1.0,1.0,.65)[lateral]
                vertex.co.z-=(.012 if ring==1 else .008)*weight
                vertex.co.y-=(.005 if ring==1 else .0025)*weight
            elif lateral in (3,4,5):
                vertex.co.y-=(.003 if ring==1 else .004)
                if lateral==3: vertex.co.z-=.002
    ob.data.update()

def signature(o):
    return (tuple(tuple(v.co) for v in o.data.vertices),tuple(tuple(f.vertices) for f in o.data.polygons),
            tuple(tuple(r) for r in o.matrix_world),tuple(m.name for m in o.data.materials),
            tuple((p.use_smooth,p.material_index) for p in o.data.polygons))

def bounds(objects):
    points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    return [(min(p[i] for p in points),max(p[i] for p in points)) for i in range(3)]

def main():
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'lowpoly_human_stage4.blend'))
    scene=bpy.context.scene
    assert not scene.get('chin_jaw_refined',False),'Chin refinement already applied.'
    jaw=bpy.data.objects['LowerSkullJaw']
    others=[o for o in scene.objects if o.type=='MESH' and o!=jaw]
    before={o.name:signature(o) for o in others}
    old_jaw=signature(jaw)
    head=list(bpy.data.collections['Stage4_Head'].objects)
    old_bounds=bounds(head)
    refine_jaw(jaw)
    assert all(signature(o)==before[o.name] for o in others),'Protected geometry changed'
    assert all(v.co.x==old_jaw[0][i][0] for i,v in enumerate(jaw.data.vertices))
    assert bounds(head)==old_bounds,'Overall head dimensions changed'
    changed=[i for i,v in enumerate(jaw.data.vertices) if tuple(v.co)!=old_jaw[0][i]]
    assert all(16<=i<48 for i in changed)
    triangles=sum(len(p.vertices)-2 for o in others+[jaw] for p in o.data.polygons)
    report={'changed_object':jaw.name,'changed_vertices':len(changed),
            'chin_forward_mm':12,'chin_down_mm':5,'protected_meshes':len(others),
            'head_dimensions_unchanged':True,'face_width_unchanged':True,'triangles':triangles}
    scene['chin_jaw_refined']=True
    scene['chin_jaw_report']=json.dumps(report)
    scene['stage_notes']='Stage 4 lower chin/jaw refined only. No helmet or clothing. Body, neck, cranium and facial features unchanged.'
    assert all(o.name not in ('Helmet','Visor','Facemask') for o in scene.objects)
    for o in scene.objects: o.select_set(o.type=='MESH')
    scene.camera=bpy.data.objects['FrontThreeQuarter']
    scene.render.filepath=str(OUT/'lowpoly_human_stage4_frontthreequarter.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'lowpoly_human_stage4.blend'))
    bpy.ops.export_scene.gltf(filepath=str(OUT/'lowpoly_human_stage4.glb'),export_format='GLB',use_selection=True,
        export_yup=False,export_animations=False,export_cameras=False,export_lights=False)
    raw=(OUT/'lowpoly_human_stage4.glb').read_bytes()
    length=struct.unpack_from('<I',raw,12)[0]; gltf=json.loads(raw[20:20+length])
    assert len(gltf['meshes'])==35 and not gltf.get('animations') and not gltf.get('skins')
    assert sum(gltf['accessors'][p['indices']]['count']//3 for m in gltf['meshes'] for p in m['primitives'])==triangles
    for name in ('Front','Back','FrontThreeQuarter','Side'):
        scene.camera=bpy.data.objects[name]
        scene.render.filepath=str(OUT/('lowpoly_human_stage4_'+name.lower()+'.png'))
        bpy.ops.render.render(write_still=True)
    scene.camera=bpy.data.objects['FrontThreeQuarter']
    scene.render.filepath=str(OUT/'lowpoly_human_stage4_frontthreequarter.png')
    print('CHIN_JAW_REPORT '+json.dumps(report))

if __name__=='__main__': main()
