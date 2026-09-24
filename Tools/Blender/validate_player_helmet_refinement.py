"""Read-only saved-source/GLB audit and previews, using Blender MCP."""
from pathlib import Path
import bpy, sys, json, math
import numpy as np
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
sys.dont_write_bytecode=True
sys.path.insert(0,str(ROOT/'Tools/Blender'))
import refine_player_helmet as fit
import validate_player_jersey as audit
from validate_player_silhouette import combine
from validate_helmet_skinning import EQUIPMENT, head_relative_vertices, validate_glb
PRE=fit.PRE;OUT=fit.OUT

def tree(names,dg):
    verts=[];faces=[]
    for name in names:
        o=bpy.data.objects[name].evaluated_get(dg);off=len(verts)
        verts.extend(o.matrix_world@v.co for v in o.data.vertices)
        faces.extend(tuple(off+i for i in p.vertices) for p in o.data.polygons)
    return BVHTree.FromPolygons(verts,faces),verts

def motion_check(stem):
    r=bpy.data.objects['PlayerRig'];s=bpy.context.scene
    head=[o.name for o in bpy.data.collections['Stage4_Head'].objects if o.type=='MESH']
    if stem=='rigged':r.data.pose_position='REST'
    start,end=(1,1) if stem=='rigged' else tuple(map(int,r.animation_data.action.frame_range))
    reference=None;max_error=0;hits=[];minimum={n:100 for n in EQUIPMENT};crossings={}
    # Half frames supplement the exported integer animation samples.
    frames=[start+i*.5 for i in range((end-start)*2+1)]
    for frame in frames:
        s.frame_set(int(frame),subframe=frame%1);dg=bpy.context.evaluated_depsgraph_get()
        current=head_relative_vertices(r,dg)
        if reference is None:reference=current
        max_error=max(max_error,max((p-q).length for n in EQUIPMENT for p,q in zip(reference[n],current[n])))
        h,hv=tree(head+['NeckBase'],dg)
        trees={}
        for n in EQUIPMENT:
            t,vs=tree([n],dg);trees[n]=t
            assert all(math.isfinite(c) for p in vs for c in p)
            count=len(t.overlap(h))
            if count:hits.append({'frame':frame,'object':n,'head_neck_intersections':count})
            minimum[n]=min(minimum[n],min(t.find_nearest(p)[3] for p in hv),min(h.find_nearest(p)[3] for p in vs))
        if frame==start:
            crossings={a+'/'+b:len(trees[a].overlap(trees[b])) for a,b in [('Helmet','Facemask'),('Helmet','Visor'),('Helmet','ChinStrap'),('Facemask','Visor'),('Facemask','ChinStrap')]}
        assert not trees['Helmet'].overlap(trees['Visor'])
        assert not trees['Facemask'].overlap(trees['Visor'])
        assert not trees['Facemask'].overlap(trees['ChinStrap'])
        # Shell contacts are intentional mechanical mounting junctions.
        assert trees['Helmet'].overlap(trees['Facemask'])
        assert trees['Helmet'].overlap(trees['ChinStrap'])
    assert max_error<1e-5,max_error
    return {'samples':len(frames),'max_head_attachment_error':max_error,'head_neck_intersections':hits,'sampled_minimum_head_neck_clearance':minimum,'equipment_intersections_at_first_frame':crossings}

def main():
    report={}
    for stem in ('rigged','jog','run','sprint'):
        filename='lowpoly_human_'+stem+'.blend'
        bpy.ops.wm.open_mainfile(filepath=str(PRE/'Baseline'/filename));before=fit.protected_signature()
        bpy.ops.wm.open_mainfile(filepath=str(OUT/filename));assert before==fit.protected_signature()
        coords={n:[tuple(v.co) for v in bpy.data.objects[n].data.vertices] for n in EQUIPMENT}
        fit.refine();assert coords=={n:[tuple(v.co) for v in bpy.data.objects[n].data.vertices] for n in EQUIPMENT}
        report[stem]=motion_check(stem)
        report[stem]['protected_source_data_unchanged']=True
    for filename in ('football_player.glb','lowpoly_human_jog_validation.glb','lowpoly_human_run_validation.glb','lowpoly_human_sprint_validation.glb'):
        doc,before=audit.channels((PRE/'Baseline'/filename).read_bytes())
        before_triangles=sum(doc['accessors'][p['indices']]['count']//3 for m in doc['meshes'] for p in m['primitives'])
        doc,after=audit.channels((OUT/filename).read_bytes())
        assert before.keys()==after.keys()
        assert all(all(np.array_equal(x,y) for x,y in zip(before[k],after[k])) for k in before)
        report[filename]={'exact_animation_channels':len(before),'head_skin_validation':validate_glb(OUT/filename),'triangles_before':before_triangles,'triangles':sum(doc['accessors'][p['indices']]['count']//3 for m in doc['meshes'] for p in m['primitives']),'joints':len(doc['skins'][0]['joints'])}
    audit.PRE=PRE
    for clip,frames in [('Jog',[1,5,10,15,19,24,28]),('Run',[1,5,9,13,17,21,24]),('Sprint',[1,4,7,11,14,17,20])]:
        bpy.ops.wm.open_mainfile(filepath=str(OUT/('lowpoly_human_'+clip.lower()+'.blend')))
        audit.contacts(clip,frames)
    for view in ('Front','Side','ThreeQuarter'):
        combine([PRE/('Before_Helmet_'+view+'.png'),PRE/('Helmet_'+view+'.png')],PRE/('Comparison_'+view+'.png'))
    (PRE/'validation.json').write_text(json.dumps(report,indent=2))
    print('HELMET_VALIDATION',json.dumps(report))
    assert all(not report[stem]['head_neck_intersections'] for stem in ('rigged','jog','run','sprint')),'Head or neck intersection detected'

if __name__=='__main__':main()
