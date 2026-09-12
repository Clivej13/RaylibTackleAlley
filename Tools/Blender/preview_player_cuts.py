"""Render cut phases and one-shot videos with a Blender-only floor reference."""
from pathlib import Path
import sys
sys.dont_write_bytecode = True
import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_football import look
from create_player_cut_animations import CONFIG, OUT, PRE


def floor():
    mat = bpy.data.materials.new('CutPreviewFloor')
    mat.diffuse_color = (.075,.09,.10,1)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0,-.008,0))
    obj = bpy.context.object
    obj.name = '_CutPreviewFloor'
    obj.scale = (6,.01,6)
    obj.data.materials.append(mat)
    line = bpy.data.materials.new('CutPreviewGrid')
    line.diffuse_color = (.19,.22,.23,1)
    for axis in (0,2):
        for i in range(-6,7):
            loc = [0,-.0025,0]
            loc[axis] = i*.25
            bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
            obj = bpy.context.object
            obj.name = '_CutPreviewGrid'
            obj.scale = (.002,.0005,3) if axis == 0 else (3,.0005,.002)
            obj.data.materials.append(line)


def main():
    PRE.mkdir(exist_ok=True)
    for name in CONFIG:
        bpy.ops.wm.open_mainfile(filepath=str(OUT / ('football_player_'+name.replace('Cut','cut_').lower()+'.blend')))
        scene = bpy.context.scene
        floor()
        scene.render.engine = 'BLENDER_EEVEE'
        scene.eevee.taa_render_samples = 48
        scene.render.resolution_x = 480
        scene.render.resolution_y = 540
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = 'PNG'
        camera = scene.camera
        camera.data.ortho_scale = 2.15
        sheet_pixels = np.zeros((1080,2880,4), dtype=np.float32)
        for row,(view,location) in enumerate((('Front',(0,1.5,-4)),('Rear',(-2.5,1.8,4)))):
            camera.location = location
            look(camera, (0,.88,0))
            for column,(frame,phase) in enumerate(((1,'Approach'),(7,'Plant'),(9,'Load'),(13,'Push'),(17,'Drive'),(23,'Recover'))):
                scene.frame_set(frame)
                path = PRE / (name+'_'+phase+'_'+view+'.png')
                scene.render.filepath = str(path)
                bpy.ops.render.render(write_still=True)
                image = bpy.data.images.load(str(path), check_existing=False)
                pixels = np.empty(480*540*4, dtype=np.float32)
                image.pixels.foreach_get(pixels)
                sheet_pixels[(1-row)*540:(2-row)*540, column*480:(column+1)*480] = pixels.reshape((540,480,4))
                bpy.data.images.remove(image)
        image = bpy.data.images.new(name+'Phases', width=2880, height=1080, alpha=True)
        image.pixels.foreach_set(sheet_pixels.ravel())
        image.filepath_raw = str(PRE / (name+'_phases.png'))
        image.file_format = 'PNG'
        image.save()
        camera.location = (-3.0,1.8,-4.0)
        look(camera, (0,.88,0))
        scene.render.resolution_x, scene.render.resolution_y = 640,720
        scene.frame_set(13)
        scene.render.filepath = str(PRE / (name+'_Push_ThreeQuarter.png'))
        bpy.ops.render.render(write_still=True)
        camera.data.ortho_scale = .95
        look(camera, (0,1.24,0))
        scene.render.filepath = str(PRE / (name+'_Tuck_Push.png'))
        bpy.ops.render.render(write_still=True)
        camera.data.ortho_scale = 2.15
        look(camera, (0,.88,0))
        scene.frame_start, scene.frame_end = 1,23
        scene.render.image_settings.file_format = 'FFMPEG'
        scene.render.ffmpeg.format = 'MPEG4'
        scene.render.ffmpeg.codec = 'H264'
        scene.render.ffmpeg.constant_rate_factor = 'HIGH'
        for fps,label in ((60,'preview'),(20,'slow')):
            scene.render.fps = fps
            scene.render.filepath = str(PRE / (name+'_'+label+'.mp4'))
            bpy.ops.render.render(animation=True)
        print('CUT_PREVIEWS_COMPLETE', name)


if __name__ == '__main__':
    main()
