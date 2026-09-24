"""Targeted existing-island alignment. Run through Blender MCP."""
from pathlib import Path
import json, struct, copy, hashlib, math
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'Assets/Models/UniformAtlas'
DEST=ROOT/'Assets/Models/football_player.glb'
REG=json.loads((ART/'atlas_regions.json').read_text())
TARGET={'Helmet_crown','Trousers_front','Trousers_back','Sleeve_L','Sleeve_R'}
def read(path):
    raw=path.read_bytes(); size=struct.unpack_from('<I',raw,12)[0]
    return raw,json.loads(raw[20:20+size]),bytearray(raw[28+size:]),28+size
def array(doc,buf,index):
    a=doc['accessors'][index]; v=doc['bufferViews'][a['bufferView']]
    dt=np.dtype({5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']])
    nc={'VEC2':2,'VEC3':3,'VEC4':4,'SCALAR':1}[a['type']]
    return np.ndarray((a['count'],nc),dtype=dt,buffer=buf,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',dt.itemsize*nc),dt.itemsize))
def collect(doc,buf):
    islands=[]
    for mi,m in enumerate(doc['meshes']):
        for pi,p in enumerate(m['primitives']):
            if doc['materials'][p['material']]['name']!='Uniform': continue
            pos=array(doc,buf,p['attributes']['POSITION']); uv=array(doc,buf,p['attributes']['TEXCOORD_0']); ix=array(doc,buf,p['indices']).ravel().reshape(-1,3)
            # glTF image-space UV -> bottom-left UV for grouping and transforms.
            old=uv.copy(); old[:,1]=1-old[:,1]
            groups={}
            for ti,t in enumerate(ix):
                c=old[t].mean(axis=0)
                name=next(k for k,(x0,y0,x1,y1) in REG.items() if x0<=c[0]<=x1 and y0<=c[1]<=y1)
                if name in TARGET: groups.setdefault(name,[]).append(ti)
            for name,tids in groups.items():
                # Match original island edges across hard-normal vertex splits.
                links={}; adj={t:set() for t in tids}
                for ti in tids:
                    keys=[tuple(np.round(np.r_[pos[v],old[v]],6)) for v in ix[ti]]
                    for j in range(3):
                        edge=tuple(sorted((keys[j],keys[(j+1)%3])))
                        if edge in links: adj[ti].add(links[edge]); adj[links[edge]].add(ti)
                        else: links[edge]=ti
                pending=set(tids); ni=0
                while pending:
                    stack=[min(pending)]; comp=set()
                    while stack:
                        t=stack.pop()
                        if t in comp: continue
                        comp.add(t); stack.extend(adj[t]-comp)
                    pending-=comp; vs=np.unique(ix[sorted(comp)])
                    islands.append(dict(name=name,id=f'{name}_{ni:02d}',mi=mi,pi=pi,tris=sorted(comp),vs=vs,pos=pos[vs].copy(),old=old[vs].copy(),uv=uv))
                    ni+=1
    return islands
def rotate(i,kind):
    q=i['old'].astype(float)*2048; p=i['pos']; a=np.c_[q,np.ones(len(q))]
    target=p[:,0] if kind=='crown' else (p[:,2] if kind=='trousers' else p[:,1]-.38*np.abs(p[:,0]))
    coef=np.linalg.lstsq(a,target,rcond=None)[0]; g=coef[:2].copy(); g/=np.linalg.norm(g)
    if kind in ('crown','trousers'):
        mask=np.abs(target-(.01 if kind=='trousers' else 0))<1e-5
        points=np.unique(q[mask],axis=0)
        if len(points)>2:
            _,_,v=np.linalg.svd(points-points.mean(0),full_matrices=False)
            normal=np.array([v[0,1],-v[0,0]])
            if normal@g<0: normal=-normal
            g=normal
        rot=np.array([g,[-g[1],g[0]]]).T
    else: rot=np.array([[g[1],-g[0]],g]).T
    i['rot']=rot; i['angle']=float(np.degrees(np.arctan2(rot[1,0],rot[0,0])))
    i['shape']=q@rot
    return i['shape']
def box(q): return np.r_[q.min(0),q.max(0)]
def collision(a,b,pad=8): return not (a[2]+pad<=b[0] or b[2]+pad<=a[0] or a[3]+pad<=b[1] or b[3]+pad<=a[1])
def local_place(items,fixed,region):
    """Place only edited islands near original locations; reserve untouched islands."""
    bounds=np.array(REG[region])*2048; bounds[:2]+=12; bounds[2:]-=12
    occupied=[box(i['old']*2048) for i in fixed]
    ordered=sorted(items,key=lambda i:-np.prod(np.ptp(i['shape'],axis=0)))
    candidates=[]
    for i in ordered:
        q=i['shape']; size=np.ptp(q,axis=0)
        preferred=(i['old'].min(0)+i['old'].max(0))*1024-size/2
        xs=np.unique(np.r_[np.arange(bounds[0],bounds[2]-size[0]+.01,6),np.clip(preferred[0],bounds[0],bounds[2]-size[0])])
        ys=np.unique(np.r_[np.arange(bounds[1],bounds[3]-size[1]+.01,6),np.clip(preferred[1],bounds[1],bounds[3]-size[1])])
        cs=[np.r_[x,y,x+size[0],y+size[1]] for x in xs for y in ys]
        cs=[b for b in cs if not any(collision(b,f) for f in occupied)]
        cs.sort(key=lambda b:float(np.sum((b[:2]-preferred)**2)))
        candidates.append(cs)
    attempts=[0]
    def search(k):
        if k==len(ordered): return True
        for b in candidates[k]:
            attempts[0]+=1
            if attempts[0]>2000000: return False
            if any(collision(b,f) for f in occupied): continue
            occupied.append(b)
            if search(k+1):
                i=ordered[k]; i['new']=i['shape']-i['shape'].min(0)+b[:2]; return True
            occupied.pop()
        return False
    assert search(0),('No rigid local placement',region,attempts)
def main():
    backup=ART/'football_player_before_usability.glb'
    if not backup.exists(): backup.write_bytes(DEST.read_bytes())
    raw,doc,buf,off=read(backup)
    # Refuse to overwrite later unrelated model edits on a deterministic rerun.
    current,cd,cb,coff=read(DEST)
    assert cd==doc, 'Current GLB structure differs from preserved input'
    islands=collect(doc,buf); edits=[]; graphics=[]
    crowns=[i for i in islands if i['name']=='Helmet_crown']
    placements={0:(1754,934),1:(1754,1110),2:(1754,805),3:(1942,934),4:(1942,1110),5:(1942,805),6:(1754,708),7:(1754,609),8:(1910,610),9:(1960,610),10:(2010,610),11:(1910,710),12:(1960,710),13:(2010,710)}
    for i in crowns:
        idx=int(i['id'].split('_')[-1])
        if idx==13: continue  # This small tab already clears the aligned shell.
        if idx>=8:
            i['rot']=np.eye(2); i['angle']=0.0; i['shape']=i['old'].astype(float)*2048
            q=i['shape']  # Clearance-only translation; no reason to rotate these tabs.
        else: q=rotate(i,'crown')
        center=(q.min(0)+q.max(0))/2
        if idx<=7:
            mask=np.abs(i['pos'][:,0])<1e-5; center[0]=q[mask,0].mean()
        i['new']=q-center+placements[idx]; edits.append(i)
    graphics.append(dict(region='Helmet_crown',kind='vertical',center_px=1754,width_px=26,span_px=[.26*2048,.59*2048],note='Outer crown and rim centerline; inner shell remains red'))
    for name in ('Trousers_front','Trousers_back'):
        group=[i for i in islands if i['name']==name]
        ids={0,2,5,6} if name.endswith('front') else {0,2,3,8}
        changed=[i for i in group if int(i['id'].split('_')[-1]) in ids]
        for i in changed: rotate(i,'trousers')
        local_place(changed,[i for i in group if i not in changed],name)
        for i in changed:
            q=i['new']; mask=np.abs(i['pos'][:,2]-.01)<1e-5
            center=float(q[mask,0].mean())
            graphics.append(dict(region=name,island=i['id'],kind='vertical',center_px=center,width_px=24,span_px=[float(q[:,1].min()-3),float(q[:,1].max()+3)]))
        edits+=changed
    for name in ('Sleeve_L','Sleeve_R'):
        group=[i for i in islands if i['name']==name]
        changed=group[:4]
        for i in changed: rotate(i,'sleeve')
        local_place(changed,group[4:],name)
        for i in changed:
            q=i['new']; axis=i['pos'][:,1]-.38*np.abs(i['pos'][:,0])
            fit=np.linalg.lstsq(np.c_[q,np.ones(len(q))],axis,rcond=None)[0]
            center=float((1.265-fit[2])/fit[1])
            graphics.append(dict(region=name,island=i['id'],kind='horizontal',center_px=center,width_px=float(.032/abs(fit[1])),span_px=[float(q[:,0].min()-3),float(q[:,0].max()+3)]))
        edits+=changed
    # Validate bounds and conservative inter-island bounding-box clearance.
    for i in islands: i.setdefault('new',i['old'].astype(float)*2048)
    for i in edits:
        b=box(i['new']); r=np.array(REG[i['name']])*2048
        assert np.all(b[:2]>=r[:2]+11.99) and np.all(b[2:]<=r[2:]-11.99),(i['id'],b,r)
        for j in islands:
            if j is not i and i['name']==j['name']:
                assert not collision(b,box(j['new']),7.99),(i['id'],j['id'],'padding')
        uv=i['new']/2048; uv[:,1]=1-uv[:,1]; i['uv'][i['vs']]=uv
    # Exact binary patch: no accessors, indices, vertices, materials or animations added.
    allowed=np.zeros(len(buf),dtype=bool)
    for i in edits:
        p=doc['meshes'][i['mi']]['primitives'][i['pi']]; a=doc['accessors'][p['attributes']['TEXCOORD_0']]; v=doc['bufferViews'][a['bufferView']]
        for vi in i['vs']:
            start=v.get('byteOffset',0)+a.get('byteOffset',0)+int(vi)*v.get('byteStride',8); allowed[start:start+8]=True
    old=np.frombuffer(raw[off:],dtype=np.uint8); new=np.frombuffer(buf,dtype=np.uint8)
    assert np.array_equal(old[~allowed],new[~allowed])
    previous=ART/'usability_changes.json'
    known_previous=previous.exists() and hashlib.sha256(current).hexdigest()==json.loads(previous.read_text())['output_sha256']
    assert known_previous or np.array_equal(np.frombuffer(cb,dtype=np.uint8)[~allowed],old[~allowed]),'Unrelated source bytes changed since backup'
    output=raw[:off]+bytes(buf)
    # Recreate the wireframe from the final unchanged-topology GLB.
    svg=['<svg xmlns="http://www.w3.org/2000/svg" width="2048" height="2048" viewBox="0 0 2048 2048">','<rect width="2048" height="2048" fill="#222"/>']
    for m in doc['meshes']:
        for p in m['primitives']:
            if doc['materials'][p['material']]['name']!='Uniform': continue
            uv=array(doc,buf,p['attributes']['TEXCOORD_0']); ix=array(doc,buf,p['indices']).ravel().reshape(-1,3)
            for tri in ix:
                pts=' '.join(f'{u*2048:.2f},{v*2048:.2f}' for u,v in uv[tri]); svg.append(f'<polygon points="{pts}" fill="none" stroke="#ddd" stroke-width=".65"/>')
    for name,(x0,y0,x1,y1) in REG.items():
        svg.append(f'<rect x="{x0*2048}" y="{(1-y1)*2048}" width="{(x1-x0)*2048}" height="{(y1-y0)*2048}" fill="none" stroke="#ffb040" stroke-width="3"/>')
        svg.append(f'<text x="{x0*2048+6}" y="{(1-y1)*2048+22}" fill="#ffb040" font-size="20">{name}</text>')
    svg.append('</svg>')
    report={'input_sha256':hashlib.sha256(raw).hexdigest(),'output_sha256':hashlib.sha256(output).hexdigest(),'only_target_uv_bytes_changed':True,'chest_back_uvs_exact':True,'all_non_uv_binary_bytes_exact':True,'gltf_json_exact':True,'islands_scaled_or_reunwrapped':False,'minimum_changed_island_bbox_padding_px':8,'changed_islands':[]}
    for i in edits:
        report['changed_islands'].append(dict(id=i['id'],mesh_index=i['mi'],primitive_index=i['pi'],triangle_indices=i['tris'],rotation_degrees=i['angle'],translation_px=(i['new'][0]-i['shape'][0]).tolist(),old_bounds_px=box(i['old']*2048).tolist(),new_bounds_px=box(i['new']).tolist()))
    DEST.write_bytes(output)
    (ART/'uniform_uv_layout.svg').write_text('\n'.join(svg))
    (ART/'usability_changes.json').write_text(json.dumps(report,indent=2))
    (ART/'artwork_guides.json').write_text(json.dumps(graphics,indent=2))
    print('UPDATED',len(edits),'existing islands by rotation/translation only')
if __name__=='__main__': main()
