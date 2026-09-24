using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

public sealed partial class Opponent
{
    public Ragdoll Ragdoll { get; }
    private readonly Ragdoll _contactPose;

    public bool HasBodyContact(BallCarrier carrier)
    {
        if (IsRecovering || carrier.IsRecovering) return false;
        // Only a broad-phase rejection; distance alone never confirms body contact.
        if (Vector3.DistanceSquared(_position, carrier.Position) > _config.ContactBroadPhaseDistance * _config.ContactBroadPhaseDistance) return false;
        Ragdoll defenderPose = Ragdoll;
        if (!Ragdoll.IsActive)
        {
            if (_model is null || _animation is null) return false;
            var world = Matrix4x4.Transpose(_model.Model.Transform) * Matrix4x4.CreateScale(_visualScale) *
                Matrix4x4.CreateRotationY(_yawDegrees * MathF.PI / 180f) *
                Matrix4x4.CreateTranslation(_position + Vector3.UnitY * _groundOffset);
            RagdollPose.RefreshContactPose(_animation, world, _contactPose);
            defenderPose = _contactPose;
        }
        return carrier.ContactPose() is { } playerPose && RagdollContact.Overlaps(defenderPose, playerPose);
    }
    private RagdollSkeleton? _ragdollSkeleton;
    private RagdollRecovery? _recovery;
    private float _groundedRecoveryTime;
    public bool IsRecovering => _recovery is not null;
    public bool CanActivateRagdoll => _model is not null && _animation is not null && !Ragdoll.IsActive && !IsRecovering;
    public Vector3 Velocity => _movementDirection * CurrentSpeed + Vector3.UnitY * VerticalVelocity;

    public unsafe bool ActivateRagdoll(RagdollImpulse? impulse = null)
    {
        if (!CanActivateRagdoll || _model is null || _animation is null) return false;
        _groundedRecoveryTime = 0;
        // Ensure the final animated frame is applied before capturing the ragdoll pose.
        _animation.SeekTime(_animation.CurrentTime);
        var world = Matrix4x4.Transpose(_model.Model.Transform) * Matrix4x4.CreateScale(_visualScale) *
            Matrix4x4.CreateRotationY(_yawDegrees * MathF.PI / 180f) *
            Matrix4x4.CreateTranslation(_position + Vector3.UnitY * _groundOffset);
        _ragdollSkeleton = RagdollPose.Activate(_model.Model, _animation, world, Ragdoll, Velocity, impulse, _spawnPosition.Y);
        return true;
    }

    public void BeginTackleStruggle(int? hitBody = null)
    {
        if (_model is not null && _animations.TryGetValue("Run", out var gait))
            Ragdoll.StartActiveDrive(RagdollPose.StruggleTargets(_model.Model, gait, Ragdoll), hitBody);
    }

    private bool UpdatePhysicsAndRecovery(float dt)
    {
        if (Ragdoll.IsActive)
        {
            Ragdoll.Update(Math.Max(0, dt));
            // Begin recovery after sustained torso contact, without waiting for every
            // limb to stop moving. An airborne body must finish landing first.
            bool torsoGrounded = Ragdoll.HasMeaningfulGroundContact &&
                (Ragdoll.Bodies[0].Bottom <= Ragdoll.GroundHeight + _config.RagdollGroundContactTolerance ||
                 Ragdoll.Bodies[1].Bottom <= Ragdoll.GroundHeight + _config.RagdollGroundContactTolerance);
            _groundedRecoveryTime = torsoGrounded ? _groundedRecoveryTime + Math.Min(Math.Max(0, dt), .25f) : 0;
            if ((Ragdoll.State == RagdollState.Settled ||
                 torsoGrounded && _groundedRecoveryTime >= _config.OpponentRecoveryGroundDelay) &&
                _model is not null && _ragdollSkeleton is not null)
            {
                _recovery = new(_model.Model, _ragdollSkeleton, Ragdoll, _animations["Down"],
                    _animations["GetUp"], _visualScale, _groundOffset, _yawDegrees, _config, defender: true);
                _position = _recovery.Position; _yawDegrees = _recovery.YawDegrees;
                CurrentSpeed = VerticalVelocity = 0; _movementDirection = Vector3.Zero;
                _tackleRemaining = 0; State = DefenderState.Down; _tackleAnimation = "Down";
                _animation = _animations["Down"];
                Ragdoll.Deactivate(); _ragdollSkeleton = null;
            }
            return true;
        }
        if (_recovery is null) return false;
        _recovery.Update(dt);
        State = _recovery.Phase == RecoveryPhase.Down ? DefenderState.Down : DefenderState.GetUp;
        _tackleAnimation = State.ToString(); _animation = _recovery.Animation;
        if (_recovery.Phase == RecoveryPhase.Complete)
        {
            _recovery = null; State = DefenderState.Locomotion; _tackleAnimation = null;
            SelectAnimation();
            if (_model is not null && _animation is not null)
                Raylib.UpdateModelAnimation(_model.Model, _animation.Animation, _animation.CurrentFrame);
        }
        return true;
    }

    private void DrawRagdoll()
    {
        if (_model is null || _ragdollSkeleton is null)
            throw new InvalidOperationException("Activate through the visual ragdoll adapter before drawing.");
        _ragdollSkeleton.Apply(_model.Model, Ragdoll);
        _ragdollSkeleton.Draw(_model.Model);
        if (RagdollDebugControls.ShowBodies) DrawRagdollBodies();
    }

    private void RestoreAnimatedOwnership()
    {
        if (_ragdollSkeleton is null) return;
        _ragdollSkeleton = null;
        // The playback clock may still cache this frame from before activation.
        // Explicitly reapply it without advancing or restarting the animation.
        if (_model is not null && _animation is not null)
            Raylib.UpdateModelAnimation(_model.Model, _animation.Animation, _animation.CurrentFrame);
    }

    private void DrawRagdollBodies()
    {
        Color colour = Ragdoll.State == RagdollState.Settled ? Color.Green : Color.Orange;
        foreach (var body in Ragdoll.Bodies)
            Raylib.DrawCapsuleWires(body.SegmentStart, body.SegmentEnd, body.Radius, 6, 8, colour);
        foreach (var joint in Ragdoll.Joints)
        {
            var body = Ragdoll.Bodies[joint.Parent];
            Raylib.DrawSphere(body.Position + Vector3.Transform(joint.ParentAnchor, body.Orientation), .025f, Color.Red);
        }
    }
}
