namespace RaylibTackleAlley.Game;

public static class LungeTackleOutcome
{
    public static bool Confirm(Opponent defender, BallCarrier carrier)
    {
        if (defender.State != DefenderState.LungeTackle || !carrier.CanActivateRagdoll ||
            (!defender.Ragdoll.IsActive && !defender.CanActivateRagdoll) ||
            !defender.HasBodyContact(carrier)) return false;
        // Capture current poses and actual velocities before resolving geometric contact.
        carrier.ActivateRagdoll();
        if (!defender.Ragdoll.IsActive) defender.ActivateRagdoll();
        var hit = RagdollContact.Resolve(defender.Ragdoll, carrier.Ragdoll, confirmedContact: true);
        defender.BeginTackleStruggle(hit?.DefenderBody);
        carrier.BeginTackleStruggle(hit?.CarrierBody);
        // Separation moves the pose immediately; keep the camera root and football hand in sync.
        carrier.UpdatePhysicsAndRecovery(0);
        return true;
    }
}
