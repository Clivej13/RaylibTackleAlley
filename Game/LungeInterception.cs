using System.Numerics;

namespace RaylibTackleAlley.Game;

/// <summary>Legacy bounded point prediction for standalone callers. Gameplay tackles use TackleAiming.</summary>
public static class LungeInterception
{

    // Validate the actual bounded aim, not an unlimited mathematical intercept.
    public static bool TryTarget(Vector3 defenderPosition, Vector3 carrierPosition,
        Vector3 carrierVelocity, float launchSpeed, float duration, out Vector3 target,
        TackleAlleyConfig? config = null)
    {
        config ??= new();
        target = Target(defenderPosition, carrierPosition, carrierVelocity, launchSpeed, config);
        Vector3 direction = target - defenderPosition;
        direction.Y = 0;
        if (direction.LengthSquared() < 1e-8f) return true;
        Vector3 relative = carrierPosition - defenderPosition;
        relative.Y = 0;
        carrierVelocity.Y = 0;
        Vector3 relativeVelocity = carrierVelocity - Vector3.Normalize(direction) * launchSpeed;
        float time = relativeVelocity.LengthSquared() > 1e-8f
            ? Math.Clamp(-Vector3.Dot(relative, relativeVelocity) / relativeVelocity.LengthSquared(), 0, duration)
            : 0;
        return (relative + relativeVelocity * time).LengthSquared() <=
            config.OpponentLungeContactDistance * config.OpponentLungeContactDistance;
    }

    public static Vector3 Target(Vector3 defenderPosition, Vector3 carrierPosition,
        Vector3 carrierVelocity, float launchSpeed, TackleAlleyConfig? config = null)
    {
        var _config = config ?? new TackleAlleyConfig();
        _config.ValidatePrediction();
        carrierVelocity.Y = 0;
        Vector3 offset = carrierPosition - defenderPosition;
        offset.Y = 0;
        if (launchSpeed <= .0001f || offset.LengthSquared() < 1e-8f ||
            carrierVelocity.LengthSquared() < 1e-8f) return carrierPosition;

        // |offset + velocity*t| = launchSpeed*t, using the earliest positive solution.
        float a = carrierVelocity.LengthSquared() - launchSpeed * launchSpeed;
        float b = 2 * Vector3.Dot(offset, carrierVelocity);
        float c = offset.LengthSquared();
        float time = float.PositiveInfinity;
        if (Math.Abs(a) < 1e-5f)
        {
            if (b < -1e-5f) time = -c / b;
        }
        else
        {
            float discriminant = b * b - 4 * a * c;
            if (discriminant >= 0)
            {
                float root = MathF.Sqrt(discriminant);
                float first = (-b - root) / (2 * a);
                float second = (-b + root) / (2 * a);
                if (first > 0) time = first;
                if (second > 0) time = Math.Min(time, second);
            }
        }
        float distance = offset.Length();
        // Near contact, reduce both the prediction horizon and the allowed lead distance.
        // The distance cap also handles sudden fast lateral movement and almost equal
        // chase speeds, where a time-only cap can still aim far beyond the carrier.
        float proximity = Math.Clamp(distance / _config.LungeFullPredictionDistance, 0, 1);
        float maximumLead = distance * _config.LungeMaximumLeadDistanceRatio * proximity;
        float maximumTime = Math.Min(_config.LungeMaximumPredictionSeconds * proximity,
            maximumLead / carrierVelocity.Length());

        // An unreachable runner still gets a bounded lead, never extra dive speed.
        if (!float.IsFinite(time)) time = distance / launchSpeed;
        return carrierPosition + carrierVelocity * Math.Clamp(time, 0, maximumTime);
    }
}
