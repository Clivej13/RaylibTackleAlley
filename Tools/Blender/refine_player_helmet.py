"""Refine existing Head-skinned helmet meshes through Blender MCP.

Keeps object IDs, parents, transforms, materials and Head attachment. Saved
pre-pass coordinates/topology provide deterministic repeats; only the cage
receives additional low-sided rails. No body, uniform, rig or action edits.
"""
from pathlib import Path
import bpy, bmesh, math, json, sys, shutil, hashlib, runpy
from mathutils import Vector, Matrix
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Models';PRE=OUT/'HelmetRefinementPreviews'
sys.dont_write_bytecode=True
sys.path.insert(0,str(ROOT/'Tools/Blender'))
from upgrade_player_jersey import signature, hash_value
from upgrade_player_uniform import export
from validate_helmet_skinning import EQUIPMENT

def protected_signature():
    result=signature()
    for name in EQUIPMENT:result['objects'].pop(name,None)
    # Include garments excluded by the legacy jersey signature.
    for n in ('Jersey','Undershirt'):
        o=bpy.data.objects[n]
        result['objects'][n]=hash_value(([tuple(v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons],[[ (g.group,g.weight) for g in v.groups] for v in o.data.vertices]))
    result['helmet_attachment']={n:hash_value((bpy.data.objects[n].parent.name,bpy.data.objects[n].parent_type,tuple(map(tuple,bpy.data.objects[n].matrix_world)),[(m.name,m.type,m.object.name,m.use_deform_preserve_volume) for m in bpy.data.objects[n].modifiers if m.type=='ARMATURE'],[g.name for g in bpy.data.objects[n].vertex_groups])) for n in EQUIPMENT}
    return result

def originals(o):
    key='helmet_refinement_original'
    if key not in o:
        o[key]=json.dumps({'vertices':[tuple(v.co) for v in o.data.vertices],'faces':[tuple(p.vertices) for p in o.data.polygons]})
    return json.loads(o[key])

def tube(vs,fs,points,r):
    start=len(vs);pts=[Vector(p) for p in points];sides=6
    for j,p in enumerate(pts):
        t=(pts[min(j+1,len(pts)-1)]-pts[max(0,j-1)]).normalized()
        ref=Vector((0,1,0)) if abs(t.y)<.9 else Vector((1,0,0))
        u=t.cross(ref).normalized();v=t.cross(u).normalized()
        for k in range(sides):
            a=math.tau*k/sides;vs.append(tuple(p+r*(math.cos(a)*u+math.sin(a)*v)))
    fs.append(tuple(start+i for i in reversed(range(sides))))
    for j in range(len(pts)-1):
        for k in range(sides):
            a=start+j*sides+k;b=start+j*sides+(k+1)%sides
            fs.append((a,b,b+sides,a+sides))
    fs.append(tuple(start+(len(pts)-1)*sides+k for k in range(sides)))

def refine():
    for name in EQUIPMENT:
        o=bpy.data.objects[name];src=originals(o)
        if name=='Facemask':
            verts=[]
            for x,y,z in src['vertices']:
                # Keep temple mounts; increase face projection progressively.
                front=max(0,min(1,(-z-.065)/.09))
                rear=max(0,min(1,(z+.14)/.09))
                jaw=max(0,min(1,(1.735-y)/.045))
                verts.append((x,y-.006*max(0,min(1,(1.74-y)/.09))+.030*rear*jaw,z-.012*front))
            faces=[tuple(p) for p in src['faces']]
            # Continuous brow bar above the retained mid-face rail.
            tube(verts,faces,[(-.099,1.780,-.093),(-.118,1.779,-.128),(-.086,1.779,-.163),(0,1.778,-.173),(.086,1.779,-.163),(.118,1.779,-.128),(.099,1.780,-.093)],.007)
            # Pair of lower supports joins existing nose and chin rails.
            for sign in (-1,1):
                tube(verts,faces,[(sign*.044,1.743,-.168),(sign*.044,1.647,-.173)],.006)
            o.data.clear_geometry();o.data.from_pydata(verts,[],faces);o.data.update()
            o.vertex_groups['Head'].add(list(range(len(verts))),1,'REPLACE')
            bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
        else:
            assert len(src['vertices'])==len(o.data.vertices)
            for v,p in zip(o.data.vertices,src['vertices']):
                x,y,z=p
                if name=='Helmet':
                    # Lower crown; elongate rear/brow without scaling the head.
                    crown=max(0,min(1,(y-1.76)/.164))
                    low=max(0,min(1,(1.78-y)/.10))
                    side=min(1,abs(x)/.11)
                    xx=x*(1+.025*(1-crown))
                    yy=y-.024*crown-.005*low*side
                    zz=.02+(z-.02)*1.09+.003*crown
                    # Pull low rear inward for a clean neck cutout.
                    zz-=.014*low*max(0,min(1,(z-.05)/.08))
                    v.co=(xx,yy,zz)
                elif name=='Visor':
                    v.co=(x*.91,1.726+(y-1.707)*(.052/.074),z+.005)
                else:
                    center=max(0,1-(abs(x)/.14)**2)
                    anchor=max(0,min(1,(abs(x)-.09)/.05))
                    v.co=(x*(1.012-.037*anchor),y-.003*center+.012*anchor,z-.010*center+.018*anchor)
            o.data.update()
        for p in o.data.polygons:p.use_smooth=False

def aim(camera,target):
    back=(camera.location-Vector(target)).normalized();right=Vector((0,1,0)).cross(back).normalized();up=back.cross(right).normalized()
    camera.rotation_euler=Matrix((right,up,back)).transposed().to_euler()

def previews(prefix='',rest=True):
    s=bpy.context.scene;r=bpy.data.objects['PlayerRig']
    if rest:r.data.pose_position='REST'
    s.render.resolution_x=800;s.render.resolution_y=800;s.render.resolution_percentage=100;s.eevee.taa_render_samples=48;s.render.image_settings.file_format='PNG'
    for view,pos in [('Front',(0,1.76,-4)),('Side',(4,1.76,0)),('ThreeQuarter',(2.5,1.89,-4))]:
        camera=bpy.data.objects['HelmetCloseThreeQuarter'];camera.location=pos;camera.data.ortho_scale=.49;aim(camera,(0,1.765,0))
        s.camera=camera;s.render.filepath=str(PRE/(prefix+'Helmet_'+view+'.png'));bpy.ops.render.render(write_still=True)
    for view in ('Front','Side','FrontThreeQuarter'):
        s.camera=bpy.data.objects[view];s.render.resolution_x=720;s.render.resolution_y=800
        s.render.filepath=str(PRE/(prefix+'Player_'+view+'.png'));bpy.ops.render.render(write_still=True)

def main():
    PRE.mkdir(exist_ok=True);baseline=PRE/'Baseline';baseline.mkdir(exist_ok=True)
    report={}
    cs={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in ROOT.rglob('*.cs') if not any(k in p.parts for k in ('.git','bin','obj'))}
    for name in ('football_player.glb','lowpoly_human_jog_validation.glb','lowpoly_human_run_validation.glb','lowpoly_human_sprint_validation.glb'):
        if not (baseline/name).exists():shutil.copy2(OUT/name,baseline/name)
    for stem in ('rigged','jog','run','sprint'):
        path=OUT/('lowpoly_human_'+stem+'.blend')
        if not (baseline/path.name).exists():shutil.copy2(path,baseline/path.name)
        bpy.ops.wm.open_mainfile(filepath=str(path));bpy.context.preferences.filepaths.save_version=0
        before=protected_signature()
        if stem=='rigged' and not (PRE/'Before_Helmet_Front.png').exists():
            previews('Before_');bpy.ops.wm.open_mainfile(filepath=str(path))
        counts={n:sum(len(p)-2 for p in originals(bpy.data.objects[n])['faces']) for n in EQUIPMENT}
        refine();assert before==protected_signature(),'Protected data changed'
        after={n:sum(len(p.vertices)-2 for p in bpy.data.objects[n].data.polygons) for n in EQUIPMENT}
        report[stem]={'protected':before,'triangles_before':counts,'triangles_after':after}
        bpy.ops.wm.save_as_mainfile(filepath=str(path))
        if stem=='rigged':previews()
        else:export(OUT/('lowpoly_human_'+stem+'_validation.glb'))
    runpy.run_path(str(ROOT/'Tools/Blender/export_football_player.py'),run_name='__main__')
    assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in cs.items())
    report['csharp_unchanged']=True
    (PRE/'preservation.json').write_text(json.dumps(report,indent=2))
    print('HELMET_REFINEMENT_COMPLETE',report['rigged']['triangles_before'],report['rigged']['triangles_after'])

if __name__=='__main__':main()
