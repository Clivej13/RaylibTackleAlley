using System.Numerics;
using RaylibGameFramework.Input;

namespace RaylibTackleAlley.Game;

// Dedicated camera actions; never derive look from movement or evasion input.
// Positive X looks right; positive Y pitches downward.
public readonly record struct CameraLookInput(Vector2 Rate)
{
    public static CameraLookInput Capture(InputController input) => new(new(
        Math.Clamp(Math.Abs(input.GetValue("CameraLookRight")) - Math.Abs(input.GetValue("CameraLookLeft")), -1, 1),
        Math.Clamp(Math.Abs(input.GetValue("CameraLookDown")) - Math.Abs(input.GetValue("CameraLookUp")), -1, 1)));
}
