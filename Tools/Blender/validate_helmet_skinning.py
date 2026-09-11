"""Shared Blender and GLB checks for separate, rigid Head-skinned equipment."""
import json
import struct

EQUIPMENT = ('Helmet', 'Facemask', 'Visor', 'ChinStrap')


def head_relative_vertices(rig, depsgraph):
    import bpy
    result = {}
    inv = (rig.matrix_world @ rig.pose.bones['Head'].matrix).inverted()
    for name in EQUIPMENT:
        obj = bpy.data.objects[name]
        assert obj.type == 'MESH' and obj.parent == rig and obj.parent_type == 'OBJECT', name
        assert [g.name for g in obj.vertex_groups] == ['Head'], name
        assert all(len(v.groups) == 1 and v.groups[0].weight == 1.0 for v in obj.data.vertices), name
        mods = [m for m in obj.modifiers if m.type == 'ARMATURE']
        assert len(mods) == 1 and mods[0].object == rig and not mods[0].use_deform_preserve_volume, name
        evaluated = obj.evaluated_get(depsgraph)
        result[name] = [inv @ (evaluated.matrix_world @ v.co) for v in evaluated.data.vertices]
    return result


def validate_glb(path):
    raw = path.read_bytes()
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + size])
    binary = raw[28 + size:]

    def accessor(index):
        a = doc['accessors'][index]
        view = doc['bufferViews'][a['bufferView']]
        assert not a.get('sparse') and not a.get('normalized')
        code = {5121: 'B', 5123: 'H', 5125: 'I', 5126: 'f'}[a['componentType']]
        width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
        fmt = '<' + code * width
        offset = view.get('byteOffset', 0) + a.get('byteOffset', 0)
        stride = view.get('byteStride', struct.calcsize(fmt))
        return [struct.unpack_from(fmt, binary, offset + i * stride) for i in range(a['count'])]

    result = {}
    mesh_ids = []
    for name in EQUIPMENT:
        matches = [n for n in doc['nodes'] if n.get('name') == name and 'mesh' in n]
        assert len(matches) == 1, name
        node = matches[0]
        assert 'skin' in node, name
        mesh_ids.append(node['mesh'])
        joints = doc['skins'][node['skin']]['joints']
        count = 0
        for primitive in doc['meshes'][node['mesh']]['primitives']:
            attrs = primitive['attributes']
            assert 'JOINTS_0' in attrs and 'WEIGHTS_0' in attrs, name
            weights = accessor(attrs['WEIGHTS_0'])
            indices = accessor(attrs['JOINTS_0'])
            assert len(weights) == len(indices) == len(accessor(attrs['POSITION']))
            for js, ws in zip(indices, weights):
                active = [(doc['nodes'][joints[j]]['name'], w) for j, w in zip(js, ws) if w != 0]
                assert active == [('Head', 1.0)], (name, active)
            for key, index in attrs.items():
                if key.startswith('WEIGHTS_') and key != 'WEIGHTS_0':
                    assert all(all(w == 0 for w in row) for row in accessor(index)), name
            count += len(weights)
        result[name] = count
    assert len(set(mesh_ids)) == 4
    return result
