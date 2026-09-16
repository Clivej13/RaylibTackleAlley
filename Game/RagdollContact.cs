using System.Numerics;

namespace RaylibTackleAlley.Game;

public readonly record struct RagdollHit(int DefenderBody, int CarrierBody, Vector3 Point, Vector3 Normal);

/// <summary>Two-character torso spheres and arm/leg capsules, without self collision.</summary>
public static class RagdollContact
{
    public const float Restitution = .05f;
    public const float ContactFriction = .2f;
    public const float ImpulseScale = 1f;
    public const float MaximumImpulse = 160f;
    // Full-character momentum transfer at confirmed tackle only (not each collision tick).
    public const float MaximumTackleImpulse = 1200f;
    public const float TackleAngularEnergyShare = .2f;
    public const float TorsoRadius = .34f;
    public const float PelvisRadius = .28f;
    public const float ChestShare = .6f;
    public const float PenetrationSlop = .005f;
    public const int SeparationIterations = 8;

    public const float LimbRadiusScale = 1f;
    private static readonly int[] CollisionBodies = [0, 1, 3, 4, 5, 6, 7, 8, 9, 10];
    public static float Radius(RagdollBody body) => body.Bone switch
    {
        "Chest" => TorsoRadius * body.Radius / .19f,
        "Hips" => PelvisRadius * body.Radius / .15f,
        _ => body.Radius * LimbRadiusScale
    };

    public static float SurfaceGap(RagdollBody a, RagdollBody b)
    {
        ClosestPoints(a, b, out var pa, out var pb);
        return Vector3.Distance(pa, pb) - Radius(a) - Radius(b);
    }

    private static void ClosestPoints(RagdollBody a, RagdollBody b, out Vector3 pa, out Vector3 pb)
    {
        bool torsoA = a.Bone is "Chest" or "Hips", torsoB = b.Bone is "Chest" or "Hips";
        Vector3 p = torsoA ? a.Position : a.SegmentStart;
        Vector3 q = torsoB ? b.Position : b.SegmentStart;
        Vector3 u = torsoA ? Vector3.Zero : a.SegmentEnd - p;
        Vector3 v = torsoB ? Vector3.Zero : b.SegmentEnd - q;
        Vector3 r = p - q;
        float aa = Vector3.Dot(u, u), bb = Vector3.Dot(u, v), cc = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, r), e = Vector3.Dot(v, r);
        float s = 0, t = 0;
        if (aa <= 1e-8f) t = cc > 1e-8f ? Math.Clamp(e / cc, 0, 1) : 0;
        else if (cc <= 1e-8f) s = Math.Clamp(-d / aa, 0, 1);
        else
        {
            float denominator = aa * cc - bb * bb;
            s = denominator > 1e-8f ? Math.Clamp((bb * e - cc * d) / denominator, 0, 1) : 0;
            t = (bb * s + e) / cc;
            if (t < 0) { t = 0; s = Math.Clamp(-d / aa, 0, 1); }
            else if (t > 1) { t = 1; s = Math.Clamp((bb - d) / aa, 0, 1); }
        }
        pa = p + u * s; pb = q + v * t;
    }

    private static (Vector3 Normal, Vector3 Point) Geometry(RagdollBody a, RagdollBody b, Vector3 closing)
    {
        ClosestPoints(a, b, out var pa, out var pb);
        Vector3 normal = Normal(pb - pa, closing);
        return (normal, (pa + normal * Radius(a) + pb - normal * Radius(b)) * .5f);
    }

    // The existing gameplay distance test can confirm before these simplified proxies touch.
    // Use the closest approaching surface pair at handoff; later contacts require overlap.
    public static RagdollHit? Resolve(Ragdoll defender, Ragdoll carrier, bool confirmedContact = false)
    {
        if (!defender.IsActive || !carrier.IsActive || ReferenceEquals(defender, carrier)) return null;
        int nearestA = 0, nearestB = 0;
        float nearestGap = float.PositiveInfinity;
        bool foundClosing = false;
        Vector3 closingVelocity = CentreVelocity(defender) - CentreVelocity(carrier);
        foreach (int a in CollisionBodies)
        foreach (int b in CollisionBodies)
        {
            float gap = SurfaceGap(defender.Bodies[a], carrier.Bodies[b]);
            var geometry = Geometry(defender.Bodies[a], carrier.Bodies[b], closingVelocity);
            bool closing = Vector3.Dot(closingVelocity, geometry.Normal) > 1e-5f;
            // A diving pose can have a separating pelvis while its chest hits the carrier.
            // Choose the nearest approaching proxy for the one-time tackle handoff.
            if ((!foundClosing || closing) && (closing && !foundClosing || gap < nearestGap))
            {
                nearestGap = gap; nearestA = a; nearestB = b; foundClosing = closing;
            }
            if (gap > PenetrationSlop) continue;
            if (!confirmedContact) Contact(defender, carrier, a, b);
        }
        var hitGeometry = Geometry(defender.Bodies[nearestA], carrier.Bodies[nearestB], closingVelocity);
        var hit = new RagdollHit(nearestA, nearestB, hitGeometry.Point, hitGeometry.Normal);
        if (confirmedContact) TackleMomentum(defender, carrier, nearestA, nearestB);

        // Split positional correction: never turn spawn overlap into kinetic energy.
        // Translate whole articulated poses so existing joints and angular limits are preserved.
        for (int iteration = 0; iteration < SeparationIterations; iteration++)
        foreach (int a in CollisionBodies)
        foreach (int b in CollisionBodies)
        {
            var da = defender.Bodies[a]; var cb = carrier.Bodies[b];
            float depth = -SurfaceGap(da, cb) - PenetrationSlop;
            if (depth <= 0) continue;
            Vector3 normal = Geometry(da, cb, Velocity(defender, a, da.Position) - Velocity(carrier, b, cb.Position)).Normal;
            float wa = 1 / Mass(defender), wb = 1 / Mass(carrier);
            Translate(defender, -normal * depth * wa / (wa + wb));
            Translate(carrier, normal * depth * wb / (wa + wb));
        }
        return confirmedContact || nearestGap <= PenetrationSlop ? hit : null;
    }

    private static Vector3 CentreVelocity(Ragdoll doll) =>
        doll.Bodies.Aggregate(Vector3.Zero, (sum, b) => sum + b.Mass * b.LinearVelocity) / Mass(doll);

    private static Vector3 CentreOfMass(Ragdoll doll) =>
        doll.Bodies.Aggregate(Vector3.Zero, (sum, b) => sum + b.Mass * b.Position) / Mass(doll);

    private static void TackleMomentum(Ragdoll a, Ragdoll b, int ai, int bi)
    {
        float ma = Mass(a), mb = Mass(b);
        Vector3 va = CentreVelocity(a), vb = CentreVelocity(b);
        var ab = a.Bodies[ai]; var bb = b.Bodies[bi];
        var (normal, point) = Geometry(ab, bb, va - vb);
        if (Vector3.Dot(vb - va, normal) >= -1e-5f) return;

        // Inelastic tackle: both COM velocities become (ma*va + mb*vb)/(ma+mb).
        // Vector addition already subtracts opposing momentum and retains angled momentum.
        // Transmit translation through every body's mass; torso-only kicks leave the
        // remaining mass running forward and spend most of the response on bending joints.
        float inverseMass = 1 / ma + 1 / mb;
        Vector3 impulse = (va - vb) / inverseMass;
        if (impulse.Length() > MaximumTackleImpulse)
            impulse = Vector3.Normalize(impulse) * MaximumTackleImpulse;
        float lostEnergy = Math.Max(0, Vector3.Dot(impulse, va - vb) -
            .5f * inverseMass * impulse.LengthSquared());
        float angularBudget = lostEnergy * TackleAngularEnergyShare * .5f;
        ApplyTackleImpulse(a, -impulse, point, angularBudget, ai);
        ApplyTackleImpulse(b, impulse, point, angularBudget, bi);
    }

    private static float TackleShare(int body, int hit) => hit < 2 ? (body < 2 ? Share(body) : 0) :
        body == hit ? .75f : body == 0 ? .1f : body == 1 ? .15f : 0;

    private static void ApplyTackleImpulse(Ragdoll doll, Vector3 impulse, Vector3 point, float angularBudget, int hit)
    {
        Vector3 torque = Vector3.Cross(point - CentreOfMass(doll), impulse);
        float mass = Mass(doll);
        for (int i = 0; i < doll.Bodies.Count; i++)
            doll.ApplyImpulse(new(i, impulse * (doll.Bodies[i].Mass / mass)));

        // Localize rotation to the struck limb or torso, bounded by energy actually
        // dissipated in this impact, including any angular motion already present.
        float quadratic = 0, linear = 0;
        for (int i = 0; i < doll.Bodies.Count; i++)
        {
            var body = doll.Bodies[i];
            Vector3 delta = torque * TackleShare(i, hit) * body.InverseInertia;
            quadratic += .5f * delta.LengthSquared() / body.InverseInertia;
            linear += Vector3.Dot(body.AngularVelocity, delta) / body.InverseInertia;
        }
        float scale = quadratic > 1e-8f
            ? Math.Clamp((-linear + MathF.Sqrt(linear * linear + 4 * quadratic * angularBudget)) /
                (2 * quadratic), 0, 1) : 0;
        for (int i = 0; i < doll.Bodies.Count; i++)
        {
            var body = doll.Bodies[i];
            Vector3 angular = body.AngularVelocity + torque * TackleShare(i, hit) * body.InverseInertia * scale;
            body.AngularVelocity = angular.Length() > Ragdoll.MaximumAngularSpeed
                ? Vector3.Normalize(angular) * Ragdoll.MaximumAngularSpeed : angular;
        }
    }

    private static Vector3 Normal(Vector3 delta, Vector3 closing) =>
        delta.LengthSquared() > 1e-8f ? Vector3.Normalize(delta) :
        closing.LengthSquared() > 1e-8f ? Vector3.Normalize(closing) : Vector3.UnitX;

    private static void Contact(Ragdoll a, Ragdoll b, int ai, int bi)
    {
        var ab = a.Bodies[ai]; var bb = b.Bodies[bi];
        var (normal, point) = Geometry(ab, bb, Velocity(a, ai, ab.Position) - Velocity(b, bi, bb.Position));
        Vector3 relative = Velocity(b, bi, point) - Velocity(a, ai, point);
        float closing = Vector3.Dot(relative, normal);
        if (closing >= -1e-5f) return; // Never attract separating bodies or reapply a canned kick.
        float impulse = Math.Min(MaximumImpulse,
            -(1 + Restitution) * closing * ImpulseScale / (EffectiveInverseMass(a, ai, point, normal) + EffectiveInverseMass(b, bi, point, normal)));
        Vector3 total = normal * impulse;
        Vector3 tangent = relative - normal * closing;
        if (tangent.LengthSquared() > 1e-8f)
        {
            Vector3 direction = Vector3.Normalize(tangent);
            float friction = Math.Min(ContactFriction * impulse, tangent.Length() /
                (EffectiveInverseMass(a, ai, point, direction) + EffectiveInverseMass(b, bi, point, direction)));
            total -= direction * friction;
        }
        if (total.Length() > MaximumImpulse) total = Vector3.Normalize(total) * MaximumImpulse;
        Apply(a, ai, -total, point);
        Apply(b, bi, total, point);
        a.ReactToContact(ai, -closing);
        b.ReactToContact(bi, -closing);
    }

    private static float Share(int i) => i == 1 ? ChestShare : 1 - ChestShare;
    private static Vector3 Velocity(Ragdoll doll, int hit, Vector3 point)
    {
        if (hit >= 3)
        {
            var b = doll.Bodies[hit];
            return b.LinearVelocity + Vector3.Cross(b.AngularVelocity, point - b.Position);
        }
        Vector3 velocity = Vector3.Zero;
        for (int i = 0; i < 2; i++)
        {
            var body = doll.Bodies[i];
            velocity += Share(i) * (body.LinearVelocity + Vector3.Cross(body.AngularVelocity, point - body.Position));
        }
        return velocity;
    }

    private static float EffectiveInverseMass(Ragdoll doll, int hit, Vector3 point, Vector3 direction)
    {
        if (hit >= 3)
        {
            var b = doll.Bodies[hit];
            return 1 / b.Mass + Vector3.Cross(point - b.Position, direction).LengthSquared() * b.InverseInertia;
        }
        float inverse = 0;
        for (int i = 0; i < 2; i++)
        {
            var body = doll.Bodies[i];
            float share = Share(i);
            inverse += share * share * (1 / body.Mass +
                Vector3.Cross(point - body.Position, direction).LengthSquared() * body.InverseInertia);
        }
        return inverse;
    }

    private static void Apply(Ragdoll doll, int hit, Vector3 impulse, Vector3 point)
    {
        if (hit >= 3) { doll.ApplyImpulse(new(hit, impulse, point)); return; }
        for (int i = 0; i < 2; i++) doll.ApplyImpulse(new(i, impulse * Share(i), point));
    }

    private static float Mass(Ragdoll doll) => doll.Bodies.Sum(b => b.Mass);
    private static void Translate(Ragdoll doll, Vector3 offset)
    {
        // Keep every capsule above its existing ground plane without changing the pose.
        float clearance = doll.Bodies.Min(b => b.Bottom - doll.GroundHeight);
        offset.Y = Math.Max(offset.Y, -Math.Max(0, clearance));
        foreach (var body in doll.Bodies) body.Position += offset;
    }
}
