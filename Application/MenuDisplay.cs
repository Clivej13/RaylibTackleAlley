using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;

namespace RaylibTackleAlley.Application;

internal static class MenuDisplay
{
    internal static string GetBindingDisplayName(InputBinding? binding)
    {
        string name = MenuManager.GetBindingDisplayName(binding);
        // Menus 0.1.2 supplies friendly axes/buttons but leaves modifier keys unspaced.
        return binding?.Device == "Keyboard" ? name switch
        {
            "LeftShift" => "Left Shift",
            "RightShift" => "Right Shift",
            "LeftControl" => "Left Control",
            "RightControl" => "Right Control",
            "LeftAlt" => "Left Alt",
            "RightAlt" => "Right Alt",
            _ => name
        } : name;
    }

    public static void Draw(MenuManager menu, InputConfig input)
    {
        // This release has no display formatter hook. GetBinding reads the config,
        // while gameplay uses the controller's cached physical bindings. Substitute
        // only during the synchronous Draw call, then restore before any input/update.
        var displayOverrides = input.Bindings
            .Select((binding, index) => (Binding: binding, Index: index,
                DisplayName: GetBindingDisplayName(binding)))
            .Where(entry => entry.DisplayName != MenuManager.GetBindingDisplayName(entry.Binding))
            .ToArray();
        try
        {
            foreach (var entry in displayOverrides)
                input.Bindings[entry.Index] = new InputBinding
                {
                    Device = entry.Binding.Device,
                    Input = entry.DisplayName,
                    Action = entry.Binding.Action
                };
            menu.Draw();
        }
        finally
        {
            foreach (var entry in displayOverrides)
                input.Bindings[entry.Index] = entry.Binding;
        }
    }
}
