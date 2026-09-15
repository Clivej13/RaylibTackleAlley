"""Shared ball-free pursuit arm carriage, in the rig's Y-up coordinates."""
import bpy, math
from mathutils import Vector

def arms(rig, side, sign, phase, running):
    bpy.context.view_layer.update()
    chest = rig.pose.bones['Chest'].matrix @ rig.data.bones['Chest'].matrix_local.inverted()
    swing = -(30 if running else 22) * math.cos(phase)
    bend = (78 if running else 68) + 6 * math.cos(phase)
    def aim(name, direction):
        p = rig.pose.bones[name + '.' + side]
        rest = p.bone
        turn = (rest.tail_local-rest.head_local).rotation_difference(Vector(direction))
        matrix = (chest.to_3x3() @ turn.to_matrix() @ rest.matrix_local.to_3x3()).to_4x4()
        matrix.translation = p.head
        p.matrix = matrix
        bpy.context.view_layer.update()
    a = math.radians(swing)
    b = math.radians(swing+bend)
    aim('UpperArm', (sign*.23, -math.cos(a), -math.sin(a)))
    aim('LowerArm', (-sign*.045, -math.cos(b), -math.sin(b)))
    aim('Hand', (-sign*.045, -math.cos(b), -math.sin(b)))
