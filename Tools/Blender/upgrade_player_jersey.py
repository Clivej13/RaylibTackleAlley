"""Add upper uniform layers to existing approved sources through Blender MCP.

Does not reconstruct body, pads, lower uniform, rig or actions. Run this module
after the lower-uniform pass. Original objects remain unchanged and editable.
"""
from pathlib import Path
import bpy, bmesh, math, json, hashlib, sys, runpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import numpy as np

ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'Assets/Models'; PRE=OUT/'JerseyPreviews'
sys.dont_write_bytecode=True
sys.path.insert(0,str(ROOT/'Tools/Blender'))
from upgrade_player_uniform import mesh, material, export
NEW=('Undershirt','Jersey')

def hash_value(x):return hashlib.sha256(repr(x).encode()).hexdigest()

def signature():
    r=bpy.data.objects['PlayerRig'];s=bpy.context.scene
    result={'bones':hash_value([(b.name,b.parent.name if b.parent else None,tuple(map(tuple,b.matrix_local)),b.length,b.use_deform) for b in r.data.bones]),
      'actions':{a.name:hash_value([(f.data_path,f.array_index,f.extrapolation,[(tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation,k.handle_left_type,k.handle_right_type) for k in f.keyframe_points]) for f in a.fcurves]) for a in bpy.data.actions},
      'timing':(s.frame_start,s.frame_end,s.render.fps,s.render.fps_base),
      'objects':{}}
    for o in bpy.data.objects:
        if o.type!='MESH' or o.name in NEW:continue
        result['objects'][o.name]=hash_value(([tuple(v.co) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons],[[ (g.group,g.weight) for g in v.groups] for v in o.data.vertices],[g.name for g in o.vertex_groups],tuple(map(tuple,o.matrix_world)),o.parent.name if o.parent else None,o.parent_bone,o.hide_render,bool(o.get('uniform_reference')),[m.name for m in o.data.materials],[(m.name,tuple(m.diffuse_color)) for m in o.data.materials]))
    return result

def ramp(x,a,b):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)

def torso_weight(y):
    if y<1.15:
        t=ramp(y,1.015,1.15);return {'Hips':1-t,'Spine':t}
    t=ramp(y,1.17,1.34);return {'Spine':1-t,'Chest':t}

class Builder:
    def __init__(self):self.v=[];self.f=[];self.w=[];self.mi=[]
    def add(self,verts,faces,weights,mat=0):
        off=len(self.v);self.v.extend(verts);self.f.extend(tuple(off+i for i in f) for f in faces);self.w.extend(weights);self.mi.extend([mat]*len(faces))
    def finish(self,name,mats):
        o=mesh(name,self.v,self.f,self.w,mats,self.mi)
        bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
        return o

def undershirt():
    b=Builder();r=bpy.data.objects['PlayerRig']
    for name in ['LowerBack','Belly','LeftChest','RightChest','UpperBack','NeckBase','LeftShoulder','RightShoulder','LeftUpperArm','RightUpperArm']:
        o=bpy.data.objects[name];verts=[];weights=[]
        for v in o.data.vertices:
            p=v.co+v.normal*.003
            # Keep a short neck binding, leaving the upper neck visibly exposed.
            if name=='NeckBase':p.y=1.55+(p.y-1.55)*.52
            verts.append(tuple(p));ws={o.vertex_groups[g.group].name:g.weight for g in v.groups if o.vertex_groups[g.group].name in r.data.bones and g.weight>1e-7}
            total=sum(ws.values());weights.append({n:w/total for n,w in ws.items()})
        b.add(verts,[tuple(p.vertices) for p in o.data.polygons],weights)
    return b.finish('Undershirt',[material('Undershirt',(.055,.068,.082))])

def jersey():
    b=Builder();pad=bpy.data.objects['ShoulderPads'];r=bpy.data.objects['PlayerRig']
    profile=[(0,-1),(.40,-1),(.76,-.94),(.94,-.60),(1,0),(.94,.57),(.74,.92),(.39,1),(0,.96),(-.39,1),(-.74,.92),(-.94,.57),(-1,0),(-.94,-.60),(-.76,-.94),(-.40,-1)]
    rows=[(1.005,.204,.15,.149),(1.09,.202,.146,.143),(1.20,.208,.151,.147),(1.28,.221,.165,.158),(1.36,.241,.186,.176),(1.43,.246,.195,.187),(1.50,.239,.194,.184),(1.559,.19,.148,.148),(1.598,.076,.065,.074),(1.535,.076,.065,.074)]
    verts=[];weights=[];faces=[]
    for y,rx,front,back in rows:
        for x,z in profile:
            verts.append((x*rx,y,.005+z*(front if z<0 else back)))
            # Pad shell is rigid on Chest. Match it over its full depth, then
            # blend only below its lower edge into the original torso weights.
            if y>=1.20:ws={'Chest':1}
            else:
                t=ramp(y,1.07,1.20);ws={'Chest':t,'Spine':1-t}
                if y<1.07:ws=torso_weight(y)
            weights.append(ws)
    for j in range(len(rows)-1):
        for i in range(16):
            faces.append((j*16+i,j*16+(i+1)%16,(j+1)*16+(i+1)%16,(j+1)*16+i))
    b.add(verts,faces,weights)
    for side,sign in [('L',1),('R',-1)]:
        prefix='Left' if side=='L' else 'Right'
        groups={pad.vertex_groups[prefix+'ShoulderCap'].index,pad.vertex_groups[prefix+'CapPadding'].index}
        cap=[v.co.copy() for v in pad.data.vertices if any(g.group in groups for g in v.groups)]
        center=Vector((sign*.245,1.474,.005))
        verts=[tuple(center+(p-center)*1.28) for p in cap]
        weights=[{'Clavicle.'+side:1} for _ in verts]
        bone=r.data.bones['UpperArm.'+side];axis=(bone.tail_local-bone.head_local).normalized()
        radial=Vector((sign*.855,.519,0));depth=Vector((0,0,1))
        # Short loose sleeve; dark fitted undershirt extends further down arm.
        cuff=bone.head_local.lerp(bone.tail_local,.46)
        for i in range(12):
            a=math.tau*i/12;p=cuff+radial*(.088*math.cos(a))+depth*(.09*math.sin(a));verts.append(tuple(p));weights.append({'UpperArm.'+side:1})
        bm=bmesh.new()
        for p in verts:bm.verts.new(p)
        bm.verts.ensure_lookup_table();bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
        bm.verts.index_update();faces=[tuple(v.index for v in f.verts) for f in bm.faces if not all(v.index>=len(cap) for v in f.verts)]
        b.add(verts,faces,weights,1);bm.free()
        # Cuff facing closes the view into the loose sleeve around the fitted
        # base layer, preventing the pad underside appearing through its opening.
        facing=[]
        for rx,rz in [(.088,.09),(.050,.056)]:
            for i in range(12):
                a=math.tau*i/12;facing.append(tuple(cuff+radial*(rx*math.cos(a))+depth*(rz*math.sin(a))))
        b.add(facing,[(i,(i+1)%12,12+(i+1)%12,12+i) for i in range(12)],[{'UpperArm.'+side:1} for _ in facing],1)
    return b.finish('Jersey',[material('Jersey',(.20,.27,.33)),material('Jersey_Sleeves',(.20,.27,.33))])

def install():
    for n in NEW:
        if n in bpy.data.objects:
            old=bpy.data.objects[n];data=old.data;bpy.data.objects.remove(old,do_unlink=True)
            if not data.users:bpy.data.meshes.remove(data)
    undershirt();jersey()

def tree(o,dg):
    ev=o.evaluated_get(dg)
    return BVHTree.FromPolygons([v.co.copy() for v in ev.data.vertices],[tuple(p.vertices) for p in ev.data.polygons])

def validate():
    r=bpy.data.objects['PlayerRig'];s=bpy.context.scene;action=r.animation_data.action;frame=s.frame_current;report={}
    for n in NEW:
        o=bpy.data.objects[n]
        for v in o.data.vertices:
            ws={o.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>1e-7}
            assert abs(sum(ws.values())-1)<1e-5
            assert not (any(k.endswith('.L') for k in ws) and any(k.endswith('.R') for k in ws))
    for name in ('Pose_Neutral','Jog','Run','Sprint'):
        if name not in bpy.data.actions:continue
        a=bpy.data.actions[name];r.animation_data.action=a;intersections=[]
        for f in range(int(a.frame_range[0]),int(a.frame_range[1])+1):
            s.frame_set(f);dg=bpy.context.evaluated_depsgraph_get()
            pairs=tree(bpy.data.objects['Jersey'],dg).overlap(tree(bpy.data.objects['ShoulderPads'],dg))
            if pairs:intersections.append({'frame':f,'pairs':len(pairs)})
            for n in NEW:assert all(math.isfinite(c) for v in bpy.data.objects[n].evaluated_get(dg).data.vertices for c in v.co)
        report[name]={'frames_checked':int(a.frame_range[1]-a.frame_range[0])+1,'jersey_pad_intersections':intersections}
    r.animation_data.action=action;s.frame_set(frame)
    return report

def render(tag,rest=False):
    s=bpy.context.scene;r=bpy.data.objects['PlayerRig'];pose=r.data.pose_position
    if rest:r.data.pose_position='REST'
    s.render.image_settings.file_format='PNG';s.render.resolution_x=720;s.render.resolution_y=800;s.render.resolution_percentage=100;s.eevee.taa_render_samples=32
    for view in (('Front','Side','Back') if rest else ('FrontThreeQuarter','Side')):
        s.camera=bpy.data.objects[view];s.render.filepath=str(PRE/(tag+'_'+view+'.png'));bpy.ops.render.render(write_still=True)
    r.data.pose_position=pose

def main():
    PRE.mkdir(exist_ok=True);report={}
    # Filesystem hashes establish that unrelated C# changes belong to the user.
    cs={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in ROOT.rglob('*.cs') if not any(x in p.parts for x in ('.git','bin','obj'))}
    for stem in ('rigged','jog','run','sprint'):
        path=OUT/('lowpoly_human_'+stem+'.blend');bpy.ops.wm.open_mainfile(filepath=str(path));bpy.context.preferences.filepaths.save_version=0
        before=signature();install();assert signature()==before,'Preserved objects or animation changed'
        checks=validate();tris={n:sum(len(p.vertices)-2 for p in bpy.data.objects[n].data.polygons) for n in NEW}
        report[stem]={'preservation':before,'unchanged':True,'triangles':tris,'animation_checks':checks}
        bpy.ops.wm.save_as_mainfile(filepath=str(path))
        if stem=='rigged':render('Standing',True)
        else:
            export(OUT/('lowpoly_human_'+stem+'_validation.glb'))
            bpy.context.scene.frame_set({'jog':5,'run':5,'sprint':4}[stem]);render(stem.title())
    runpy.run_path(str(ROOT/'Tools/Blender/export_football_player.py'),run_name='__main__')
    assert all(hashlib.sha256((ROOT/n).read_bytes()).hexdigest()==h for n,h in cs.items())
    report['csharp_unchanged']=True
    (PRE/'validation.json').write_text(json.dumps(report,indent=2))
    print('JERSEY_COMPLETE',json.dumps({s:{k:v for k,v in data.items() if k!='preservation'} if isinstance(data,dict) else data for s,data in report.items()}))

if __name__=='__main__':main()
