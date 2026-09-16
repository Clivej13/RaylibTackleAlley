using System.Numerics;

namespace RaylibTackleAlley.Game;

public enum RagdollState { Inactive, Active, Settling, Settled }

public readonly record struct RagdollImpulse(int Body, Vector3 Impulse, Vector3? WorldPoint = null);

public sealed class RagdollBody
{
    public string Bone { get; }
    public Vector3 Position { get; internal set; }
    public Quaternion Orientation { get; internal set; }
    public Vector3 LinearVelocity { get; internal set; }
    public Vector3 AngularVelocity { get; internal set; }
    public float Mass { get; }
    public float Radius { get; }
    // Capsule segment in body space, centred on Position.
    public Vector3 HalfSegment { get; }
    internal float InverseInertia => 1f / (Mass * (HalfSegment.LengthSquared() / 3f + 0.4f * Radius * Radius));
    public Vector3 SegmentStart => Position - Vector3.Transform(HalfSegment, Orientation);
    public Vector3 SegmentEnd => Position + Vector3.Transform(HalfSegment, Orientation);
    public float Bottom => Math.Min(SegmentStart.Y, SegmentEnd.Y) - Radius;

    internal RagdollBody(string bone, Vector3 position, Quaternion rotation, Vector3 halfSegment, float radius, float mass)
    {
        Bone = bone; Position = position; Orientation = rotation;
        HalfSegment = halfSegment; Radius = radius; Mass = mass;
    }
}

public sealed record RagdollJoint(int Parent, int Child, Vector3 ParentAnchor,
    Vector3 ChildAnchor, Quaternion ReferenceRotation, Vector3 MinimumAngles, Vector3 MaximumAngles);

/// <summary>Small position-based articulated capsule solver; metres, kilograms, seconds.
/// No animation, rendering, character collision or recovery dependencies.</summary>
public sealed partial class Ragdoll
{
    public const float FixedStep = 1f / 120f;
    public const int SolverIterations = 24;
    public const float Gravity = 9.81f;
    public const float LinearDamping = 0.35f;
    public const float AngularDamping = 2.5f;
    public const float GroundFriction = 8f;
    public const float MaximumAngularSpeed = 12f;
    public const float SettleLinearSpeed = 0.12f;
    public const float SettleAngularSpeed = 0.25f;
    public const float SettleDuration = 0.8f;
    private RagdollBody[] _bodies = [];
    private RagdollJoint[] _joints = [];
    private Vector3[] _previousPositions = [];
    private Quaternion[] _previousRotations = [];
    private float _accumulator, _quietTime, _torsoContactTime;
    private float[] _incomingVertical = [];
    public const float GroundContactTolerance = .005f;
    public const float DownTorsoRadiusMultiplier = 3.5f;
    public const float GroundImpactMinimumSpeed = .5f;
    public const float GroundContactHoldSeconds = .08f;
    public bool HasMeaningfulGroundContact { get; private set; }
    public RagdollState State { get; private set; }
    public bool IsActive => State != RagdollState.Inactive;
    public IReadOnlyList<RagdollBody> Bodies => Array.AsReadOnly(_bodies);
    public IReadOnlyList<RagdollJoint> Joints => Array.AsReadOnly(_joints);
    public float GroundHeight { get; private set; }

    // Body endpoints use actual rig joints, including hands/feet as terminal landmarks.
    public static IReadOnlyList<string> RequiredBones { get; } = Array.AsReadOnly(new[] {
        "Hips", "Chest", "Neck", "Head", "UpperArm.L", "LowerArm.L", "Hand.L",
        "UpperArm.R", "LowerArm.R", "Hand.R", "UpperLeg.L", "LowerLeg.L", "Foot.L",
        "UpperLeg.R", "LowerLeg.R", "Foot.R" });
    private static readonly (string Bone, string? End, int Parent, float Radius, float Mass)[] Layout = [
        ("Hips", "Chest", -1, .15f, 18f), ("Chest", "Neck", 0, .19f, 25f),
        ("Head", null, 1, .13f, 6f),
        ("UpperArm.L", "LowerArm.L", 1, .065f, 3f), ("LowerArm.L", "Hand.L", 3, .055f, 2f),
        ("UpperArm.R", "LowerArm.R", 1, .065f, 3f), ("LowerArm.R", "Hand.R", 5, .055f, 2f),
        ("UpperLeg.L", "LowerLeg.L", 0, .095f, 9f), ("LowerLeg.L", "Foot.L", 7, .07f, 5f),
        ("UpperLeg.R", "LowerLeg.R", 0, .095f, 9f), ("LowerLeg.R", "Foot.R", 9, .07f, 5f)
    ];

    /// <summary>Both dictionaries contain absolute bone transforms in world space.
    /// Reference pose defines anatomical zero (bind pose), never the activation pose.
    /// Snapshot and velocity are copied exactly; constraints first run on the next physics tick.</summary>
    public void Activate(IReadOnlyDictionary<string, Matrix4x4> pose,
        IReadOnlyDictionary<string, Matrix4x4> referencePose, Vector3 velocity,
        RagdollImpulse? impulse = null, float groundHeight = 0f)
    {
        if (!Finite(velocity) || !float.IsFinite(groundHeight)) throw new ArgumentException("Non-finite activation.");
        foreach (string name in RequiredBones)
            if (!pose.TryGetValue(name, out var p) || !referencePose.TryGetValue(name, out var r) ||
                !ValidTransform(p) || !ValidTransform(r))
                throw new ArgumentException($"Missing or invalid ragdoll bone: {name}");
        if (impulse is { } requested) ValidateImpulse(requested, Layout.Length);
        var bodies = new RagdollBody[Layout.Length];
        var joints = new List<RagdollJoint>();
        for (int i = 0; i < Layout.Length; i++)
        {
            var part = Layout[i];
            Matrix4x4.Decompose(pose[part.Bone], out var scale, out var rotation, out var start);
            rotation = Quaternion.Normalize(rotation);
            Vector3 end = part.End is { } name ? pose[name].Translation :
                start + Vector3.Transform(Vector3.UnitY * .12f * scale.Y, rotation);
            Vector3 centre = (start + end) * .5f;
            // Keep rounded ends within the bone span where possible.
            Vector3 half = (end - start) * .5f;
            float radius = part.Radius * Math.Abs(scale.Y);
            if (half.Length() > radius) half *= (half.Length() - radius) / half.Length();
            else half = Vector3.Zero;
            bodies[i] = new(part.Bone, centre, rotation,
                Vector3.Transform(half, Quaternion.Inverse(rotation)), radius, part.Mass) { LinearVelocity = velocity };
            if (part.Parent < 0) continue;
            var parent = bodies[part.Parent];
            Vector3 anchor = start;
            var parentReference = Rotation(referencePose[Layout[part.Parent].Bone]);
            var childReference = Rotation(referencePose[part.Bone]);
            var (min, max) = Limits(i);
            joints.Add(new(part.Parent, i,
                Vector3.Transform(anchor - parent.Position, Quaternion.Inverse(parent.Orientation)),
                Vector3.Transform(anchor - centre, Quaternion.Inverse(rotation)),
                Quaternion.Normalize(Quaternion.Inverse(parentReference) * childReference), min, max));
        }
        StopActiveDrive();
        _bodies = bodies; _joints = joints.ToArray();
        _previousPositions = new Vector3[bodies.Length]; _previousRotations = new Quaternion[bodies.Length];
        _incomingVertical = new float[bodies.Length];
        HasMeaningfulGroundContact = false; _torsoContactTime = 0f;
        _accumulator = _quietTime = 0f; GroundHeight = groundHeight;
        State = RagdollState.Active;
        if (impulse is { } kick) ApplyImpulse(kick);
    }

    // Quaternion rotation-vector bounds in the child's anatomical reference frame.
    // Elbows/knees predominantly hinge about X; small Y/Z slack avoids a brittle perfect hinge.
    private static (Vector3, Vector3) Limits(int body)
    {
        var degrees = body switch {
            1 => (new Vector3(-35, -30, -25), new Vector3(35, 30, 25)),
            2 => (new Vector3(-45, -65, -35), new Vector3(45, 65, 35)),
            4 or 6 => (new Vector3(-145, -8, -8), new Vector3(5, 8, 8)),
            8 or 10 => (new Vector3(-5, -6, -6), new Vector3(145, 6, 6)),
            7 or 9 => (new Vector3(-110, -35, -45), new Vector3(35, 35, 45)),
            _ => (new Vector3(-120, -70, -95), new Vector3(120, 70, 95))
        };
        return (degrees.Item1 * (MathF.PI / 180f), degrees.Item2 * (MathF.PI / 180f));
    }

    public void ApplyImpulse(RagdollImpulse impulse)
    {
        ValidateImpulse(impulse, _bodies.Length);
        if (!IsActive) throw new InvalidOperationException("Activate before applying an impulse.");
        var body = _bodies[impulse.Body];
        body.LinearVelocity += impulse.Impulse / body.Mass;
        if (impulse.WorldPoint is { } point)
            body.AngularVelocity = ClampLength(body.AngularVelocity +
                Vector3.Cross(point - body.Position, impulse.Impulse) * body.InverseInertia, MaximumAngularSpeed);
        _quietTime = 0f; State = RagdollState.Active;
    }

    public void Deactivate()
    {
        StopActiveDrive();
        State = RagdollState.Inactive; _accumulator = _quietTime = _torsoContactTime = 0f;
        HasMeaningfulGroundContact = false;
        foreach (var b in _bodies) b.LinearVelocity = b.AngularVelocity = Vector3.Zero;
    }

    public void Update(float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (!IsActive || State == RagdollState.Settled) return;
        // Bound catch-up to 30 ticks after a stall; never take a variable-size physics step.
        _accumulator += Math.Min(elapsed, .25f);
        while (_accumulator + 1e-7f >= FixedStep && State != RagdollState.Settled)
        {
            Step(); _accumulator = Math.Max(0f, _accumulator - FixedStep);
        }
    }

    private void Step()
    {
        UpdateActiveDrive();
        for (int i = 0; i < _bodies.Length; i++)
        {
            var b = _bodies[i];
            _previousPositions[i] = b.Position; _previousRotations[i] = b.Orientation;
            b.LinearVelocity = (b.LinearVelocity - Vector3.UnitY * Gravity * FixedStep) * MathF.Exp(-LinearDamping * FixedStep);
            b.AngularVelocity = ClampLength(b.AngularVelocity * MathF.Exp(-AngularDamping * FixedStep), MaximumAngularSpeed);
            _incomingVertical[i] = b.LinearVelocity.Y;
            b.Position += b.LinearVelocity * FixedStep;
            b.Orientation = Quaternion.Normalize(Exp(b.AngularVelocity * FixedStep) * b.Orientation);
        }
        for (int iteration = 0; iteration < SolverIterations; iteration++)
        {
            for (int jointIndex = 0; jointIndex < _joints.Length; jointIndex++)
            {
                var joint = _joints[jointIndex];
                SolveActiveMotor(jointIndex);
                var a = _bodies[joint.Parent]; var b = _bodies[joint.Child];
                // Project angular limits before attachment constraints.
                Quaternion relative = Quaternion.Inverse(a.Orientation) * b.Orientation;
                Vector3 angles = Log(Quaternion.Inverse(joint.ReferenceRotation) * relative);
                Vector3 limited = Vector3.Clamp(angles, joint.MinimumAngles, joint.MaximumAngles);
                Quaternion target = Quaternion.Normalize(a.Orientation * joint.ReferenceRotation * Exp(limited));
                b.Orientation = target;
                Vector3 ra = Vector3.Transform(joint.ParentAnchor, a.Orientation);
                Vector3 rb = Vector3.Transform(joint.ChildAnchor, b.Orientation);
                Vector3 error = b.Position + rb - a.Position - ra;
                // Mass-weighted positional projection, with bounded angular correction.
                float weightA = 1f / a.Mass, weightB = 1f / b.Mass;
                float angularA = Vector3.Cross(ra, error).LengthSquared() > 0 ? a.InverseInertia : 0f;
                float angularB = Vector3.Cross(rb, error).LengthSquared() > 0 ? b.InverseInertia : 0f;
                float denominator = weightA + weightB + angularA * ra.LengthSquared() + angularB * rb.LengthSquared();
                Vector3 correction = error / Math.Max(denominator, 1e-6f);
                a.Position += correction * weightA; b.Position -= correction * weightB;
                a.Orientation = Quaternion.Normalize(Exp(ClampLength(Vector3.Cross(ra, correction) * angularA, .1f)) * a.Orientation);
                b.Orientation = Quaternion.Normalize(Exp(ClampLength(Vector3.Cross(rb, -correction) * angularB, .1f)) * b.Orientation);
            }
            foreach (var b in _bodies) SolveGround(b);
        }
        // Final angular projection is exact; lift capsules again after it to guarantee ground clearance.
        foreach (var j in _joints)
        {
            var a = _bodies[j.Parent]; var b = _bodies[j.Child];
            b.Orientation = Quaternion.Normalize(a.Orientation * j.ReferenceRotation *
                Exp(Vector3.Clamp(JointAngles(j), j.MinimumAngles, j.MaximumAngles)));
        }
        // Reconcile attachments and the ground without disturbing the final angular limits.
        for (int iteration = 0; iteration < SolverIterations; iteration++)
        {
            foreach (var j in _joints)
            {
                var a = _bodies[j.Parent]; var b = _bodies[j.Child];
                Vector3 error = b.Position + Vector3.Transform(j.ChildAnchor, b.Orientation) -
                    a.Position - Vector3.Transform(j.ParentAnchor, a.Orientation);
                float wa = 1f / a.Mass, wb = 1f / b.Mass;
                a.Position += error * (wa / (wa + wb));
                b.Position -= error * (wb / (wa + wb));
            }
            foreach (var b in _bodies)
                b.Position += Vector3.UnitY * Math.Max(0f, GroundHeight - b.Bottom);
        }
        bool quiet = true, grounded = false;
        for (int i = 0; i < _bodies.Length; i++)
        {
            var b = _bodies[i];
            b.Position += Vector3.UnitY * Math.Max(0f, GroundHeight - b.Bottom);
            b.LinearVelocity = (b.Position - _previousPositions[i]) / FixedStep;
            b.AngularVelocity = ClampLength(Log(b.Orientation * Quaternion.Inverse(_previousRotations[i])) / FixedStep, MaximumAngularSpeed);
            if (b.Bottom <= GroundHeight + GroundContactTolerance)
            {
                grounded = true;
                b.LinearVelocity *= MathF.Exp(-GroundFriction * FixedStep);
                b.AngularVelocity *= MathF.Exp(-GroundFriction * FixedStep);
                if (b.LinearVelocity.Y < 0) b.LinearVelocity = new(b.LinearVelocity.X, 0, b.LinearVelocity.Z);
            }
            quiet &= b.LinearVelocity.Length() < SettleLinearSpeed && b.AngularVelocity.Length() < SettleAngularSpeed;
        }
        // Ignore feet/hands/head grazes. A low torso plus pelvis/chest contact means
        // the carrier is actually down. Evaluate every substep so a brief impact is not lost.
        bool torsoContact = (_bodies[0].Bottom <= GroundHeight + GroundContactTolerance ||
            _bodies[1].Bottom <= GroundHeight + GroundContactTolerance) &&
            _bodies[1].Position.Y <= GroundHeight + DownTorsoRadiusMultiplier * _bodies[1].Radius;
        bool impact = (_bodies[0].Bottom <= GroundHeight + GroundContactTolerance && _incomingVertical[0] <= -GroundImpactMinimumSpeed) ||
            (_bodies[1].Bottom <= GroundHeight + GroundContactTolerance && _incomingVertical[1] <= -GroundImpactMinimumSpeed);
        _torsoContactTime = torsoContact ? _torsoContactTime + FixedStep : 0f;
        HasMeaningfulGroundContact |= torsoContact && (impact || _torsoContactTime >= GroundContactHoldSeconds);
        if (HasMeaningfulGroundContact) StopActiveDrive();
        _quietTime = quiet && grounded ? _quietTime + FixedStep : 0f;
        State = _quietTime >= SettleDuration ? RagdollState.Settled :
            _quietTime > 0f ? RagdollState.Settling : RagdollState.Active;
        if (State == RagdollState.Settled)
            foreach (var b in _bodies) b.LinearVelocity = b.AngularVelocity = Vector3.Zero;
    }

    private void SolveGround(RagdollBody b)
    {
        Vector3 offset = Vector3.Transform(b.HalfSegment, b.Orientation);
        Vector3 contact = (offset.Y > 0 ? -offset : offset) - Vector3.UnitY * b.Radius;
        float depth = GroundHeight - (b.Position.Y + contact.Y);
        if (depth <= 0) return;
        Vector3 torque = Vector3.Cross(contact, Vector3.UnitY);
        float w = 1f / b.Mass;
        float lambda = depth / (w + b.InverseInertia * torque.LengthSquared());
        b.Position += Vector3.UnitY * lambda * w;
        b.Orientation = Quaternion.Normalize(Exp(ClampLength(torque * lambda * b.InverseInertia, .15f)) * b.Orientation);
    }

    public Vector3 JointAngles(RagdollJoint joint) => Log(Quaternion.Inverse(joint.ReferenceRotation) *
        Quaternion.Inverse(_bodies[joint.Parent].Orientation) * _bodies[joint.Child].Orientation);
    public float JointSeparation(RagdollJoint joint) => Vector3.Distance(
        _bodies[joint.Parent].Position + Vector3.Transform(joint.ParentAnchor, _bodies[joint.Parent].Orientation),
        _bodies[joint.Child].Position + Vector3.Transform(joint.ChildAnchor, _bodies[joint.Child].Orientation));
    private static Quaternion Rotation(Matrix4x4 m) { Matrix4x4.Decompose(m, out _, out var q, out _); return Quaternion.Normalize(q); }
    private static bool ValidTransform(Matrix4x4 m) => Matrix4x4.Decompose(m, out var s, out var q, out var p) &&
        Finite(p) && Finite(s) && s.X > 0 && s.Y > 0 && s.Z > 0 && float.IsFinite(q.LengthSquared()) && q.LengthSquared() > 1e-8f;
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static void ValidateImpulse(RagdollImpulse i, int count)
    {
        if (i.Body < 0 || i.Body >= count || !Finite(i.Impulse) || i.WorldPoint is { } p && !Finite(p))
            throw new ArgumentException("Invalid ragdoll impulse.");
    }
    private static Vector3 ClampLength(Vector3 v, float max) => v.LengthSquared() > max * max ? Vector3.Normalize(v) * max : v;
    private static Quaternion Exp(Vector3 v) => v.Length() < 1e-8f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.Normalize(v), v.Length());
    private static Vector3 Log(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        if (q.W < 0) q = new(-q.X, -q.Y, -q.Z, -q.W);
        Vector3 v = new(q.X, q.Y, q.Z);
        return v.Length() < 1e-8f ? Vector3.Zero : Vector3.Normalize(v) * (2f * MathF.Atan2(v.Length(), Math.Clamp(q.W, -1f, 1f)));
    }
}
