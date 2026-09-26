using System.Numerics;

namespace RaylibTackleAlley.Game;

// Decision state is intentionally independent of animation/physics execution state.
public enum DefenderAiState { Pursuit, Approach, Breakdown, WrapCommitment, LungeCommitment, Recovery, Taunt }
public enum DefenderTackleChoice { None, Wrap, Lunge }

public readonly record struct DefenderApproach(
    DefenderAiState State, Vector3 PursuitTarget, Vector3 PredictedTarget, Vector3 Direction,
    float Distance, float BrakingDistance, float RequiredStoppingDistance, bool InReadyCone,
    bool CanBreakDown, bool Ready, string Reason);

/// <summary>Shared pure decision stages. No animation, physics, profile-ID dispatch or contact mutation.</summary>
public static class DefenderDecision
{
    public static DefenderApproach PlanApproach(DefenderProfile profile, TackleAlleyConfig config,
        Vector3 position, Vector3 carrierPosition, Vector3? predictedTarget, Vector3? carrierForward,
        float speed, float readySpeed, float wrapReach, bool wasPreparing)
    {
        Vector3 offset = carrierPosition - position;
        offset.Y = 0;
        float distance = offset.Length();
        Vector3 lead = (predictedTarget ?? carrierPosition) - carrierPosition;
        lead.Y = 0;
        lead *= profile.PursuitPredictionStrength;
        Vector3 prediction = carrierPosition + lead;
        float cap = distance * Math.Clamp(distance / config.LungeFullPredictionDistance, 0, 1) * .5f;
        lead = TackleAiming.Limit(lead, cap);
        bool inCone = carrierForward is not { } forward || InReadyCone(offset, forward, config.OpponentReadyHalfAngleDegrees);
        bool approaching = distance <= profile.ApproachDistance;
        if (approaching)
        {
            // Blend away from chasing the lead toward the goal-side lane. This affects
            // approach only; a tackle always aims at the reachable physical carrier.
            lead *= 1 - profile.ContainBias;
            if (inCone && carrierForward is { } heading)
            {
                heading.Y = 0;
                if (heading.LengthSquared() > .0001f)
                    lead += Vector3.Normalize(heading) * profile.ContainBias * Math.Min(distance * .25f, wrapReach);
            }
        }
        Vector3 pursuit = inCone ? carrierPosition + lead : carrierPosition;
        float braking = speed <= readySpeed ? 0 :
            config.ForwardDeceleration > 0 ? (speed * speed - readySpeed * readySpeed) /
                (2 * config.ForwardDeceleration) : float.PositiveInfinity;
        float stopping = wrapReach + speed * profile.ReactionTime + braking;
        bool canBreakDown = distance >= stopping;
        bool ready = inCone && (wasPreparing && distance <= profile.BreakdownExitDistance ||
            distance <= profile.BreakdownDistance && canBreakDown);
        Vector3 targetOffset = pursuit - position;
        targetOffset.Y = 0;
        if (targetOffset.LengthSquared() < config.OpponentPredictionArrivalDistance * config.OpponentPredictionArrivalDistance)
            targetOffset = offset;
        Vector3 direction = targetOffset.LengthSquared() > .0001f ? Vector3.Normalize(targetOffset) : Vector3.Zero;
        var state = ready && speed > readySpeed + config.OpponentWrapSpeedTolerance
            ? DefenderAiState.Breakdown : ready || approaching ? DefenderAiState.Approach : DefenderAiState.Pursuit;
        string reason = !inCone ? "Outside ready cone" : ready ? state == DefenderAiState.Breakdown
            ? "Braking to ready pace" : "Controlled approach" : !canBreakDown && approaching
            ? "Insufficient braking space" : approaching ? "Closing approach" : "Pursuing";
        return new(state, pursuit, prediction, direction, distance, braking, stopping, inCone, canBreakDown, ready, reason);
    }

    public static bool InReadyCone(Vector3 toCarrier, Vector3 carrierForward, float halfAngle)
    {
        carrierForward.Y = 0;
        if (carrierForward.LengthSquared() < .0001f || toCarrier.LengthSquared() < .0001f) return false;
        float cosine = Vector3.Dot(Vector3.Normalize(-toCarrier), Vector3.Normalize(carrierForward));
        return cosine + .000001f >= MathF.Cos(halfAngle * MathF.PI / 180);
    }

    public static DefenderTackleChoice SelectTackle(DefenderProfile profile, DefenderApproach approach,
        bool wasReady, bool wrapReachable, bool lungeReachable, float wrapReach, float heightRatio,
        float urgentLungeDistance) => SelectTackle(profile, approach, wasReady, wrapReachable,
            lungeReachable, wrapReach, heightRatio, urgentLungeDistance, out _);

    public static DefenderTackleChoice SelectTackle(DefenderProfile profile, DefenderApproach approach,
        bool wasReady, bool wrapReachable, bool lungeReachable, float wrapReach, float heightRatio,
        float urgentLungeDistance, out string reason)
    {
        if (approach.Distance > profile.TackleCommitDistance * heightRatio)
        {
            reason = "Outside profile commit range";
            return DefenderTackleChoice.None;
        }
        bool wrap = wasReady && wrapReachable && profile.WrapPreference > 0;
        bool lunge = lungeReachable && profile.LungePreference > 0 &&
            (wasReady ? approach.Distance > wrapReach || wrapReachable && profile.LungePreference > profile.WrapPreference
                : !approach.CanBreakDown || approach.Distance <= urgentLungeDistance);
        // Preferences never make an unreachable tackle legal. Ties preserve the old wrap-first rule.
        if (wrap && (!lunge || profile.WrapPreference >= profile.LungePreference))
        {
            reason = "Reachable wrap selected";
            return DefenderTackleChoice.Wrap;
        }
        if (lunge)
        {
            reason = "Reachable lunge selected";
            return DefenderTackleChoice.Lunge;
        }
        reason = !wrapReachable && !lungeReachable ? "No reachable contact" :
            wrapReachable && profile.WrapPreference == 0 || lungeReachable && profile.LungePreference == 0
                ? "Tackle disabled by preference" : "Waiting for controlled approach";
        return DefenderTackleChoice.None;
    }
}
