using System.Numerics;

namespace RaylibTackleAlley.Game;

public sealed partial class Ragdoll
{
    public const float StruggleDuration = 1.25f;
    public const float StruggleBlendIn = .12f;
    public const float StruggleFadeOut = .5f;
    public const float MotorStiffness = 100f;
    public const float MotorDamping = 12f;
    public const float MaximumMotorAcceleration = 90f;
    public const float MotorPoseResponse = 35f;
    public const float MaximumMotorCorrectionSpeed = 6f;
    public const float FootSupportDistance = .12f;
    public const float SupportStiffness = 32f;
    public const float SupportDamping = 6f;
    public const float MaximumSupportAcceleration = 14f;
    public const float BalanceStiffness = 18f;
    public const float MaximumBalanceAcceleration = 12f;
    public const float HitLimbMotorStrength = .08f;
    public const float HitTorsoMotorStrength = .15f;
    public const float ContactYieldSpeed = .75f;
    private float[] _motorStrength = [];
    private bool _torsoHit;
    public int? LastHitBody { get; private set; }
    public float MotorStrength(int body) => IsActivelyDriven ? _motorStrength[body] : 0f;

    public void ReactToContact(int body, float closingSpeed)
    {
        if (!IsActivelyDriven || closingSpeed < ContactYieldSpeed) return;
        if (body < 0 || body >= _bodies.Length) throw new ArgumentOutOfRangeException(nameof(body));
        LastHitBody = body;
        if (body <= 1)
        {
            _torsoHit = true;
            _motorStrength[0] = _motorStrength[1] = HitTorsoMotorStrength;
            return; // Legs keep their gait while the torso falls.
        }
        if (body == 2) return;
        // Yield the entire struck limb, leaving other limbs and the torso braced.
        int first = body % 2 == 0 ? body - 1 : body;
        _motorStrength[first] = _motorStrength[first + 1] = HitLimbMotorStrength;
    }
    private Func<float, IReadOnlyList<Quaternion>>? _drivePose;
    private Quaternion[] _driveStart = [], _driveTargets = [];
    private Quaternion _driveRoot;
    private float _driveHeight, _driveElapsed;
    public bool IsActivelyDriven => _drivePose is not null;
    public float ActiveDriveWeight { get; private set; }

    /// <summary>Joint-space animation targets; no animated root translation or velocity overwrite.</summary>
    public void StartActiveDrive(Func<float, IReadOnlyList<Quaternion>> pose, int? hitBody = null)
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (!IsActive || State == RagdollState.Settled || HasMeaningfulGroundContact) return;
        _driveStart = _joints.Select(j => Quaternion.Normalize(
            Quaternion.Inverse(_bodies[j.Parent].Orientation) * _bodies[j.Child].Orientation)).ToArray();
        _driveTargets = _driveStart.ToArray();
        _driveRoot = _bodies[0].Orientation;
        _driveHeight = _bodies[0].Position.Y - GroundHeight;
        _driveElapsed = 0; ActiveDriveWeight = 0; _drivePose = pose;
        _motorStrength = Enumerable.Repeat(1f, _bodies.Length).ToArray();
        _torsoHit = false; LastHitBody = null;
        if (hitBody is { } hit) ReactToContact(hit, ContactYieldSpeed);
    }

    private void StopActiveDrive()
    {
        _drivePose = null; _driveStart = []; _driveTargets = []; _motorStrength = [];
        _torsoHit = false; LastHitBody = null;
        _driveElapsed = 0; ActiveDriveWeight = 0;
    }

    private void SolveActiveMotor(int index)
    {
        if (!IsActivelyDriven) return;
        var joint = _joints[index];
        var a = _bodies[joint.Parent]; var b = _bodies[joint.Child];
        Vector3 error = Log(a.Orientation * _driveTargets[index] * Quaternion.Inverse(b.Orientation));
        // Compliant angular motor inside the position solver, so attachment projection
        // cannot erase the animation's small velocity impulse on every iteration.
        Vector3 correction = ClampLength(error * MotorPoseResponse * FixedStep / SolverIterations,
            MaximumMotorCorrectionSpeed * FixedStep / SolverIterations) *
            ActiveDriveWeight * _motorStrength[joint.Child];
        float inverse = a.InverseInertia + b.InverseInertia;
        a.Orientation = Quaternion.Normalize(Exp(-correction * a.InverseInertia / inverse) * a.Orientation);
        b.Orientation = Quaternion.Normalize(Exp(correction * b.InverseInertia / inverse) * b.Orientation);
    }

    private void UpdateActiveDrive()
    {
        if (_drivePose is null) return;
        _driveElapsed += FixedStep;
        if (_driveElapsed >= StruggleDuration || HasMeaningfulGroundContact)
        {
            StopActiveDrive(); return;
        }
        float blendIn = Math.Clamp(_driveElapsed / StruggleBlendIn, 0, 1);
        float fade = Math.Clamp((StruggleDuration - _driveElapsed) / StruggleFadeOut, 0, 1);
        ActiveDriveWeight = blendIn * fade;
        var targets = _drivePose(_driveElapsed);
        if (targets.Count != _joints.Length) throw new ArgumentException("One drive target per ragdoll joint is required.");
        for (int i = 0; i < _joints.Length; i++)
        {
            var j = _joints[i]; var a = _bodies[j.Parent]; var b = _bodies[j.Child];
            if (!float.IsFinite(targets[i].LengthSquared()) || targets[i].LengthSquared() < 1e-8f)
                throw new ArgumentException("Invalid drive rotation.");
            Quaternion target = Quaternion.Slerp(_driveStart[i], Quaternion.Normalize(targets[i]), blendIn);
            Vector3 angles = Vector3.Clamp(Log(Quaternion.Inverse(j.ReferenceRotation) * target), j.MinimumAngles, j.MaximumAngles);
            _driveTargets[i] = Quaternion.Normalize(j.ReferenceRotation * Exp(angles));
            Quaternion desired = Quaternion.Normalize(a.Orientation * _driveTargets[i]);
            Vector3 acceleration = ClampLength(
                Log(desired * Quaternion.Inverse(b.Orientation)) * MotorStiffness -
                (b.AngularVelocity - a.AngularVelocity) * MotorDamping, MaximumMotorAcceleration) * ActiveDriveWeight * _motorStrength[j.Child];
            // Equal/opposite internal torque: animation works against the collision, never teleports bones.
            Vector3 angularImpulse = acceleration * FixedStep / (a.InverseInertia + b.InverseInertia);
            a.AngularVelocity -= angularImpulse * a.InverseInertia;
            b.AngularVelocity += angularImpulse * b.InverseInertia;
        }

        // Feet can briefly support body weight and brace the pelvis. No support in mid-air,
        // no horizontal root motion, and no strength after a large roll/knockdown.
        var root = _bodies[0];
        float tilt = Log(root.Orientation * Quaternion.Inverse(_driveRoot)).Length();
        float balance = Math.Clamp(1 - tilt / (MathF.PI * .45f), 0, 1);
        bool supported = (_motorStrength[8] > .5f && _bodies[8].Bottom <= GroundHeight + FootSupportDistance) ||
            (_motorStrength[10] > .5f && _bodies[10].Bottom <= GroundHeight + FootSupportDistance);
        if (!supported || _torsoHit) return;
        float weight = ActiveDriveWeight * balance;
        float support = Math.Clamp(Gravity + SupportStiffness * (_driveHeight - (root.Position.Y - GroundHeight)) -
            SupportDamping * root.LinearVelocity.Y, 0, MaximumSupportAcceleration) * weight;
        foreach (var body in _bodies) body.LinearVelocity += Vector3.UnitY * support * FixedStep;
        Vector3 balanceAcceleration = ClampLength(
            Log(_driveRoot * Quaternion.Inverse(root.Orientation)) * BalanceStiffness -
            root.AngularVelocity * 3f, MaximumBalanceAcceleration);
        root.AngularVelocity += balanceAcceleration * weight * FixedStep;
    }
}
