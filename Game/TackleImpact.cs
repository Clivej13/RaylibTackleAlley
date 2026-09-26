using System.Numerics;

namespace RaylibTackleAlley.Game;

public enum PhysicalTackleOutcome { Glancing, Staggered, ControlledWrap, Resisted, DrivenBack, BroughtDown }
public readonly record struct TackleImpact(float ImpactScore, float ResistanceScore, float ClosingSpeed,
    float Severity, float Transfer, float AngularScale, PhysicalTackleOutcome Outcome)
{
    // Normal points from defender to carrier. Tangential running is never counted as impact.
    public static TackleImpact Calculate(PlayerPhysicalAttributes defender, PlayerPhysicalAttributes carrier,
        Vector3 defenderVelocity, Vector3 carrierVelocity, Vector3 normal, float contactHeight,
        bool dive, float facingEffectiveness = 1f, float distance = 0f,
        Vector3? impactRelativeVelocity = null, TackleBodyRegion? region = null)
    {
        if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < 1e-8f ||
            !float.IsFinite(contactHeight) || !float.IsFinite(facingEffectiveness) ||
            !float.IsFinite(distance) || distance < 0)
            throw new ArgumentException("Invalid tackle geometry.");
        defender.Momentum(defenderVelocity); carrier.Momentum(carrierVelocity);
        normal = Vector3.Normalize(normal);
        Vector3 relative = impactRelativeVelocity ?? (defenderVelocity - carrierVelocity);
        if (!float.IsFinite(relative.LengthSquared())) throw new ArgumentException("Invalid impact velocity.");
        float closing = Math.Max(0, Vector3.Dot(relative, normal));
        float reach = dive ? defender.DiveReach : defender.WrapReach;
        float height = Math.Clamp(contactHeight / carrier.CollisionHeight, 0, 1);
        float position = Math.Clamp(facingEffectiveness, 0, 1) *
            (distance <= reach && contactHeight <= defender.CollisionHeight ? 1f : 0f);
        float effective = defender.TotalMass * (closing + (dive ? 0f : 2f));
        float impact = effective * defender.TackleForceMultiplier * (dive ? 1.1f : 1f) * position;
        // A stationary carrier still has weight-bearing support. Opposing normal momentum adds resistance.
        float resistance = carrier.TotalMass * (2f + Math.Max(0, -Vector3.Dot(carrierVelocity, normal))) *
            carrier.TackleResistanceMultiplier * carrier.BalanceSupportMultiplier;
        float severity = impact / resistance;
        var outcome = severity < .45f ? PhysicalTackleOutcome.Glancing :
            severity < .85f ? PhysicalTackleOutcome.Staggered :
            !dive && severity < 1.4f ? PhysicalTackleOutcome.ControlledWrap :
            severity < 1.7f ? PhysicalTackleOutcome.Resisted :
            severity < 2.5f ? PhysicalTackleOutcome.DrivenBack : PhysicalTackleOutcome.BroughtDown;
        // At rating 50 the strength transfer is exactly one. Bound transfer to [0,1.2], below the energy-conserving elastic limit of two.
        float transfer = Math.Clamp(defender.TackleForceMultiplier / carrier.TackleResistanceMultiplier *
            Math.Min(1, severity / .85f), 0, 1.2f);
        float angular = dive ? (region == TackleBodyRegion.ThighHip || height < .45f ? 1.35f :
            region == TackleBodyRegion.Head || height > .75f ? 1.15f : .8f) : 1f;
        return new(impact, resistance, closing, severity, transfer, angular, outcome);
    }
}
