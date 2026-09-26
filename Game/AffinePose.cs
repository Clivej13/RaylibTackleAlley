using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

// Nonuniform player proportions followed by bone rotation can create shear.
// Physics needs a rigid orientation; rendering keeps the original affine matrix.
public static class AffinePose
{
    public static bool Decompose(Matrix4x4 m, out Vector3 scale, out Quaternion rotation, out Vector3 position)
    {
        if (Matrix4x4.Decompose(m, out scale, out rotation, out position)) return true;
        position = m.Translation;
        Vector3 x = new(m.M11, m.M12, m.M13), y = new(m.M21, m.M22, m.M23), z = new(m.M31, m.M32, m.M33);
        scale = new(x.Length(), y.Length(), z.Length());
        rotation = Quaternion.Identity;
        if (!float.IsFinite(scale.LengthSquared()) || Math.Min(scale.X, Math.Min(scale.Y, scale.Z)) < 1e-6f) return false;
        x = Vector3.Normalize(x);
        y -= x * Vector3.Dot(y, x);
        if (y.LengthSquared() < 1e-12f) return false;
        y = Vector3.Normalize(y);
        z = Vector3.Normalize(Vector3.Cross(x, y));
        rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new(
            x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        return float.IsFinite(position.LengthSquared()) && m.GetDeterminant() > 0;
    }

    // Exact affine skinning for sized ragdolls. Native TRS cannot represent shear.
    // Only per-instance animated buffers are written; bind vertices/clips stay shared.
    public static unsafe void Apply(Model model, IReadOnlyList<Matrix4x4> pose)
    {
        var skin = new Matrix4x4[pose.Count];
        var normals = new Matrix4x4[pose.Count];
        for (int i = 0; i < pose.Count; i++)
        {
            Matrix4x4.Invert(RagdollPose.Matrix(model.Skeleton.BindPose[i]), out var inverseBind);
            skin[i] = inverseBind * pose[i];
            Matrix4x4.Invert(skin[i], out var inverseSkin);
            normals[i] = Matrix4x4.Transpose(inverseSkin);
        }
        for (int m = 0; m < model.MeshCount; m++)
        {
            var mesh = model.Meshes[m];
            if (mesh.BoneIndices == null || mesh.BoneWeights == null || mesh.AnimVertices == null) continue;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                int p = v * 3;
                Vector3 source = new(mesh.Vertices[p], mesh.Vertices[p + 1], mesh.Vertices[p + 2]);
                Vector3 normal = mesh.Normals == null ? Vector3.UnitY : new(mesh.Normals[p], mesh.Normals[p + 1], mesh.Normals[p + 2]);
                Vector3 point = Vector3.Zero, direction = Vector3.Zero;
                float total = 0;
                for (int influence = 0; influence < 4; influence++)
                {
                    int slot = v * 4 + influence, bone = mesh.BoneIndices[slot];
                    float weight = mesh.BoneWeights[slot];
                    if (weight <= 0 || bone >= skin.Length) continue;
                    point += Vector3.Transform(source, skin[bone]) * weight;
                    direction += Vector3.TransformNormal(normal, normals[bone]) * weight;
                    total += weight;
                }
                if (total == 0) { point = source; direction = normal; }
                if (direction.LengthSquared() > 0) direction = Vector3.Normalize(direction);
                mesh.AnimVertices[p] = point.X; mesh.AnimVertices[p + 1] = point.Y; mesh.AnimVertices[p + 2] = point.Z;
                if (mesh.AnimNormals != null)
                {
                    mesh.AnimNormals[p] = direction.X; mesh.AnimNormals[p + 1] = direction.Y; mesh.AnimNormals[p + 2] = direction.Z;
                }
            }
            Raylib.UpdateMeshBuffer(mesh, 0, mesh.AnimVertices, mesh.VertexCount * 3 * sizeof(float), 0);
            if (mesh.AnimNormals != null)
                Raylib.UpdateMeshBuffer(mesh, 2, mesh.AnimNormals, mesh.VertexCount * 3 * sizeof(float), 0);
        }
    }
}
