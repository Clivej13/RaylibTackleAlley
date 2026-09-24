"""Deterministic 2048px atlas artwork. Run with Python + Pillow (no Blender needed)."""
from pathlib import Path
import json, io, zipfile, xml.etree.ElementTree as ET
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Textures/Uniforms'
OUT.mkdir(parents=True, exist_ok=True)
N = 2048
regions = json.loads((ROOT/'Assets/Models/UniformAtlas/atlas_regions.json').read_text())
labels = ['FRONT JERSEY','BACK JERSEY','LEFT SLEEVE','RIGHT SLEEVE','HELMET LEFT','HELMET RIGHT','TROUSERS FRONT','TROUSERS BACK','LEFT SOCK','RIGHT SOCK','HELMET CROWN','UNDERSHIRT','JERSEY TRIM','SOCK UNDERLAY']
colors = ['#102344','#102344','#ed2535','#087bff','#f5f5f5','#f5f5f5','#f5f5f5','#d5d9df','#102344','#ed2535','#ed2535','#090b10','#fff000','#343943']
font_path = 'C:/Windows/Fonts/arialbd.ttf'
def font(size): return ImageFont.truetype(font_path, size)
def rect(r):
    x0,y0,x1,y1=r
    return (round(x0*N),round((1-y1)*N),round(x1*N),round((1-y0)*N))
base=Image.new('RGBA',(N,N),'white')
test=base.copy()
guides=Image.new('RGBA',(N,N))
wire=Image.new('RGBA',(N,N))
art_guides=Image.new('RGBA',(N,N))
bd,td,gd,wd=map(ImageDraw.Draw,(base,test,guides,wire))
for i,((name,r),label,color) in enumerate(zip(regions.items(),labels,colors)):
    x0,y0,x1,y1=rect(r)
    bd.rectangle((x0,y0,x1-1,y1-1),fill=['#e4ebef','#dce4e9'][i%2])
    td.rectangle((x0,y0,x1-1,y1-1),fill=color)
    gd.rectangle((x0+2,y0+2,x1-3,y1-3),outline='#243b50',width=4)
    size=28
    while gd.textlength(label,font=font(size))>x1-x0-32: size-=1
    gd.rectangle((x0+9,y0+9,x1-10,y0+61),fill='#243b50')
    gd.text(((x0+x1)/2,y0+35),label,font=font(size),anchor='mm',fill='white')
    gd.text((x0+16,y1-22),name,font=font(18),fill='#243b50',anchor='lm')
    # Graphics are drawn in image space without changing any UVs.
    if name in ('Chest','Back'):
        td.text(((x0+x1)/2,(y0+y1)/2),'10',font=font(370),anchor='mm',fill='white',stroke_width=2)
    if name=='Helmet_crown' and not (ROOT/'Assets/Models/UniformAtlas/artwork_guides.json').exists():
        mid=(x0+x1)//2
        td.rectangle((mid-21,y0,mid+21,y1-1),fill='white')
guide_path=ROOT/'Assets/Models/UniformAtlas/artwork_guides.json'
if guide_path.exists():
    changes=json.loads((guide_path.parent/'usability_changes.json').read_text())
    boxes={i['id']:i['new_bounds_px'] for i in changes['changed_islands']}
    ad=ImageDraw.Draw(art_guides)
    for g in json.loads(guide_path.read_text()):
        center=g['center_px']; half=g['width_px']/2; lo,hi=g['span_px']
        if g['kind']=='vertical':
            b=[center-half,lo,center+half,hi]; line=[(center,N-hi),(center,N-lo)]
        else:
            b=[lo,center-half,hi,center+half]; line=[(lo,N-center),(hi,N-center)]
        if 'island' in g:
            clip=boxes[g['island']]; b=[max(b[0],clip[0]-3),max(b[1],clip[1]-3),min(b[2],clip[2]+3),min(b[3],clip[3]+3)]
        td.rectangle((round(b[0]),round(N-b[3]),round(b[2]),round(N-b[1])),fill='#102344' if g['region'].startswith('Trousers') or g['region']=='Helmet_crown' else 'white')
        ad.line(line,fill=(0,115,160,160),width=2)
    for entry in changes['changed_islands']:
        x0,y0,x1,y1=entry['new_bounds_px']
        ad.text(((x0+x1)/2,N-(y0+y1)/2),entry['id'].rsplit('_',1)[1],font=font(15),anchor='mm',fill=(0,85,125,200))
svg=ET.parse(ROOT/'Assets/Models/UniformAtlas/uniform_uv_layout.svg')
for p in svg.findall('.//{http://www.w3.org/2000/svg}polygon'):
    points=[tuple(map(float,v.split(','))) for v in p.attrib['points'].split()]
    wd.line(points+[points[0]],fill=(50,70,85,65),width=1)
merged=Image.alpha_composite(Image.alpha_composite(Image.alpha_composite(base,wire),art_guides),guides)
for im,name in [(base,'uniform_paint_base'),(wire,'uniform_uv_overlay'),(guides,'uniform_labels_overlay'),(art_guides,'uniform_artwork_guides'),(merged,'uniform_template'),(test,'test_uniform')]:
    im.save(OUT/(name+'.png'))
# OpenRaster layers are supported by Krita/GIMP; standalone overlays also work in Photoshop.
layers=[('Labels and region boundaries — hide for export',guides),('Straight stripe placement guides — hide for export',art_guides),('UV wireframe — hide for export',wire),('Paint base — edit or replace',base)]
root=ET.Element('image',w=str(N),h=str(N),name='Uniform texture template',version='0.0.3')
stack=ET.SubElement(root,'stack')
with zipfile.ZipFile(OUT/'uniform_template.ora','w') as z:
    z.writestr('mimetype','image/openraster',compress_type=zipfile.ZIP_STORED)
    for i,(name,im) in enumerate(layers):
        path=f'data/layer{i}.png'; b=io.BytesIO(); im.save(b,format='PNG'); z.writestr(path,b.getvalue())
        ET.SubElement(stack,'layer',name=name,src=path,opacity='1.0',visibility='visible',x='0',y='0', **{'composite-op':'svg:src-over'})
    z.writestr('stack.xml',ET.tostring(root,encoding='utf-8'))
    b=io.BytesIO(); merged.save(b,format='PNG'); z.writestr('mergedimage.png',b.getvalue())
    thumb=merged.copy(); thumb.thumbnail((256,256)); b=io.BytesIO(); thumb.save(b,format='PNG'); z.writestr('Thumbnails/thumbnail.png',b.getvalue())
print('Created uniform template, layers, OpenRaster source and validation texture:',OUT)
