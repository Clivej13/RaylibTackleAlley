using System.Numerics;
using RaylibGameFramework.Input;

namespace RaylibTackleAlley.Game;

// Separate analogue movement from digital bindings before applying the response curve.
internal sealed class MovementStickInput
{
    private static readonly string[] Actions = ["MoveLeft", "MoveRight", "MoveForward", "MoveBackward"];
    private readonly List<(string Device, string Input, string Action)> _bindings = [];
    private InputController? _directions;

    public (Vector2 Direction, bool IsStick) Read(InputController input, float sensitivity)
    {
        var bindings = new List<(string Device, string Input, string Action)>();
        foreach (string action in Actions)
        foreach (InputDeviceFamily family in Enum.GetValues<InputDeviceFamily>())
            if (input.GetBinding(action, family) is { } binding)
                bindings.Add((binding.Device, binding.Input, action));
        if (_directions is null || !_bindings.SequenceEqual(bindings))
        {
            _bindings.Clear();
            _bindings.AddRange(bindings);
            _directions = new InputController(new InputConfig
            {
                Bindings = bindings.Select(b => new InputBinding
                {
                    Device = b.Device, Input = b.Input,
                    Action = (b.Device.Equals("GamepadAxis", StringComparison.OrdinalIgnoreCase) ? "Stick." : "Digital.") + b.Action
                }).ToList()
            });
        }
        _directions.Update();
        bool stickActive = false;
        float ReadAction(string action)
        {
            float stick = Math.Abs(_directions.GetValue("Stick." + action));
            float digital = Math.Abs(_directions.GetValue("Digital." + action));
            float curved = MathF.Pow(Math.Min(1, stick), 1 / sensitivity);
            stickActive |= curved > digital;
            return Math.Max(curved, digital);
        }
        Vector2 direction = new(ReadAction("MoveRight") - ReadAction("MoveLeft"),
            ReadAction("MoveBackward") - ReadAction("MoveForward"));
        return (direction, stickActive);
    }
}
