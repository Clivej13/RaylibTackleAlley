using System.Numerics;

namespace RaylibTackleAlley.Game;

public enum RagdollState { Inactive, Active, Settling, Settled }

public readonly record struct RagdollImpulse(int Body, Vector3 Impulse, Vector3? WorldPoint = null);

public sealed class RagdollBody
{
    internal TackleAlleyConfig Config { get; }
    internal float Scale { get; }
    internal float ContactRadius { get; init; }
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

    internal RagdollBody(string bone, Vector3 position, Quaternion rotation, Vector3 halfSegment, float radius, float mass, TackleAlleyConfig config, float scale)
    {
        Config = config; Scale = scale;
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
    internal TackleAlleyConfig Config { get; }
    private TackleAlleyConfig _config => Config;
    public PlayerPhysicalAttributes? Physical { get; }
    public Vector3 Momentum => _bodies.Aggregate(Vector3.Zero, (sum, b) => sum + b.Mass * b.LinearVelocity);
    public float LastContactImpulse { get; internal set; }
    public Ragdoll(TackleAlleyConfig? config = null, PlayerPhysicalAttributes? physical = null)
    {
        Config = config ?? new();
        Physical = physical;
        physical?.Validate();
        Config.ValidateRagdollPhysics();
    }
    private RagdollBody[] _bodies = [];
    private RagdollJoint[] _joints = [];
    private Vector3[] _previousPositions = [];
    private Quaternion[] _previousRotations = [];
    private float _accumulator, _quietTime, _torsoContactTime;
    private float[] _incomingVertical = [];
    public bool HasMeaningfulGroundContact { get; private set; }
    public bool HasDownGroundContact { get; private set; }
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
    private (string Bone, string? End, int Parent, float Radius, float Mass)[] Layout => [
        ("Hips", "Chest", -1, _config.RagdollPelvisRadius, _config.RagdollPelvisMass), ("Chest", "Neck", 0, _config.RagdollChestRadius, _config.RagdollChestMass),
        ("Head", null, 1, _config.RagdollHeadRadius, _config.RagdollHeadMass),
        ("UpperArm.L", "LowerArm.L", 1, _config.RagdollUpperArmRadius, _config.RagdollUpperArmMass), ("LowerArm.L", "Hand.L", 3, _config.RagdollLowerArmRadius, _config.RagdollLowerArmMass),
        ("UpperArm.R", "LowerArm.R", 1, _config.RagdollUpperArmRadius, _config.RagdollUpperArmMass), ("LowerArm.R", "Hand.R", 5, _config.RagdollLowerArmRadius, _config.RagdollLowerArmMass),
        ("UpperLeg.L", "LowerLeg.L", 0, _config.RagdollUpperLegRadius, _config.RagdollUpperLegMass), ("LowerLeg.L", "Foot.L", 7, _config.RagdollLowerLegRadius, _config.RagdollLowerLegMass),
        ("UpperLeg.R", "LowerLeg.R", 0, _config.RagdollUpperLegRadius, _config.RagdollUpperLegMass), ("LowerLeg.R", "Foot.R", 9, _config.RagdollLowerLegRadius, _config.RagdollLowerLegMass)
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
        var layout = Layout;
        if (impulse is { } requested) ValidateImpulse(requested, layout.Length);
        var bodies = new RagdollBody[layout.Length];
        var joints = new List<RagdollJoint>();
        for (int i = 0; i < layout.Length; i++)
        {
            var part = layout[i];
            AffinePose.Decompose(pose[part.Bone], out var scale, out var rotation, out var start);
            rotation = Quaternion.Normalize(rotation);
            Vector3 end = part.End is { } name ? pose[name].Translation :
                start + Vector3.Transform(Vector3.UnitY * _config.RagdollHeadSegmentLength * scale.Y, rotation);
            Vector3 centre = (start + end) * .5f;
            // Keep rounded ends within the bone span where possible.
            Vector3 half = (end - start) * .5f;
            float radiusScale = Math.Abs(scale.Y) * (Physical is null ? 1f : Physical.RadiusScale / Physical.HeightRatio);
            float radius = Physical is null ? part.Radius * radiusScale : Physical.BodyPartRadii[i] * Math.Abs(scale.Y) / Physical.HeightRatio;
            if (half.Length() > radius) half *= (half.Length() - radius) / half.Length();
            else half = Vector3.Zero;
            bodies[i] = new(part.Bone, centre, rotation,
                Vector3.Transform(half, Quaternion.Inverse(rotation)), radius, Physical?.BodyPartMasses[i] ?? part.Mass, _config, radiusScale) { LinearVelocity = velocity,
                    ContactRadius = Physical is not null ? Physical.BodyPartContactRadii[i] * Math.Abs(scale.Y) / Physical.HeightRatio :
                        i == 0 ? _config.ContactPelvisRadius * radiusScale :
                        i == 1 ? _config.ContactTorsoRadius * radiusScale : radius * _config.ContactLimbRadiusScale };
            if (part.Parent < 0) continue;
            var parent = bodies[part.Parent];
            Vector3 anchor = start;
            var parentReference = Rotation(referencePose[layout[part.Parent].Bone]);
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
        HasMeaningfulGroundContact = false; HasDownGroundContact = false; _torsoContactTime = 0f;
        _accumulator = _quietTime = 0f; GroundHeight = groundHeight;
        State = RagdollState.Active;
        if (impulse is { } kick) ApplyImpulse(kick);
    }

    // Quaternion rotation-vector bounds in the child's anatomical reference frame.
    // Elbows/knees predominantly hinge about X; small Y/Z slack avoids a brittle perfect hinge.
    private (Vector3, Vector3) Limits(int body)
    {
        var degrees = body switch {
            1 => (_config.RagdollTorsoLimits.Minimum, _config.RagdollTorsoLimits.Maximum),
            2 => (_config.RagdollNeckLimits.Minimum, _config.RagdollNeckLimits.Maximum),
            4 or 6 => (_config.RagdollElbowLimits.Minimum, _config.RagdollElbowLimits.Maximum),
            8 or 10 => (_config.RagdollKneeLimits.Minimum, _config.RagdollKneeLimits.Maximum),
            7 or 9 => (_config.RagdollHipLimits.Minimum, _config.RagdollHipLimits.Maximum),
            _ => (_config.RagdollShoulderLimits.Minimum, _config.RagdollShoulderLimits.Maximum)
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
                Vector3.Cross(point - body.Position, impulse.Impulse) * body.InverseInertia, _config.RagdollMaximumAngularSpeed);
        _quietTime = 0f; State = RagdollState.Active;
    }

    public void Deactivate()
    {
        StopActiveDrive();
        LastContactImpulse = 0;
        State = RagdollState.Inactive; _accumulator = _quietTime = _torsoContactTime = 0f;
        HasMeaningfulGroundContact = false; HasDownGroundContact = false;
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
            b.LinearVelocity = (b.LinearVelocity - Vector3.UnitY * _config.RagdollGravity * FixedStep) * MathF.Exp(-_config.RagdollLinearDamping * FixedStep);
            b.AngularVelocity = ClampLength(b.AngularVelocity * MathF.Exp(-_config.RagdollAngularDamping * FixedStep), _config.RagdollMaximumAngularSpeed);
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
            b.AngularVelocity = ClampLength(Log(b.Orientation * Quaternion.Inverse(_previousRotations[i])) / FixedStep, _config.RagdollMaximumAngularSpeed);
            if (b.Bottom <= GroundHeight + _config.RagdollGroundContactTolerance)
            {
                grounded = true;
                b.LinearVelocity *= MathF.Exp(-_config.RagdollGroundFriction * FixedStep);
                b.AngularVelocity *= MathF.Exp(-_config.RagdollGroundFriction * FixedStep);
                if (b.LinearVelocity.Y < 0) b.LinearVelocity = new(b.LinearVelocity.X, 0, b.LinearVelocity.Z);
            }
            quiet &= b.LinearVelocity.Length() < _config.RagdollSettleLinearSpeed && b.AngularVelocity.Length() < _config.RagdollSettleAngularSpeed;
        }
        HasDownGroundContact |= IsDownOnGround();
        // Ignore feet/hands/head grazes. A low torso plus pelvis/chest contact means
        // the carrier is actually down. Evaluate every substep so a brief impact is not lost.
        bool torsoContact = (_bodies[0].Bottom <= GroundHeight + _config.RagdollGroundContactTolerance ||
            _bodies[1].Bottom <= GroundHeight + _config.RagdollGroundContactTolerance) &&
            _bodies[1].Position.Y <= GroundHeight + _config.RagdollDownTorsoRadiusMultiplier * _bodies[1].Radius;
        bool impact = (_bodies[0].Bottom <= GroundHeight + _config.RagdollGroundContactTolerance && _incomingVertical[0] <= -_config.RagdollGroundImpactMinimumSpeed) ||
            (_bodies[1].Bottom <= GroundHeight + _config.RagdollGroundContactTolerance && _incomingVertical[1] <= -_config.RagdollGroundImpactMinimumSpeed);
        _torsoContactTime = torsoContact ? _torsoContactTime + FixedStep : 0f;
        HasMeaningfulGroundContact |= torsoContact && (impact || _torsoContactTime >= _config.RagdollGroundContactHoldSeconds);
        if (HasMeaningfulGroundContact) StopActiveDrive();
        _quietTime = quiet && grounded ? _quietTime + FixedStep : 0f;
        State = _quietTime >= _config.RagdollSettleDuration ? RagdollState.Settled :
            _quietTime > 0f ? RagdollState.Settling : RagdollState.Active;
        if (State == RagdollState.Settled)
            foreach (var b in _bodies) b.LinearVelocity = b.AngularVelocity = Vector3.Zero;
    }

    // Pelvis covers the bum; torso capsules cover belly/back. Use the proximal
    // half of each forearm so a hand plant alone does not end the run.
    // Only the knee end of a lower leg counts, never its foot end.
    public bool IsDownOnGround()
    {
        if (!IsActive) return false;
        float floor = GroundHeight + _config.RagdollGroundContactTolerance;
        foreach (var body in _bodies)
        {
            float bottom = body.Bone switch
            {
                "Hips" or "Chest" => body.Bottom,
                "LowerArm.L" or "LowerArm.R" =>
                    Math.Min(body.SegmentStart.Y, body.Position.Y) - body.Radius,
                "LowerLeg.L" or "LowerLeg.R" => body.SegmentStart.Y - body.Radius,
                _ => float.PositiveInfinity
            };
            if (bottom <= floor) return true;
        }
        return false;
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
    private static Quaternion Rotation(Matrix4x4 m) { AffinePose.Decompose(m, out _, out var q, out _); return Quaternion.Normalize(q); }
    private static bool ValidTransform(Matrix4x4 m) => AffinePose.Decompose(m, out var s, out var q, out var p) &&
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
