using System.Numerics;

namespace RaylibTackleAlley.Game;

public static class LungeTackleOutcome
{
    public static bool Confirm(Opponent defender, BallCarrier carrier, SweptContact? swept = null)
    {
        if (defender.ContactCooldown > 0 || !carrier.CanActivateRagdoll ||
            (!defender.Ragdoll.IsActive && !defender.CanActivateRagdoll) ||
            (swept is null && !defender.HasBodyContact(carrier))) return false;
        var dp = defender.ContactRagdoll;
        var cp = carrier.ContactPose()!;
        // Use the actual nearest capsule surface, including low airborne contact.
        var contact = swept?.Hit ?? RagdollContact.FindContact(dp, cp, defender.Velocity - carrier.Velocity);
        bool dive = defender.State == DefenderState.LungeTackle || defender.Ragdoll.IsActive;
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ,
            Matrix4x4.CreateRotationY(defender.FacingYawDegrees * MathF.PI / 180));
        Vector3 toward = swept?.Normal ?? (carrier.Position - defender.Position); toward.Y = 0;
        float facing = toward.LengthSquared() < 1e-8f ? 1f :
            Math.Clamp((Vector3.Dot(forward, Vector3.Normalize(toward)) + 1f) * .5f, 0, 1);
        float distance = Vector2.Distance(new(defender.Position.X, defender.Position.Z),
            new(carrier.Position.X, carrier.Position.Z));
        var impact = TackleImpact.Calculate(defender.Physical, carrier.Physical,
            defender.Ragdoll.IsActive ? defender.Momentum / defender.Physical.TotalMass : defender.Velocity,
            carrier.Velocity, contact.Normal, contact.Point.Y - cp.GroundHeight, dive, facing,
            swept.HasValue ? 0 : distance, swept?.RelativeVelocity, swept?.CarrierRegion);
        defender.LastTackle = carrier.LastTackle = impact;
        defender.ContactCooldown = .4f;
        if (impact.Outcome is PhysicalTackleOutcome.Glancing or PhysicalTackleOutcome.Staggered)
        {
            float impulse = Math.Min(defender.Ragdoll.Config.ContactMaximumTackleImpulse *
                Math.Min(defender.Physical.ImpulseLimitScale, carrier.Physical.ImpulseLimitScale),
                impact.ClosingSpeed / (1 / defender.Physical.TotalMass + 1 / carrier.Physical.TotalMass) * impact.Transfer);
            carrier.ApplyUprightContact(contact.Normal * impulse);
            defender.ApplyContactImpulse(-contact.Normal * impulse);
            return false;
        }
        carrier.ActivateRagdoll();
        if (!defender.Ragdoll.IsActive) defender.ActivateRagdoll();
        var hit = RagdollContact.Resolve(defender.Ragdoll, carrier.Ragdoll, confirmedContact: true, tackle: impact, impactContact: contact);
        defender.BeginTackleStruggle(hit?.DefenderBody);
        carrier.BeginTackleStruggle(impact.Outcome is PhysicalTackleOutcome.ControlledWrap or PhysicalTackleOutcome.Resisted
            ? null : hit?.CarrierBody);
        if (!dive)
            defender.WrapRemaining = Math.Clamp(.65f * defender.Physical.TackleForceMultiplier /
                carrier.Physical.TackleResistanceMultiplier * defender.Physical.TotalMass / carrier.Physical.TotalMass, .2f, 1.2f);
        carrier.UpdatePhysicsAndRecovery(0);
        return true;
    }
}
