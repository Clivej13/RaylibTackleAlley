using System.Numerics;
using RaylibGameFramework.Input;

namespace RaylibTackleAlley.Game;

// One immutable world-space command per rendered frame, reused by every substep.
public readonly record struct PlayerInputSnapshot(
    float Lateral, float Backward, float Sprint,
    Vector2 Gesture, bool GestureIsMouse, float Forward = 0, float Slow = 0, bool MovementIsStick = false)
{
    public Vector2 Movement => Vector2.Clamp(new(Lateral, Math.Abs(Backward) - Math.Abs(Forward)), new(-1), new(1));
    public PlayerInputSnapshot RelativeToYaw(float yaw)
    {
        Vector2 movement = Movement;
        if (movement.LengthSquared() > 1) movement = Vector2.Normalize(movement);
        Vector3 world = DirectionalMovement.Right(yaw) * movement.X
            - DirectionalMovement.Forward(yaw) * movement.Y;
        return this with { Lateral = world.X, Backward = Math.Max(0, world.Z), Forward = Math.Max(0, -world.Z) };
    }

    internal static PlayerInputSnapshot Capture(InputController input, RightStickInput gestures,
        MovementStickInput movementInput, float sensitivity)
    {
        var movement = movementInput.Read(input, sensitivity);
        var gesture = gestures.ReadFrame(input);
        return new(movement.Direction.X, Math.Max(0, movement.Direction.Y), input.GetValue("Sprint"),
            gesture.Direction, gesture.IsMouse, Math.Max(0, -movement.Direction.Y), input.GetValue("Slow"),
            movement.IsStick);
    }
}
