using System.Numerics;

namespace RaylibTackleAlley.Game;

public readonly record struct ContactCapsule(Vector3 Start, Vector3 End, float Radius, int Body)
{
    public static ContactCapsule[] Capture(Ragdoll? pose) => pose is null ? [] :
        pose.Bodies.Select((b, i) => new ContactCapsule(b.SegmentStart, b.SegmentEnd, b.ContactRadius, i))
            .Where(b => b.Body != 2).ToArray();
    public TackleBodyRegion Region => Body switch
    {
        0 => TackleBodyRegion.Waist, 1 => TackleBodyRegion.Torso, 2 => TackleBodyRegion.Head,
        >= 7 => TackleBodyRegion.ThighHip, _ => TackleBodyRegion.Arm
    };
}

public readonly record struct SweptContact(float Time, Vector3 DefenderPoint, Vector3 CarrierPoint,
    Vector3 Normal, int DefenderBody, int CarrierBody, TackleBodyRegion DefenderRegion,
    TackleBodyRegion CarrierRegion, Vector3 RelativeVelocity)
{
    public Vector3 Point => (DefenderPoint + CarrierPoint) * .5f;
    public RagdollHit Hit => new(DefenderBody, CarrierBody, Point, Normal);
}

/// <summary>Continuous collision for linearly moving capsule endpoints over a simulation frame.</summary>
public static class SweptTackleContact
{
    public const float SpatialTolerance = .00001f;
    public static SweptContact? Find(IReadOnlyList<ContactCapsule> previousDefender,
        IReadOnlyList<ContactCapsule> defender, IReadOnlyList<ContactCapsule> previousCarrier,
        IReadOnlyList<ContactCapsule> carrier, float dt)
    {
        if (dt <= 0 || !float.IsFinite(dt)) return null;
        SweptContact? earliest = null;
        foreach (var a in defender)
        foreach (var b in carrier)
        {
            int ai = FindBody(previousDefender, a.Body), bi = FindBody(previousCarrier, b.Body);
            if (ai < 0 || bi < 0) continue;
            var a0 = previousDefender[ai]; var b0 = previousCarrier[bi];
            float speedBound = Math.Max(Vector3.Distance(a0.Start, a.Start), Vector3.Distance(a0.End, a.End)) +
                Math.Max(Vector3.Distance(b0.Start, b.Start), Vector3.Distance(b0.End, b.End));
            float radius = Math.Max(a0.Radius, a.Radius) + Math.Max(b0.Radius, b.Radius);
            float Gap(float t)
            {
                Closest(Vector3.Lerp(a0.Start, a.Start, t), Vector3.Lerp(a0.End, a.End, t),
                    Vector3.Lerp(b0.Start, b.Start, t), Vector3.Lerp(b0.End, b.End, t),
                    out var pa, out var pb, out _, out _);
                return Vector3.Distance(pa, pb) - radius;
            }
            // Chronological interval search. The endpoint-motion Lipschitz bound safely prunes empty
            // intervals, including fast passes with no overlap at either rendered endpoint.
            float? Search(float lo, float hi, int depth)
            {
                if (Gap(lo) <= SpatialTolerance) return lo;
                float mid = (lo + hi) * .5f;
                if (Gap(mid) > speedBound * (hi - lo) * .5f + SpatialTolerance) return null;
                if (depth >= 24 || speedBound * (hi - lo) <= SpatialTolerance) return mid;
                return Search(lo, mid, depth + 1) ?? Search(mid, hi, depth + 1);
            }
            float? time = Search(0, earliest?.Time ?? 1, 0);
            if (time is not { } t || earliest is { } old && t >= old.Time) continue;
            Closest(Vector3.Lerp(a0.Start, a.Start, t), Vector3.Lerp(a0.End, a.End, t),
                Vector3.Lerp(b0.Start, b.Start, t), Vector3.Lerp(b0.End, b.End, t),
                out var ap, out var bp, out float sa, out float sb);
            Vector3 velocity = (Vector3.Lerp(a.Start - a0.Start, a.End - a0.End, sa) -
                Vector3.Lerp(b.Start - b0.Start, b.End - b0.End, sb)) / dt;
            Vector3 normal = bp - ap;
            normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) :
                velocity.LengthSquared() > 1e-12f ? Vector3.Normalize(velocity) : Vector3.UnitZ;
            earliest = new(t, ap + normal * a.Radius, bp - normal * b.Radius, normal,
                a.Body, b.Body, a.Region, b.Region, velocity);
        }
        return earliest;
    }

    private static int FindBody(IReadOnlyList<ContactCapsule> capsules, int body)
    {
        for (int i = 0; i < capsules.Count; i++) if (capsules[i].Body == body) return i;
        return -1;
    }

    public static void Closest(Vector3 p, Vector3 pEnd, Vector3 q, Vector3 qEnd,
        out Vector3 pa, out Vector3 pb, out float s, out float t)
    {
        Vector3 u = pEnd - p, v = qEnd - q, r = p - q;
        float aa = Vector3.Dot(u, u), bb = Vector3.Dot(u, v), cc = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, r), e = Vector3.Dot(v, r);
        s = t = 0;
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
}
