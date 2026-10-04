using System.Numerics;

namespace RaylibTackleAlley.Game;

// World-space heading convention: zero = -Z, positive = toward +X.
public static class DirectionalMovement
{
    public static float Yaw(Vector2 direction) => MathF.Atan2(direction.X, -direction.Y) * 180 / MathF.PI;
    public static float Delta(float from, float to) => ((to - from) % 360 + 540) % 360 - 180;
    public static float TurnTowards(float from, float to, float maximum) =>
        from + Math.Clamp(Delta(from, to), -maximum, maximum);
    public static Vector3 Forward(float yaw)
    {
        float radians = yaw * MathF.PI / 180;
        return new(MathF.Sin(radians), 0, -MathF.Cos(radians));
    }
    public static Vector3 Right(float yaw)
    {
        var forward = Forward(yaw);
        return new(-forward.Z, 0, forward.X);
    }
}
