namespace RaylibTackleAlley.Game;

// Shared scalar integration only; steering and animation remain character-owned.
internal static class SpeedRamp
{
    public static (float Speed, float Distance) Advance(
        float current, float target, float acceleration, float deceleration, float deltaTime)
    {
        float dt = Math.Max(0f, deltaTime);
        float rate = target >= current ? acceleration : deceleration;
        if (!float.IsFinite(rate) || rate <= 0f)
            throw new ArgumentOutOfRangeException(nameof(acceleration), "Speed ramp rates must be finite and positive.");
        float rampTime = Math.Min(dt, Math.Abs(target - current) / rate);
        float next = current + Math.Sign(target - current) * rate * rampTime;
        // Integrate the ramp plus any remaining time at target, including long frames.
        float distance = (current + next) * 0.5f * rampTime + target * (dt - rampTime);
        return (next, distance);
    }
}
