"""In-place uniform fit pass. Execute with Blender MCP, after uniform generation.

No objects, topology, weights, rig, rest transforms or actions are rebuilt.
Original coordinates are retained on the mesh for deterministic repeated runs.
"""
from pathlib import Path
import bpy, json, sys, hashlib, runpy, shutil
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Models'
PRE=OUT/'SilhouettePreviews'
sys.dont_write_bytecode=True
sys.path.insert(0,str(ROOT/'Tools/Blender'))
import upgrade_player_jersey as jersey
import validate_player_jersey as audit
from upgrade_player_uniform import export, material

def fingerprint():
    result=jersey.signature()
    result['weights']={o.name:jersey.hash_value([[(g.group,g.weight) for g in v.groups] for v in o.data.vertices]) for o in bpy.data.objects if o.type=='MESH'}
    result['topology']={o.name:jersey.hash_value([tuple(p.vertices) for p in o.data.polygons]) for o in bpy.data.objects if o.type=='MESH'}
    return result

def triangles():
    return sum(len(p.vertices)-2 for o in bpy.context.scene.objects if o.type=='MESH' and not o.get('uniform_reference') for p in o.data.polygons)

def refine():
    o=bpy.data.objects['Jersey']
    if 'silhouette_original_coordinates' not in o.data:
        o.data['silhouette_original_coordinates']=[c for v in o.data.vertices for c in v.co]
    original=list(o.data['silhouette_original_coordinates'])
    assert len(original)==len(o.data.vertices)*3
    for v in o.data.vertices:v.co=original[3*v.index:3*v.index+3]
    # Keep chest breadth; take excess ease out below the padded ribcage.
    # Hem remains wider than the waist and overlaps the existing waistband.
    widths=[.198,.189,.197,.216,.241,.246,.239,.19,.067,.067]
    oldwidth=[.204,.202,.208,.221,.241,.246,.239,.19,.076,.076]
    for v in o.data.vertices:
        if v.index<160:
            row=v.index//16
            x,y,z=v.co
            v.co.x=x*widths[row]/oldwidth[row]
            if row==0:v.co.y-=.028
            if row>=8:v.co.z=.005+(z-.005)*.88
            # Subtle convex fabric panels, retaining side clearance over pads.
            if row<8:
                bulge=.006*(1-min(1,abs(x)/oldwidth[row])**2)
                v.co.z+=(-bulge if z<.005 else bulge)
        else:
            ws={o.vertex_groups[g.group].name:g.weight for g in v.groups}
            side='L' if v.co.x>0 else 'R'
            sign=1 if side=='L' else -1
            if ws.get('Clavicle.'+side,0)>.99:
                center=Vector((sign*.245,1.474,.005))
                # Original cap envelope was inflated 28%; retain 14% clearance.
                v.co=center+(v.co-center)*(1.14/1.28)
            else:
                bone=bpy.data.objects['PlayerRig'].data.bones['UpperArm.'+side]
                center=bone.head_local.lerp(bone.tail_local,.46)
                v.co=center+(v.co-center)*.90
    # Close the small collar/chest seam exposed by the approved Sprint lean.
    # Keep the base layer fitted; extend only its existing neck binding.
    under=bpy.data.objects['Undershirt']
    if 'silhouette_original_coordinates' not in under.data:
        under.data['silhouette_original_coordinates']=[c for v in under.data.vertices for c in v.co]
    saved=list(under.data['silhouette_original_coordinates'])
    for v in under.data.vertices:v.co=saved[3*v.index:3*v.index+3]
    for v in under.data.vertices:
        if 223<=v.index<287:
            v.co.y=1.55+(v.co.y-1.55)*1.35
            v.co.x*=1.06
            v.co.z*=1.06
    # Smooth normals soften polygon panels without adding triangles or wrinkles.
    for name in ('Jersey','Undershirt','Pants'):
        for p in bpy.data.objects[name].data.polygons:p.use_smooth=True
        bpy.data.objects[name].data.update()
    material('Undershirt',(.09,.108,.13))

def main():
    PRE.mkdir(exist_ok=True)
    jersey.PRE=PRE
    audit.PRE=PRE
    cs={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in ROOT.rglob('*.cs') if not any(k in p.parts for k in ('.git','bin','obj'))}
    report={}
    for stem in ('rigged','jog','run','sprint'):
        path=OUT/('lowpoly_human_'+stem+'.blend')
        backup=PRE/'Baseline'/path.name
        backup.parent.mkdir(exist_ok=True)
        if not backup.exists():shutil.copy2(path,backup)
        bpy.ops.wm.open_mainfile(filepath=str(path))
        bpy.context.preferences.filepaths.save_version=0
        before=fingerprint();count=triangles()
        refine()
        after=fingerprint()
        assert before==after,'An existing object, weight, rig or action was changed'
        checks=jersey.validate()
        report[stem]={'preservation':before,'unchanged':True,'weights_unchanged':True,'topology_unchanged':True,'triangles_before':count,'triangles_after':triangles(),'animation_checks':checks}
        bpy.ops.wm.save_as_mainfile(filepath=str(path))
        if stem=='rigged':jersey.render('Standing',True)
        else:
            export(OUT/('lowpoly_human_'+stem+'_validation.glb'))
            bpy.context.scene.frame_set({'jog':5,'run':5,'sprint':4}[stem]);jersey.render(stem.title())
    runpy.run_path(str(ROOT/'Tools/Blender/export_football_player.py'),run_name='__main__')
    assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in cs.items())
    report['csharp_unchanged']=True
    (PRE/'validation.json').write_text(json.dumps(report,indent=2))
    print('SILHOUETTE_COMPLETE', {k:(v['triangles_before'],v['triangles_after']) for k,v in report.items() if isinstance(v,dict)})

if __name__=='__main__':main()
