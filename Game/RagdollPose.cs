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
    // Copy only joint rotations from the borrowed clip. Physics owns every world position,
    // and sampling never advances the locomotion clock or skins over the physics pose.
    public static unsafe Func<float, IReadOnlyList<Quaternion>> StruggleTargets(
        Model model, AnimationPlayer gait, Ragdoll ragdoll)
    {
        var names = new Dictionary<string, int>();
        for (int i = 0; i < model.Skeleton.BoneCount; i++) names[new string(model.Skeleton.Bones[i].Name)] = i;
        int count = Math.Max(1, gait.FrameCount - 1);
        var frames = new Quaternion[count][];
        for (int f = 0; f < count; f++)
        {
            frames[f] = new Quaternion[ragdoll.Joints.Count];
            for (int j = 0; j < ragdoll.Joints.Count; j++)
            {
                var joint = ragdoll.Joints[j];
                var parent = ragdoll.Bodies[joint.Parent]; var child = ragdoll.Bodies[joint.Child];
                // Legs keep stepping; upper body keeps its captured carry/wrap brace.
                frames[f][j] = joint.Child >= 7
                    ? Quaternion.Normalize(Quaternion.Inverse(gait.Animation.KeyframePoses[f][names[parent.Bone]].Rotation) *
                        gait.Animation.KeyframePoses[f][names[child.Bone]].Rotation)
                    : Quaternion.Normalize(Quaternion.Inverse(parent.Orientation) * child.Orientation);
            }
        }
        float start = gait.CurrentTime * gait.FramesPerSecond;
        float rate = gait.FramesPerSecond * ragdoll.Config.RagdollGaitPlaybackRate;
        var result = new Quaternion[ragdoll.Joints.Count];
        return seconds =>
        {
            float frame = (start + seconds * rate) % count;
            int a = (int)frame, b = (a + 1) % count;
            for (int j = 0; j < result.Length; j++)
                result[j] = Quaternion.Slerp(frames[a][j], frames[b][j], frame - a);
            return result;
        };
    }

    // Read-only animation sampling for contact detection before physics takes ownership.
    // Reuse the ragdoll layout so detection and post-impact collision use identical shapes.
    public static void RefreshContactPose(AnimationPlayer animation, Matrix4x4 world, Ragdoll probe)
    {
        var pose = new Dictionary<string, Matrix4x4>();
        foreach (string bone in Ragdoll.RequiredBones)
        {
            if (!animation.TryGetBoneTransform(bone, out var transform))
                throw new InvalidDataException($"Missing contact bone: {bone}");
            pose.Add(bone, transform * world);
        }
        probe.Activate(pose, pose, Vector3.Zero);
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
