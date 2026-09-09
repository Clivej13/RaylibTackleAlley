"""Refine the existing Stage 4 head in place through Blender MCP.

Also supplies the deterministic refinement used by the Stage 4 generator.
"""
from pathlib import Path
import json
import bpy

SCALE=(1.14,1.045,1.12)

def refine_head(collection):
    for ob in collection.objects:
        for vertex in ob.data.vertices:
            x,y,z=vertex.co
            # Preserve the head's embedded collar, blending to full expansion
            # at the jaw. The neck itself is never modified.
            blend=max(0.0,min(1.0,(y-1.613)/.046))
            vertex.co.x=x*(1+.14*blend)
            vertex.co.y=1.613+(y-1.613)*SCALE[1]
            # Expand primarily behind the facial plane, preserving its depth.
            vertex.co.z=z+(z+.080)*.12*blend
        ob.data.update()

def refine_head_second(collection):
    """Second approved pass: +6% X/Z, +1% Y, subtle extra jaw breadth."""
    for ob in collection.objects:
        for vertex in ob.data.vertices:
            x,y,z=vertex.co
            blend=max(0.0,min(1.0,(y-1.613)/.048))
            jaw=max(0.0,1.0-abs(y-1.674)/.045)*.01
            vertex.co.x=x*(1+(.06+jaw)*blend)
            vertex.co.y=1.613+(y-1.613)*1.01
            vertex.co.z=z+(z+.080)*.06*blend
        ob.data.update()

def signature(ob):
    return (tuple(tuple(v.co) for v in ob.data.vertices),
            tuple(tuple(p.vertices) for p in ob.data.polygons),
            tuple(tuple(row) for row in ob.matrix_world),
            tuple(m.name for m in ob.data.materials),
            tuple(p.use_smooth for p in ob.data.polygons))

def dimensions(objects):
    points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    return [round(max(p[i] for p in points)-min(p[i] for p in points),6) for i in range(3)]

def main():
    out=Path(__file__).resolve().parents[2]/'Assets'/'Models'
    bpy.ops.wm.open_mainfile(filepath=str(out/'lowpoly_human_stage4.blend'))
    scene=bpy.context.scene
    assert scene.get('head_mass_refined',False), 'First refinement required.'
    assert not scene.get('head_mass_refined_second',False), 'Second refinement already applied.'
    collection=bpy.data.collections['Stage4_Head']
    body=[o for o in scene.objects if o.type=='MESH' and o.name not in collection.objects]
    before={o.name:signature(o) for o in body}
    old=dimensions(list(collection.objects))
    refine_head_second(collection)
    assert all(signature(o)==before[o.name] for o in body), 'Body changed'
    new=dimensions(list(collection.objects))
    report={'old_dimensions_xyz_m':old,'new_dimensions_xyz_m':new,
            'scale_xyz':(1.06,1.01,1.06),'jaw_extra_width_max_percent':1,'preserved_body_meshes':len(body),
            'collar':'Expansion fades to zero at Y=1.613',
            'face':'Features follow common transform; depth pivot at facial plane Z=-0.080'}
    scene['head_mass_refined']=True
    scene['head_mass_refined_second']=True
    scene['head_refinement_report']=json.dumps(report)
    previous=json.loads(scene['stage4_report'])
    previous['head']['dimensions_xyz_m']=new
    previous['skull_width_m']=dimensions([bpy.data.objects['LowerSkullJaw'],bpy.data.objects['UpperSkullCranium']])[0]
    previous['head_width_with_ears_m']=new[0]; previous['head_height_m']=new[1]
    previous['whole_model']['dimensions_xyz_m']=dimensions(body+list(collection.objects))
    scene['stage4_report']=json.dumps(previous)
    for o in scene.objects: o.select_set(o.type=='MESH')
    scene.camera=bpy.data.objects['FrontThreeQuarter']
    scene.render.filepath=str(out/'lowpoly_human_stage4_frontthreequarter.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(out/'lowpoly_human_stage4.blend'))
    bpy.ops.export_scene.gltf(filepath=str(out/'lowpoly_human_stage4.glb'),export_format='GLB',
        use_selection=True,export_yup=False,export_animations=False,export_cameras=False,export_lights=False)
    for name in ('Front','FrontThreeQuarter','Side','Back'):
        scene.camera=bpy.data.objects[name]
        scene.render.filepath=str(out/('lowpoly_human_stage4_'+name.lower()+'.png'))
        bpy.ops.render.render(write_still=True)
    scene.camera=bpy.data.objects['FrontThreeQuarter']
    scene.render.filepath=str(out/'lowpoly_human_stage4_frontthreequarter.png')
    print('HEAD_REFINEMENT '+json.dumps(report))

if __name__=='__main__':
    main()
