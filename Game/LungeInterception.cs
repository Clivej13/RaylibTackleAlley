using System.Numerics;

namespace RaylibTackleAlley.Game;

/// <summary>Predict one launch target from observed velocity; the airborne dive never homes.</summary>
public static class LungeInterception
{
    public const float MaximumPredictionSeconds = .55f;
    public const float FullPredictionDistance = 3f;
    public const float MaximumLeadDistanceRatio = 1f;

    public static Vector3 Target(Vector3 defenderPosition, Vector3 carrierPosition,
        Vector3 carrierVelocity, float launchSpeed)
    {
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
        float proximity = Math.Clamp(distance / FullPredictionDistance, 0, 1);
        float maximumLead = distance * MaximumLeadDistanceRatio * proximity;
        float maximumTime = Math.Min(MaximumPredictionSeconds * proximity,
            maximumLead / carrierVelocity.Length());

        // An unreachable runner still gets a bounded lead, never extra dive speed.
        if (!float.IsFinite(time)) time = distance / launchSpeed;
        return carrierPosition + carrierVelocity * Math.Clamp(time, 0, maximumTime);
    }
}
