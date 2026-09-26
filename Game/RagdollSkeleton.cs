using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

/// <summary>Per-activation bridge between world-space bodies and Raylib's absolute
/// model-space skinning poses. Owns only managed snapshots, never model/clip memory.</summary>
public sealed class RagdollSkeleton
{
    private readonly int[] _parents, _bodyIndices, _order;
    private readonly Matrix4x4[] _offsets, _modelPose;
    private readonly Transform[] _nativePose;
    private readonly Matrix4x4 _inverseWorld;
    private bool _needsAffineSkinning;
    private readonly bool _sizedWorld;
    public Matrix4x4 ModelWorld { get; }
    public IReadOnlyList<int> BodyIndices { get; }
    public IReadOnlyList<Matrix4x4> ModelPose { get; }

    public RagdollSkeleton(IReadOnlyList<string> names, IReadOnlyList<int> parents,
        IReadOnlyList<Matrix4x4> animatedModelPose, Matrix4x4 modelWorld, Ragdoll ragdoll)
    {
        int count = names.Count;
        if (!ragdoll.IsActive || count == 0 || parents.Count != count || animatedModelPose.Count != count ||
            names.Distinct().Count() != count || !Matrix4x4.Invert(modelWorld, out _inverseWorld))
            throw new ArgumentException("A complete skeleton, invertible world transform and active ragdoll are required.");
        ModelWorld = modelWorld;
        float sx = new Vector3(modelWorld.M11, modelWorld.M12, modelWorld.M13).Length();
        float sy = new Vector3(modelWorld.M21, modelWorld.M22, modelWorld.M23).Length();
        float sz = new Vector3(modelWorld.M31, modelWorld.M32, modelWorld.M33).Length();
        _sizedWorld = Math.Abs(sx - sy) > .000001f || Math.Abs(sz - sy) > .000001f;
        _parents = parents.ToArray();
        _bodyIndices = Enumerable.Repeat(-1, count).ToArray();
        _offsets = new Matrix4x4[count]; _modelPose = new Matrix4x4[count]; _nativePose = new Transform[count];
        var byName = names.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index);
        for (int b = 0; b < ragdoll.Bodies.Count; b++)
        {
            if (!byName.TryGetValue(ragdoll.Bodies[b].Bone, out int bone))
                throw new ArgumentException($"Missing mapped bone: {ragdoll.Bodies[b].Bone}");
            _bodyIndices[bone] = b;
        }
        // Root is an ancestor of Hips, so it cannot inherit from Hips through the hierarchy.
        // Give it the same rigid-body delta with its own captured offset.
        if (byName.TryGetValue("Root", out int root)) _bodyIndices[root] = 0;
        var order = new List<int>();
        var visited = new byte[count];
        void Visit(int i)
        {
            if (visited[i] == 2) return;
            if (visited[i] == 1) throw new ArgumentException("Cyclic skeleton.");
            visited[i] = 1;
            int parent = _parents[i];
            if (parent < -1 || parent >= count) throw new ArgumentException("Invalid skeleton parent.");
            if (parent >= 0) Visit(parent);
            visited[i] = 2; order.Add(i);
        }
        for (int i = 0; i < count; i++) Visit(i);
        _order = order.ToArray();
        for (int i = 0; i < count; i++)
        {
            if (_bodyIndices[i] >= 0)
            {
                Matrix4x4.Invert(BodyWorld(ragdoll.Bodies[_bodyIndices[i]]), out var inverseBody);
                _offsets[i] = animatedModelPose[i] * modelWorld * inverseBody;
            }
            else if (_parents[i] >= 0)
            {
                if (!Matrix4x4.Invert(animatedModelPose[_parents[i]], out var inverseParent))
                    throw new ArgumentException("Singular parent pose.");
                _offsets[i] = animatedModelPose[i] * inverseParent;
            }
            else _offsets[i] = animatedModelPose[i];
        }
        BodyIndices = Array.AsReadOnly(_bodyIndices);
        ModelPose = Array.AsReadOnly(_modelPose);
        Evaluate(ragdoll);
    }

    public static Matrix4x4 BodyWorld(RagdollBody body) =>
        Matrix4x4.CreateFromQuaternion(body.Orientation) * Matrix4x4.CreateTranslation(body.Position);

    public void Evaluate(Ragdoll ragdoll)
    {
        _needsAffineSkinning = _sizedWorld;
        foreach (int i in _order)
        {
            int body = _bodyIndices[i];
            _modelPose[i] = body >= 0
                ? _offsets[i] * BodyWorld(ragdoll.Bodies[body]) * _inverseWorld
                : _parents[i] >= 0 ? _offsets[i] * _modelPose[_parents[i]] : _offsets[i];
            if (!Matrix4x4.Decompose(_modelPose[i], out var scale, out var rotation, out var translation))
            {
                _needsAffineSkinning = true;
                if (!AffinePose.Decompose(_modelPose[i], out scale, out rotation, out translation))
                    throw new InvalidOperationException("Ragdoll produced an invalid bone pose.");
            }
            _nativePose[i] = new Transform {
                Translation = translation, Rotation = Quaternion.Normalize(rotation), Scale = scale
            };
        }
    }

    public unsafe void Apply(Model model, Ragdoll ragdoll)
    {
        if (!ragdoll.IsActive) throw new InvalidOperationException("Animation owns an inactive ragdoll.");
        if (model.Skeleton.BoneCount != _nativePose.Length) throw new ArgumentException("Skeleton mismatch.");
        Evaluate(ragdoll);
        if (_needsAffineSkinning) AffinePose.Apply(model, _modelPose);
        else ApplyPose(model, _nativePose);
    }

    public static unsafe void ApplyPose(Model model, Transform[] nativePose)
    {
        // Raylib 6 accepts absolute model-space TRS, not parent-local matrices.
        // Two identical frames avoid interpolation and single-frame edge cases. Native
        // skinning consumes these pointers synchronously; no borrowed clip is modified.
        fixed (Transform* pose = nativePose)
        {
            Transform** frames = stackalloc Transform*[2];
            frames[0] = frames[1] = pose;
            // Raylib-cs exposes the counts as readonly without a constructor for custom
            // poses. Initialise only this stack-local interop value, never a loaded asset.
            var animation = new ModelAnimation { KeyframePoses = frames };
            System.Runtime.CompilerServices.Unsafe.AsRef(in animation.BoneCount) = nativePose.Length;
            System.Runtime.CompilerServices.Unsafe.AsRef(in animation.KeyFrameCount) = 2;
            Raylib.UpdateModelAnimation(model, animation, 0f);
        }
    }

    public void Draw(Model model)
    {
        // Only this draw-local copy changes. Transpose at the native Matrix boundary;
        // all preceding math is System.Numerics row-vector math.
        model.Transform = Matrix4x4.Transpose(ModelWorld);
        Raylib.DrawModelEx(model, Vector3.Zero, Vector3.UnitY, 0f, Vector3.One, Color.White);
    }
}
