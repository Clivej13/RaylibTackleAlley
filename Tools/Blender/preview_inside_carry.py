"""Render four-phase carry inspections in Blender; never save changes to assets."""
from pathlib import Path
import shutil
import sys
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_football import look

OUT = ROOT / 'Assets/Models'
PRE = OUT / 'CarryPreviews'
HAND = OUT / 'HandRigPreviews'


def main():
    for clip in ('Jog', 'Run', 'Sprint'):
        bpy.ops.wm.open_mainfile(filepath=str(OUT / ('football_player_carry_' + clip.lower() + '.blend')))
        scene = bpy.context.scene
        end = int(bpy.data.objects['PlayerRig'].animation_data.action.frame_range[1])
        frames = [1+i*(end-1)//4 for i in range(4)]
        scene.render.resolution_x = 480
        scene.render.resolution_y = 480
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = 'PNG'
        scene.eevee.taa_render_samples = 64
        scene.camera.data.ortho_scale = .92
        pixels = np.zeros((960, 1920, 4), dtype=np.float32)
        for row, location in enumerate(((-2.4, 1.7, -3.4), (-4, 1.5, -.1))):
            scene.camera.location = location
            look(scene.camera, (0, 1.29, -.03))
            for column, frame in enumerate(frames):
                scene.frame_set(frame)
                path = PRE / '_inside_render.png'
                scene.render.filepath = str(path)
                bpy.ops.render.render(write_still=True)
                loaded = bpy.data.images.load(str(path), check_existing=False)
                data = np.empty(480*480*4, dtype=np.float32)
                loaded.pixels.foreach_get(data)
                pixels[(1-row)*480:(2-row)*480, column*480:(column+1)*480] = data.reshape((480, 480, 4))
                bpy.data.images.remove(loaded)
                path.unlink()
        sheet = bpy.data.images.new(clip + 'InsideInspection', width=1920, height=960, alpha=True)
        sheet.pixels.foreach_set(pixels.ravel())
        sheet.filepath_raw = str(PRE / ('Carry' + clip + '_inside_inspection.png'))
        sheet.file_format = 'PNG'
        sheet.save()
        for frame in frames:
            filename = 'Carry' + clip + '_' + str(frame).zfill(2) + '.png'
            shutil.copyfile(PRE / filename, HAND / filename)
        shutil.copyfile(PRE / ('Carry' + clip + '_loop.mp4'), HAND / ('Carry' + clip + '_loop.mp4'))
        if clip == 'Jog':
            scene.frame_set(1)
            center = bpy.data.objects['FootballPreview'].matrix_world.translation
            scene.camera.location = center + Vector((-.32, .22, -.40))
            look(scene.camera, center)
            scene.camera.data.ortho_scale = .39
            scene.render.resolution_x = 720
            scene.render.resolution_y = 720
            scene.render.filepath = str(HAND / 'grip_R.png')
            bpy.ops.render.render(write_still=True)
            shutil.copyfile(HAND / 'grip_R.png', HAND / 'grip_fit.png')
        print('INSIDE_PREVIEWS_COMPLETE', clip)


if __name__ == '__main__':
    main()
