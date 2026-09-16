using System.Numerics;

namespace RaylibTackleAlley.Game;

public static class LungeTackleOutcome
{
    public const float ImpulseMass = 8f;
    public const float MaximumImpulse = 72f;
    public const float ChestShare = .6f;
    public static Vector3 Impulse(Vector3 committedVelocity)
    {
        committedVelocity.Y = 0;
        float speed = committedVelocity.Length();
        return speed > .00001f ? committedVelocity / speed * Math.Min(MaximumImpulse, speed * ImpulseMass) : Vector3.Zero;
    }
    public static bool Confirm(Opponent defender, BallCarrier carrier)
    {
        if (defender.State != DefenderState.LungeTackle || !carrier.CanActivateRagdoll ||
            (!defender.Ragdoll.IsActive && !defender.CanActivateRagdoll)) return false;
        Vector3 impulse = Impulse(defender.Velocity);
        // Current pose and actual velocity are captured before adding equal/opposite contact impulses.
        carrier.ActivateRagdoll();
        if (!defender.Ragdoll.IsActive) defender.ActivateRagdoll();
        Apply(carrier.Ragdoll, impulse);
        Apply(defender.Ragdoll, -impulse);
        return true;
    }
    private static void Apply(Ragdoll ragdoll, Vector3 impulse)
    {
        ragdoll.ApplyImpulse(new(1, impulse * ChestShare));
        ragdoll.ApplyImpulse(new(0, impulse * (1 - ChestShare)));
    }
}
