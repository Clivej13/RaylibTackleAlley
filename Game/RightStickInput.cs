using System.Numerics;
using RaylibGameFramework.Input;

namespace RaylibTackleAlley.Game;

// Keep raw mouse pixels separate from unit-range bindings until after normalization.
// The public actions remain shared, including any changes made by the Controls menu.
internal sealed class RightStickInput
{
    private static readonly string[] Actions =
        ["RightStickLeft", "RightStickRight", "RightStickForward", "RightStickBack"];
    private readonly TackleAlleyConfig _config;
    private readonly List<(string Device, string Input, string Action)> _bindings = [];
    private InputController? _directions;
    private bool _ignoreNextMouseDelta = true;

    public RightStickInput(TackleAlleyConfig config) => _config = config;

    public void IgnoreNextMouseDelta() => _ignoreNextMouseDelta = true;

    public (Vector2 Direction, bool IsMouse) Read(InputController input)
    {
        var bindings = new List<(string Device, string Input, string Action)>();
        foreach (string action in Actions)
        foreach (InputDeviceFamily family in Enum.GetValues<InputDeviceFamily>())
        {
            InputBinding? binding = input.GetBinding(action, family);
            if (binding != null)
                bindings.Add((binding.Device, binding.Input, action));
        }

        if (_directions == null || !_bindings.SequenceEqual(bindings))
        {
            _bindings.Clear();
            _bindings.AddRange(bindings);
            _directions = new InputController(new InputConfig
            {
                Bindings = bindings.Select(binding => new InputBinding
                {
                    Device = binding.Device,
                    Input = binding.Input,
                    Action = Prefix(binding.Device) + binding.Action
                }).ToList()
            });
        }

        _directions.Update();
        Vector2 mouse = ReadDirections("Mouse.", normalizeMouse: true);
        Vector2 other = ReadDirections("Other.", normalizeMouse: false);
        if (_ignoreNextMouseDelta)
        {
            mouse = Vector2.Zero;
            _ignoreNextMouseDelta = false;
        }
        // Compare in stick units, so a pixel of mouse noise cannot swamp a controller.
        return mouse.LengthSquared() > other.LengthSquared() ? (mouse, true) : (other, false);
    }

    private static string Prefix(string device) =>
        device.Equals("MouseAxis", StringComparison.OrdinalIgnoreCase) ? "Mouse." : "Other.";

    private Vector2 ReadDirections(string prefix, bool normalizeMouse)
    {
        float Read(string action)
        {
            float value = _directions!.GetValue(prefix + action);
            return normalizeMouse ? NormalizeMouseDelta(value, _config.MouseGestureSensitivity, _config.MouseGestureNoisePixels) : value;
        }

        return new Vector2(
            CombineDirections(Read(Actions[0]), Read(Actions[1])),
            CombineDirections(Read(Actions[2]), Read(Actions[3])));
    }

    internal static float NormalizeMouseDelta(float delta, float sensitivity, float noisePixels)
    {
        if (!float.IsFinite(delta) || !float.IsFinite(sensitivity) || sensitivity <= 0f)
            return 0f;
        // Filter mouse noise before applying sensitivity.
        if (Math.Abs(delta) <= noisePixels)
            return 0f;
        return Math.Clamp(delta * sensitivity, -1f, 1f);
    }

    private static float CombineDirections(float negative, float positive) =>
        Math.Clamp(Math.Abs(positive) - Math.Abs(negative), -1f, 1f);
}
