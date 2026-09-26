using System.Numerics;

namespace RaylibTackleAlley.Game;

public enum TackleBodyRegion { Waist, PelvisLowerChest, ThighHip, Torso, Arm, Head }
public readonly record struct TackleTarget(Vector3 Point, float Radius, TackleBodyRegion Region);
public readonly record struct ContactSolution(bool Reachable, Vector3 ContactPoint, float ContactTime,
    Vector3 LaunchDirection, TackleBodyRegion Region, float Confidence);
public readonly record struct ContactCandidate(Vector3 Point, float Time, bool Reachable);

/// <summary>Observed movement history, independent of render frequency. Surprises lower confidence immediately.</summary>
public sealed class TackleMotionObservation
{
    private Vector3? _previous;
    public Vector3 Acceleration { get; private set; }
    public float Confidence { get; private set; } = 1;
    public void Reset() { _previous = null; Acceleration = Vector3.Zero; Confidence = 1; }
    public void Observe(Vector3 velocity, float dt, bool evading, TackleAlleyConfig config)
    {
        velocity.Y = 0;
        // Zero-time intent updates are not movement observations.
        if (_previous.HasValue && dt <= 1e-6f) return;
        if (dt > 0 && _previous is { } previous)
        {
            Vector3 acceleration = (velocity - previous) / dt;
            float instability = acceleration.Length() / Math.Max(1, config.TackleAccelerationPredictionLimit);
            float stability = 1 / (1 + instability);
            if (evading) stability = Math.Min(stability, .15f);
            Confidence = Math.Min(stability, 1 - (1 - Confidence) * MathF.Exp(-6 * dt));
            Acceleration = TackleAiming.Limit(acceleration, config.TackleAccelerationPredictionLimit);
        }
        else if (evading) Confidence = Math.Min(Confidence, .15f);
        _previous = velocity;
    }
}

public static class TackleAiming
{
    public static Vector3 Limit(Vector3 value, float maximum) =>
        value.LengthSquared() > maximum * maximum ? Vector3.Normalize(value) * maximum : value;

    // Use the animated, scaled capsule centreline when available. Headless callers use the same physical dimensions.
    public static TackleTarget Target(Vector3 position, PlayerPhysicalAttributes physical,
        TackleBodyRegion region = TackleBodyRegion.PelvisLowerChest, Ragdoll? pose = null)
    {
        Vector3 pelvis = pose is { Bodies.Count: > 1 } ? pose.Bodies[0].Position :
            position + Vector3.UnitY * physical.TackleContactHeight;
        Vector3 chest = pose is { Bodies.Count: > 1 } ? pose.Bodies[1].Position :
            position + Vector3.UnitY * physical.CollisionHeight * .72f;
        return region switch
        {
            TackleBodyRegion.Waist => new(pelvis, physical.PelvisRadius, region),
            TackleBodyRegion.ThighHip => new(pose is { Bodies.Count: > 7 } ?
                Vector3.Lerp(pelvis, pose.Bodies[7].Position, .65f) :
                position + Vector3.UnitY * physical.CollisionHeight * .4f,
                physical.BodyPartContactRadii[7], region),
            TackleBodyRegion.Torso => new(chest, physical.TorsoRadius, region),
            _ => new(Vector3.Lerp(pelvis, chest, .3f), Math.Max(physical.PelvisRadius, physical.TorsoRadius), region)
        };
    }

    public static float Horizon(float duration, float confidence, float lead, TackleAlleyConfig config)
    {
        float maximum = Math.Min(duration, config.LungeMaximumPredictionSeconds);
        return Math.Min(maximum, lead + Math.Max(0, maximum - lead) * Math.Clamp(confidence, 0, 1));
    }

    public static float Reach(float speed, float acceleration, float launchSpeed, float time)
    {
        float start = Math.Min(speed, launchSpeed);
        float ramp = acceleration > 0 ? Math.Min(time, (launchSpeed - start) / acceleration) : time;
        return start * ramp + .5f * acceleration * ramp * ramp + launchSpeed * (time - ramp);
    }

    public static ContactSolution Solve(Vector3 defenderPosition, Vector3 defenderVelocity, Vector3 facing,
        Vector3 carrierPosition, Vector3 carrierVelocity, Vector3 carrierAcceleration,
        PlayerPhysicalAttributes defender, TackleTarget target, float acceleration, float launchSpeed,
        float duration, float confidence, TackleAlleyConfig config, float elapsed = 0,
        List<ContactCandidate>? candidates = null, float? currentSpeed = null)
    {
        config.ValidateTackleAiming();
        candidates?.Clear();
        Vector3 relativePosition = carrierPosition - defenderPosition;
        Vector3 relativeVelocity = carrierVelocity - defenderVelocity;
        relativePosition.Y = relativeVelocity.Y = 0;
        float closing = relativePosition.LengthSquared() > 1e-8f ?
            Math.Max(0, -Vector3.Dot(relativeVelocity, Vector3.Normalize(relativePosition))) : 0;
        float envelope = Math.Clamp(config.OpponentLungeContactDistance * defender.HeightRatio +
            closing * config.OpponentLungeAnimationLeadSeconds, defender.DiveReach,
            config.OpponentMaximumLungeReachDistance * defender.HeightRatio);
        var failed = new ContactSolution(false, target.Point, 0, Vector3.Zero, target.Region, confidence);
        if (elapsed == 0 && relativePosition.Length() > envelope) return failed;
        if (confidence < config.TackleMinimumPredictionConfidence) return failed;
        float lead = Math.Max(0, config.OpponentLungeAnimationLeadSeconds - elapsed);
        float maximum = Horizon(duration, confidence, lead, config);
        if (maximum < lead) return failed;
        carrierAcceleration = Limit(carrierAcceleration, config.TackleAccelerationPredictionLimit) * confidence;
        defenderVelocity.Y = 0;
        float radius = defender.TorsoRadius + target.Radius;
        ContactSolution Evaluate(float t)
        {
            Vector3 predicted = target.Point + carrierVelocity * t + .5f * carrierAcceleration * t * t;
            Vector3 delta = predicted - (defenderPosition + Vector3.UnitY * defender.TackleContactHeight);
            Vector3 horizontal = new(delta.X, 0, delta.Z);
            Vector3 direction = horizontal.LengthSquared() > 1e-8f ? Vector3.Normalize(horizontal) : facing;
            bool angle = Vector3.Dot(direction, Vector3.Normalize(facing)) >=
                MathF.Cos(config.OpponentLungeReachAngleDegrees * MathF.PI / 180) - 1e-6f;
            float heightGap = Math.Max(0, Math.Abs(delta.Y) - radius);
            float reach = Reach(Math.Min(launchSpeed, (currentSpeed ?? defenderVelocity.Length()) + acceleration * lead), acceleration, launchSpeed, t) + radius;
            bool reachable = angle && horizontal.LengthSquared() + heightGap * heightGap <= reach * reach;
            return new(reachable, predicted - direction * target.Radius, t, direction, target.Region, confidence);
        }
        float previous = lead;
        for (int i = 0; i <= config.TacklePredictionIterations; i++)
        {
            float t = lead + (maximum - lead) * i / config.TacklePredictionIterations;
            var solution = Evaluate(t);
            candidates?.Add(new(solution.ContactPoint, t, solution.Reachable));
            if (solution.Reachable)
            {
                // Refine the first bracket, not the closest approach or a later root.
                float lo = previous, hi = t;
                for (int j = 0; j < 16; j++)
                {
                    float mid = (lo + hi) * .5f;
                    if (Evaluate(mid).Reachable) hi = mid; else lo = mid;
                }
                return Evaluate(hi);
            }
            previous = t;
        }
        return failed;
    }

    public static bool TryWrap(Vector3 position, Vector3 velocity, Vector3 facing, Vector3 carrierVelocity,
        PlayerPhysicalAttributes defender, TackleTarget target, TackleAlleyConfig config, out Vector3 contact)
    {
        Vector3 delta = target.Point - (position + Vector3.UnitY * defender.TackleContactHeight);
        Vector3 horizontal = new(delta.X, 0, delta.Z);
        Vector3 direction = horizontal.LengthSquared() > 1e-8f ? Vector3.Normalize(horizontal) : facing;
        contact = target.Point - direction * target.Radius;
        // WrapReach includes neutral torso/arm reach; adjust the surface envelope for both actual radii.
        float reach = defender.WrapReach + target.Radius + defender.PelvisRadius -
            config.ContactPelvisRadius * 2;
        Vector3 relative = carrierVelocity - velocity;
        float separating = Vector3.Dot(relative, direction);
        return delta.Length() <= reach && Math.Abs(delta.Y) <= defender.CollisionHeight * .35f &&
            Vector3.Dot(direction, facing) >= MathF.Cos(config.OpponentWrapContainmentAngleDegrees * MathF.PI / 180) &&
            separating * config.OpponentLungeAnimationLeadSeconds <= Math.Max(0, reach - delta.Length()) &&
            Math.Abs(Vector3.Dot(relative, new Vector3(-direction.Z, 0, direction.X))) *
                config.OpponentLungeAnimationLeadSeconds <= reach;
    }

    public static Vector3 Correct(Vector3 initial, Vector3 current, Vector3 desired, float elapsed,
        float dt, float duration, int agility, TackleAlleyConfig config)
    {
        float window = duration * config.LungeCorrectionWindowFraction;
        if (window <= 0 || elapsed >= window - 1e-7f || dt <= 0 || desired.LengthSquared() < 1e-8f ||
            Vector3.DistanceSquared(current, desired) < 1e-12f) return current;
        float end = Math.Min(window, elapsed + dt);
        // Exact integral of the linear fade makes the angular budget independent of frame partition.
        float budget = config.LungeCorrectionRateDegrees * PlayerMovementAttributes.RatingMultiplier(agility, .9f, 1.1f) *
            ((end - elapsed) - (end * end - elapsed * elapsed) / (2 * window));
        float origin = MathF.Atan2(initial.X, initial.Z);
        float from = MathF.IEEERemainder(MathF.Atan2(current.X, current.Z) - origin, MathF.Tau);
        float to = Math.Clamp(MathF.IEEERemainder(MathF.Atan2(desired.X, desired.Z) - origin, MathF.Tau),
            -config.LungeMaximumCorrectionDegrees * MathF.PI / 180, config.LungeMaximumCorrectionDegrees * MathF.PI / 180);
        float angle = origin + from + Math.Clamp(to - from, -budget * MathF.PI / 180, budget * MathF.PI / 180);
        return new(MathF.Sin(angle), 0, MathF.Cos(angle));
    }
}
