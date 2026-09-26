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
        if (Vector3.DistanceSquared(_position, carrier.Position) > MathF.Pow(_config.ContactBroadPhaseDistance * PlayerPhysicalAttributes.MaximumHeightRatio, 2)) return false;
        return RefreshContactPose() is { } defenderPose &&
            carrier.ContactPose() is { } playerPose && RagdollContact.Overlaps(defenderPose, playerPose);
    }

    internal Ragdoll? RefreshContactPose()
    {
        if (Ragdoll.IsActive) return Ragdoll;
        if (_model is null || _animation is null || IsRecovering) return null;
        var world = Matrix4x4.Transpose(_model.Model.Transform) * Matrix4x4.CreateScale(_modelScale) *
            Matrix4x4.CreateRotationY(_yawDegrees * MathF.PI / 180f) *
            Matrix4x4.CreateTranslation(_position + Vector3.UnitY * _groundOffset);
        RagdollPose.RefreshContactPose(_animation, world, _contactPose);
        return _contactPose;
    }
    internal Ragdoll ContactRagdoll => Ragdoll.IsActive ? Ragdoll : _contactPose;
    internal float ContactCooldown { get; set; }
    internal float WrapRemaining { get; set; }
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
        var world = Matrix4x4.Transpose(_model.Model.Transform) * Matrix4x4.CreateScale(_modelScale) *
            Matrix4x4.CreateRotationY(_yawDegrees * MathF.PI / 180f) *
            Matrix4x4.CreateTranslation(_position + Vector3.UnitY * _groundOffset);
        _ragdollSkeleton = RagdollPose.Activate(_model.Model, _animation, world, Ragdoll, Velocity, impulse, _spawnPosition.Y);
        return true;
    }

    internal void ApplyContactImpulse(Vector3 impulse)
    {
        if (Ragdoll.IsActive)
        {
            for (int i = 0; i < Ragdoll.Bodies.Count; i++)
                Ragdoll.ApplyImpulse(new(i, impulse * Ragdoll.Bodies[i].Mass / Physical.TotalMass));
        }
        else
        {
            Vector3 velocity = Velocity + impulse / Physical.TotalMass;
            VerticalVelocity = velocity.Y;
            velocity.Y = 0;
            CurrentSpeed = velocity.Length();
            _movementDirection = CurrentSpeed > .0001f ? velocity / CurrentSpeed : Vector3.Zero;
        }
        Ragdoll.LastContactImpulse = impulse.Length();
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
                    _animations["GetUp"], _modelScale, _groundOffset, _yawDegrees, _config, defender: true);
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
            ResetDecisions();
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
