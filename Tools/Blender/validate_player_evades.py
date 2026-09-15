"""Check saved evade assets and GLBs, including quarter-frame contact and grip."""
from pathlib import Path
import sys, json, math
sys.dont_write_bytecode=True
import bpy
from mathutils import Matrix, Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from create_player_evades import CONFIG, OUT, PRE, SOURCE, stem, phases, contacts, protected_hashes
from validate_player_hands import snapshot, digest, code_hashes, glb, grip_metrics, mesh_geometry
from validate_player_cuts import skeleton
from validate_inside_carry import relationship
from validate_helmet_skinning import validate_glb

def error(a,b): return max(abs(a[r][c]-b[r][c]) for r in range(4) for c in range(4))

def main():
    before=json.loads((PRE/'source_preservation.json').read_text())
    assert protected_hashes()==before['files']
    assert code_hashes()==before['code']
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    reference=snapshot(); rig=bpy.data.objects['PlayerRig']; scene=bpy.context.scene
    original={}
    for f in sorted({f for cfg in CONFIG.values() for f in phases(cfg)}):
        scene.frame_set(int(f),subframe=f-int(f))
        original[f]={p.name:(p.matrix.copy(),p.matrix_basis.copy()) for p in rig.pose.bones}
    ball=bpy.data.objects['FootballPreview']
    attachment=(ball.parent.name,ball.parent_type,ball.parent_bone,[list(row) for row in ball.matrix_parent_inverse],[list(row) for row in ball.matrix_basis])
    geometry=mesh_geometry(ball)
    canonical=skeleton(*glb(OUT/'football_player.glb'))
    report={'preserved_assets_byte_identical':True,'code_unchanged':True,'clips':{}}
    for name,cfg in CONFIG.items():
        bpy.ops.wm.open_mainfile(filepath=str(OUT/(stem(name)+'.blend')))
        scene=bpy.context.scene; rig=bpy.data.objects['PlayerRig']; current=snapshot()
        assert digest(current['bones'])==digest(reference['bones'])
        assert current['meshes']==reference['meshes']
        for n,a in reference['actions'].items(): assert current['actions'][n]==a
        assert set(current['actions'])==set(reference['actions'])|{name}
        assert current['active']==name and current['range']==[1,cfg['frames']]
        assert not any(p.constraints for p in rig.pose.bones)
        ball=bpy.data.objects['FootballPreview']
        assert mesh_geometry(ball)==geometry
        assert (ball.parent.name,ball.parent_type,ball.parent_bone,[list(row) for row in ball.matrix_parent_inverse],[list(row) for row in ball.matrix_basis])==attachment
        endpoint=0; arm_error=0
        for i,f in enumerate(phases(cfg),1):
            scene.frame_set(i)
            if i in (1,cfg['frames']):
                endpoint=max(endpoint,max(error(p.matrix,original[f][p.name][0]) for p in rig.pose.bones))
            for n in ('Clavicle.R','UpperArm.R','LowerArm.R','Hand.R','Fingers.R','Thumb.R'):
                arm_error=max(arm_error,error(rig.pose.bones[n].matrix_basis,original[f][n][1]))
        assert endpoint<1e-5,(name,'handoff',endpoint)
        assert arm_error<1e-6
        minimum=100; lateral=[]; yaw=[]; locked={}; slip={}; knees={}; floor_max={}
        for tick in range(4,cfg['frames']*4+1):
            scene.frame_set(tick//4,subframe=tick%4/4)
            t=(tick/4-1)/(cfg['frames']-1)
            assert error(rig.matrix_world,Matrix.Identity(4))<1e-8
            assert error(rig.pose.bones['Root'].matrix_basis,Matrix.Identity(4))<1e-8
            lateral.append(rig.pose.bones['Hips'].location.x)
            assert abs(rig.pose.bones['Hips'].location.z)<1e-8
            for p in rig.pose.bones:
                assert max(abs(v-1) for v in p.scale)<1e-6
                if p.name!='Hips': assert p.location.length<1e-6
            forward=rig.pose.bones['Hips'].matrix.to_3x3()@rig.data.bones['Hips'].matrix_local.to_3x3().inverted()@Vector((0,0,-1))
            a=math.degrees(math.atan2(-forward.x,-forward.z))
            if yaw:
                while a-yaw[-1]>180: a-=360
                while a-yaw[-1]<-180: a+=360
            yaw.append(a)
            dg=bpy.context.evaluated_depsgraph_get()
            verts={s:[o.matrix_world@v.co for o in [bpy.data.objects['LeftFoot' if s=='L' else 'RightFoot'].evaluated_get(dg)] for v in o.data.vertices] for s in ('L','R')}
            minimum=min(minimum,min(v.y for vs in verts.values() for v in vs))
            for j,(s,a,b,c,e,anchor,angle) in enumerate(contacts(cfg)):
                # Only intervals enclosed by two fully constrained bake keys.
                start=math.ceil(b*(cfg['frames']-1))+1; end=math.floor(c*(cfg['frames']-1))+1
                if start<=tick/4<=end:
                    vs=verts[s]
                    if j not in locked: locked[j]=[v.copy() for v in vs]; slip[j]=0; knees[j]=[]; floor_max[j]=-100
                    slip[j]=max(slip[j],max((v-w).length for v,w in zip(vs,locked[j])))
                    floor_max[j]=max(floor_max[j],min(v.y for v in vs))
                    u,l=[rig.pose.bones[n+'.'+s] for n in ('UpperLeg','LowerLeg')]
                    knees[j].append(math.degrees((u.tail-u.head).angle(l.tail-l.head)))
        print('CONTACT_METRICS',name,minimum,slip,knees,flush=True)
        assert minimum>-.003,(name,'floor penetration',minimum)
        assert max(slip.values())<.002,(name,'contact drift',slip)
        assert max(floor_max.values())<.003
        assert max(knees[0])>40
        if cfg['kind']=='Juke':
            assert max(lateral)-min(lateral)>.25
            assert max(abs(a) for a in yaw)<20
        else:
            assert abs(abs(yaw[-1]-yaw[0])-360)<5
            assert len(locked)==3
        grip=grip_metrics(rig,require_loop=False)
        inside=relationship()
        path=OUT/(stem(name)+'.glb'); doc,read=glb(path)
        assert skeleton(doc,read)==canonical
        assert len(doc['animations'])==1 and doc['animations'][0]['name']==name
        assert not any(n.get('name') in ('Football','FootballPreview') or n.get('name','').startswith('_Cut') for n in doc['nodes'])
        times=[v[0] for s in doc['animations'][0]['samplers'] for v in read(s['input'])]
        assert abs(max(times)-min(times)-(cfg['frames']-1)/120)<1e-6
        for c in doc['animations'][0]['channels']:
            if doc['nodes'][c['target']['node']]['name']=='Root':
                values=read(doc['animations'][0]['samplers'][c['sampler']]['output'])
                assert all(max(abs(x-y) for x,y in zip(v,values[0]))<1e-8 for v in values)
        helmet=validate_glb(path)
        report['clips'][name]={'duration_seconds':(cfg['frames']-1)/120,'fps':120,'frames':cfg['frames'],
            'plant_bone':'Foot.'+cfg['plant'],'plant_knee_degrees':{j:[min(v),max(v)] for j,v in knees.items()},
            'contact_drift_m':slip,'minimum_foot_height_m':minimum,'maximum_contact_sole_height_m':floor_max,
            'lateral_hips_range_m':[min(lateral),max(lateral)],'unwrapped_pelvis_yaw_degrees':[yaw[0],min(yaw),max(yaw),yaw[-1]],
            'root_identity':True,'entry_exit_matrix_error':endpoint,'right_arm_local_matrix_error':arm_error,
            'geometry_weights_skeleton_original_actions_exact':True,'grip':grip,'inside_carry':inside,
            'export_helmet':helmet,'export_skeleton_exact':True,'carryrun_entry_exit_phases':[phases(cfg)[0],phases(cfg)[-1]]}
        (PRE/'validation.json').write_text(json.dumps(report,indent=2))
        print('EVADE_VALIDATED',name,flush=True)
    report['result']='PASS'
    (PRE/'validation.json').write_text(json.dumps(report,indent=2))
    print('ALL_EVADES_VALIDATED',flush=True)

if __name__=='__main__': main()
