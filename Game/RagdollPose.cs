using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.ThreeD;

namespace RaylibTackleAlley.Game;

public static class RagdollPose
{
    public static Matrix4x4 Matrix(Transform t) => Matrix4x4.CreateScale(t.Scale) *
        Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Translation);
    public static Transform Transform(Matrix4x4 m)
    {
        if (!Matrix4x4.Decompose(m, out var s, out var q, out var p)) throw new InvalidDataException("Invalid bone pose.");
        return new() { Scale = s, Rotation = Quaternion.Normalize(q), Translation = p };
    }
    public static unsafe Matrix4x4[] Snapshot(Model model, AnimationPlayer animation)
    {
        var result = new Matrix4x4[model.Skeleton.BoneCount];
        for (int i = 0; i < result.Length; i++)
            if (!animation.TryGetBoneTransform(new string(model.Skeleton.Bones[i].Name), out result[i]))
                throw new InvalidDataException("Missing animated bone.");
        return result;
    }
    public static unsafe RagdollSkeleton Activate(Model model, AnimationPlayer animation, Matrix4x4 world,
        Ragdoll ragdoll, Vector3 velocity, RagdollImpulse? impulse, float ground)
    {
        var s = model.Skeleton;
        var current = Snapshot(model, animation);
        var names = new string[s.BoneCount]; var parents = new int[s.BoneCount];
        var pose = new Dictionary<string, Matrix4x4>(); var reference = new Dictionary<string, Matrix4x4>();
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = new string(s.Bones[i].Name); parents[i] = s.Bones[i].Parent;
            pose.Add(names[i], current[i] * world);
            reference.Add(names[i], Matrix(s.BindPose[i]) * world);
        }
        ragdoll.Activate(pose, reference, velocity, impulse, ground);
        try { return new(names, parents, current, world, ragdoll); }
        catch { ragdoll.Deactivate(); throw; }
    }
}
