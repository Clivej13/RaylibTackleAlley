using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.ThreeD;

namespace RaylibTackleAlley.Game;

public enum RecoveryPhase { Down, GetUp, Complete }

/// <summary>Shared, stationary recovery playback with a parent-local pose blend from settled physics.</summary>
public sealed class RagdollRecovery
{
    private readonly Model _model;
    private readonly AnimationPlayer _down, _getUp;
    private readonly int[] _parents;
    private readonly Transform[] _startLocal, _native;
    private readonly float _downDuration, _blendDuration;
    private float _elapsed;
    public RecoveryPhase Phase { get; private set; } = RecoveryPhase.Down;
    public Matrix4x4 World { get; }
    public Matrix4x4[] ModelPose { get; }
    public Vector3 Position { get; }
    public float YawDegrees { get; }
    public AnimationPlayer Animation => Phase == RecoveryPhase.Down ? _down : _getUp;

    public unsafe RagdollRecovery(Model model, RagdollSkeleton skeleton, Ragdoll ragdoll,
        AnimationPlayer down, AnimationPlayer getUp, float scale, float groundOffset,
        float previousYaw, TackleAlleyConfig config)
    {
        config.ValidateRagdollRecovery();
        _model = model; _down = down; _getUp = getUp;
        _downDuration = config.RagdollDownDuration; _blendDuration = config.RagdollDownBlendDuration;
        skeleton.Evaluate(ragdoll);
        var settled = skeleton.ModelPose.Select(p => p * skeleton.ModelWorld).ToArray();
        _parents = new int[settled.Length];
        int hips = -1, chest = -1;
        for (int i = 0; i < _parents.Length; i++)
        {
            _parents[i] = model.Skeleton.Bones[i].Parent;
            if (_parents[i] >= i) throw new InvalidDataException("Recovery requires parent-first skeleton order.");
            string name = new(model.Skeleton.Bones[i].Name);
            if (name == "Hips") hips = i;
            if (name == "Chest") chest = i;
        }
        if (hips < 0 || chest < 0) throw new InvalidDataException("Missing torso.");
        down.SeekTime(0);
        var target = RagdollPose.Snapshot(model, down);
        Vector3 desired = settled[chest].Translation - settled[hips].Translation;
        Vector3 authored = target[chest].Translation - target[hips].Translation;
        desired.Y = authored.Y = 0;
        float yaw = desired.LengthSquared() > .001f && authored.LengthSquared() > .001f
            ? MathF.Atan2(desired.X, desired.Z) - MathF.Atan2(authored.X, authored.Z)
            : previousYaw * MathF.PI / 180f;
        YawDegrees = yaw * 180 / MathF.PI;
        var basis = Matrix4x4.Transpose(model.Transform) * Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(yaw);
        Vector3 pelvisOffset = Vector3.Transform(target[hips].Translation, basis);
        Position = new(settled[hips].Translation.X - pelvisOffset.X, ragdoll.GroundHeight,
            settled[hips].Translation.Z - pelvisOffset.Z);
        World = basis * Matrix4x4.CreateTranslation(Position + Vector3.UnitY * groundOffset);
        Matrix4x4.Invert(World, out var inverseWorld);
        var start = settled.Select(p => p * inverseWorld).ToArray();
        _startLocal = new Transform[start.Length]; _native = new Transform[start.Length];
        ModelPose = new Matrix4x4[start.Length];
        for (int i = 0; i < start.Length; i++) _startLocal[i] = RagdollPose.Transform(Local(start, i));
        Evaluate(); // blend weight zero: exact settled pose, even on the first Down draw
    }

    private Matrix4x4 Local(Matrix4x4[] pose, int i)
    {
        if (_parents[i] < 0) return pose[i];
        Matrix4x4.Invert(pose[_parents[i]], out var inverse);
        return pose[i] * inverse;
    }

    public void Update(float dt)
    {
        dt = Math.Max(0, dt);
        while (Phase != RecoveryPhase.Complete)
        {
            float duration = Phase == RecoveryPhase.Down ? _downDuration :
                (_getUp.FrameCount - 1) / _getUp.FramesPerSecond;
            float step = Math.Min(dt, Math.Max(0, duration - _elapsed));
            Animation.Update(step); _elapsed += step; dt -= step;
            if (_elapsed + .000001f < duration) break;
            if (Phase == RecoveryPhase.Down) { Phase = RecoveryPhase.GetUp; _elapsed = 0; _getUp.SeekTime(0); }
            else { Phase = RecoveryPhase.Complete; break; }
            if (dt <= 0) break;
        }
        Evaluate();
    }

    private void Evaluate()
    {
        var target = RagdollPose.Snapshot(_model, Animation);
        float blend = Phase == RecoveryPhase.Down ? Math.Clamp(_elapsed / _blendDuration, 0, 1) : 1;
        blend = blend * blend * (3 - 2 * blend);
        for (int i = 0; i < target.Length; i++)
        {
            var end = RagdollPose.Transform(Local(target, i));
            var start = _startLocal[i];
            var t = new Transform {
                Translation = Vector3.Lerp(start.Translation, end.Translation, blend),
                Rotation = Quaternion.Slerp(start.Rotation, end.Rotation, blend),
                Scale = Vector3.Lerp(start.Scale, end.Scale, blend)
            };
            ModelPose[i] = RagdollPose.Matrix(t) * (_parents[i] >= 0 ? ModelPose[_parents[i]] : Matrix4x4.Identity);
            _native[i] = RagdollPose.Transform(ModelPose[i]);
        }
    }
    public void Draw()
    {
        RagdollSkeleton.ApplyPose(_model, _native);
        var model = _model; model.Transform = Matrix4x4.Transpose(World);
        Raylib.DrawModelEx(model, Vector3.Zero, Vector3.UnitY, 0, Vector3.One, Color.White);
    }
}
