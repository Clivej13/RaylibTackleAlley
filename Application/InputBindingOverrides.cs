using Raylib_cs;
using RaylibGameFramework.Input;

namespace RaylibTackleAlley.Application;

// Rebinding replaces the first binding for an action/device family, just as the
// Input package does. Additional defaults (e.g. D-pad AND stick menus) stay intact.
internal static class InputBindingOverrides
{
    private static bool Gamepad(InputBinding binding) =>
        binding.Device.StartsWith("Gamepad", StringComparison.OrdinalIgnoreCase);

    private static bool SameSlot(InputBinding a, InputBinding b) =>
        string.Equals(a.Action, b.Action, StringComparison.OrdinalIgnoreCase) && Gamepad(a) == Gamepad(b);

    private static bool SameValue(InputBinding a, InputBinding b) =>
        string.Equals(a.Device, b.Device, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Input, b.Input, StringComparison.OrdinalIgnoreCase);

    public static bool Apply(InputConfig defaults, IEnumerable<InputBinding> saved)
    {
        bool repaired = false;
        var visited = new List<InputBinding>();
        foreach (var binding in saved)
        {
            if (!Valid(binding)) { repaired = true; continue; }
            // Legacy files include secondary bindings for the same family.
            if (visited.Any(b => SameSlot(b, binding))) continue;
            visited.Add(binding);
            int index = defaults.Bindings.FindIndex(b => SameSlot(b, binding));
            if (index >= 0) defaults.Bindings[index] = binding;
            else if (defaults.Bindings.Any(b => string.Equals(b.Action, binding.Action, StringComparison.OrdinalIgnoreCase)))
                defaults.Bindings.Add(binding); // A rebind may add a previously unbound device family.
            else repaired = true; // Removed/unknown actions are not resurrected.
        }
        return repaired;
    }

    public static List<InputBinding> Capture(IReadOnlyList<InputBinding> defaults, InputConfig current)
    {
        var overrides = new List<InputBinding>();
        var visited = new List<InputBinding>();
        foreach (var binding in current.Bindings)
        {
            if (!Valid(binding) || visited.Any(b => SameSlot(b, binding))) continue;
            visited.Add(binding);
            var original = defaults.FirstOrDefault(b => SameSlot(b, binding));
            if (original is not null ? !SameValue(binding, original) :
                defaults.Any(b => string.Equals(b.Action, binding.Action, StringComparison.OrdinalIgnoreCase)))
                overrides.Add(binding);
        }
        return overrides;
    }

    private static bool NamedEnum<T>(string value, T excluded) where T : struct, Enum =>
        Enum.GetNames<T>().Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) &&
        Enum.TryParse<T>(value, true, out var parsed) && !parsed.Equals(excluded);

    private static bool Valid(InputBinding? binding)
    {
        if (binding is null || string.IsNullOrWhiteSpace(binding.Action) ||
            string.IsNullOrWhiteSpace(binding.Device) || string.IsNullOrWhiteSpace(binding.Input)) return false;
        string value = binding.Input;
        return binding.Device.ToLowerInvariant() switch
        {
            "keyboard" => NamedEnum(value, KeyboardKey.Null),
            "mouse" => Enum.GetNames<MouseButton>().Any(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase)),
            "mouseaxis" => value.ToUpperInvariant() is "MOUSEXNEGATIVE" or "MOUSEXPOSITIVE" or "MOUSEYNEGATIVE" or "MOUSEYPOSITIVE",
            "gamepadbutton" => NamedEnum(value, GamepadButton.Unknown),
            "gamepadaxis" => value.ToUpperInvariant() is "LEFTXNEGATIVE" or "LEFTXPOSITIVE" or "LEFTYNEGATIVE" or "LEFTYPOSITIVE"
                or "RIGHTXNEGATIVE" or "RIGHTXPOSITIVE" or "RIGHTYNEGATIVE" or "RIGHTYPOSITIVE"
                or "LEFTTRIGGER" or "RIGHTTRIGGER",
            _ => false
        };
    }
}
