using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.ThreeD;

namespace RaylibTackleAlley.Game;

public sealed partial class BallCarrier
{
    public Ragdoll Ragdoll { get; } = new();
    private RagdollSkeleton? _ragdollSkeleton;
    private RagdollRecovery? _recovery;
    private AnimationPlayer? _downAnimation, _getUpAnimation;
    public bool IsRecovering => _recovery is not null;
    public Vector3 Velocity { get; private set; }
    public bool HasTackleGroundImpact { get; private set; }
    public bool CanActivateRagdoll => _model is not null && _animation is not null && !Ragdoll.IsActive && !IsRecovering;

    public bool ActivateRagdoll()
    {
        if (!CanActivateRagdoll || _model is null || _animation is null) return false;
        _ragdollSkeleton = RagdollPose.Activate(_model.Model, _animation, PlayerWorldTransform,
            Ragdoll, Velocity, null, 0);
        _jukeRemaining = _spinRemaining = _cutRemaining = _spinGestureRemaining = _cutReversalRemaining = 0;
        _cutName = null;
        HasTackleGroundImpact = false;
        UpdatePhysicsFootball();
        return true;
    }

    public void BeginTackleStruggle(int? hitBody = null)
    {
        if (_model is not null && _animations.TryGetValue("CarryRun", out var gait))
            Ragdoll.StartActiveDrive(RagdollPose.StruggleTargets(_model.Model, gait, Ragdoll), hitBody);
    }

    // Recovery clips are already loaded for defenders during normal game startup.
    // Load players lazily so existing carrier-only consumers need not request these assets.
    private void EnsureRecoveryAnimations()
    {
        if (_downAnimation is not null || _model is null || _visualAssets is null) return;
        _visualAssets.RequireAssets("FootballPlayerDownAnimations", "FootballPlayerGetUpAnimations");
        while (!_visualAssets.ProcessNext()) { }
        foreach (var name in new[] { "Down", "GetUp" })
        {
            var clips = _visualAssets.GetModelAnimations("FootballPlayer" + name + "Animations");
            unsafe {
                var clip = clips.ToArray().Single(c => new string(c.Name) == name);
                var player = new AnimationPlayer(_model, clip, loop: name == "Down");
                if (name == "Down") _downAnimation = player; else _getUpAnimation = player;
            }
        }
    }
    private RaylibGameFramework.Assets.AssetManager? _visualAssets;

    public bool UpdatePhysicsAndRecovery(float dt)
    {
        if (Ragdoll.IsActive)
        {
            Ragdoll.Update(Math.Max(0, dt));
            HasTackleGroundImpact |= Ragdoll.HasMeaningfulGroundContact;
            if (_ragdollSkeleton is not null)
            {
                _ragdollSkeleton.Evaluate(Ragdoll);
                Position = Ragdoll.Bodies[0].Position;
                UpdatePhysicsFootball();
                if (Ragdoll.State == RagdollState.Settled && _model is not null)
                {
                    EnsureRecoveryAnimations();
                    _recovery = new(_model.Model, _ragdollSkeleton, Ragdoll, _downAnimation!, _getUpAnimation!,
                        _visualScale, _groundOffset, VisualYawDegrees, _config);
                    Position = _recovery.Position;
                    _currentRunYaw = _targetRunYaw = -_recovery.YawDegrees;
                    CurrentForwardSpeed = 0; Velocity = Vector3.Zero;
                    Ragdoll.Deactivate(); _ragdollSkeleton = null;
                    UpdatePhysicsFootball();
                }
            }
            return true;
        }
        if (_recovery is null) return false;
        _recovery.Update(dt);
        UpdatePhysicsFootball();
        if (_recovery.Phase == RecoveryPhase.Complete)
        {
            _recovery = null; SelectAnimation();
            if (_model is not null && _animation is not null)
                Raylib.UpdateModelAnimation(_model.Model, _animation.Animation, _animation.CurrentFrame);
            UpdateFootballAttachment();
        }
        return true;
    }

    private unsafe void UpdatePhysicsFootball()
    {
        if (_model is null) return;
        var pose = _ragdollSkeleton?.ModelPose ?? (IReadOnlyList<Matrix4x4>?)_recovery?.ModelPose;
        if (pose is null) return;
        var world = _ragdollSkeleton?.ModelWorld ?? _recovery!.World;
        for (int i = 0; i < _model.Model.Skeleton.BoneCount; i++)
            if (new string(_model.Model.Skeleton.Bones[i].Name) == CarryHandBone)
            {
                _footballWorldTransform = FootballGripLocal * pose[i] * world;
                return;
            }
        throw new InvalidDataException("Missing ragdoll carrying hand.");
    }
}
