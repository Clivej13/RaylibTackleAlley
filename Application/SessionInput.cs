using System.Reflection;
using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;

namespace RaylibTackleAlley.Application;

// Keep the complete saved bindings separate from the selected device's live config.
internal sealed class SessionInput
{
    private static readonly PropertyInfo ActiveFamily = typeof(InputController).GetProperty(nameof(InputController.ActiveDeviceFamily))!;
    public InputDeviceFamily Family { get; }
    public InputConfig Config { get; }
    public InputController Controller { get; }

    public SessionInput(InputConfig saved, InputDeviceFamily family)
    {
        Family = family;
        Config = new() { Bindings = saved.Bindings.Where(Accepts).Select(b =>
            new InputBinding { Device = b.Device, Input = b.Input, Action = b.Action }).ToList() };
        Controller = new(Config);
        PinFamily();
    }

    private bool Accepts(InputBinding binding) =>
        binding.Device.StartsWith("Gamepad", StringComparison.OrdinalIgnoreCase) == (Family == InputDeviceFamily.Gamepad);

    // Input 0.1.5 detects activity globally even for unbound devices. Pin its UI family.
    public void PinFamily() => ActiveFamily.SetValue(Controller, Family);

    public void CopyBindingsTo(InputConfig saved)
    {
        saved.Bindings.RemoveAll(Accepts);
        saved.Bindings.AddRange(Config.Bindings.Select(b =>
            new InputBinding { Device = b.Device, Input = b.Input, Action = b.Action }));
    }
}

// Menus 0.1.3 reads mouse hover/click/wheel directly and exposes no disable switch.
// Route controller-only navigation through its existing selection/activation methods.
internal static class ControllerMenu
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static MethodInfo Method(string name) => typeof(MenuManager).GetMethod(name, Private)
        ?? throw new NotSupportedException("Menus 0.1.3 navigation API changed.");
    private static readonly MethodInfo Move = Method("MoveSelection"), Back = Method("GoBack"),
        Adjust = Method("AdjustSelected"), Activate = Method("ActivateItem");
    private static readonly PropertyInfo Current = typeof(MenuManager).GetProperty("CurrentMenu", Private)!;

    public static MenuAction? Update(MenuManager menu, InputController input)
    {
        if (input.IsRebinding)
        {
            if (input.WasPressed("MenuBack")) input.CancelRebind();
            return null;
        }
        // Consume the capture frame so its A/B/D-pad press cannot also activate a row.
        if (input.CompletedRebind is { } binding)
        {
            bool back = (bool)Method("IsMenuBackBinding").Invoke(menu, [binding])!;
            if (!back) input.ApplyRebind(binding);
            input.CancelRebind();
            return null;
        }
        if (input.WasPressed("MenuBack")) { Back.Invoke(menu, null); return null; }
        if (input.WasPressed("MenuUp")) Move.Invoke(menu, [-1]);
        else if (input.WasPressed("MenuDown")) Move.Invoke(menu, [1]);
        if (input.WasPressed("MenuLeft")) return (MenuAction?)Adjust.Invoke(menu, [-1]);
        if (input.WasPressed("MenuRight")) return (MenuAction?)Adjust.Invoke(menu, [1]);
        if (!input.WasPressed("MenuConfirm")) return null;
        var definition = (MenuDefinition)Current.GetValue(menu)!;
        var item = definition.Items[menu.SelectedIndex];
        if (item.Type == "KeyBind")
        {
            input.BeginRebind(item.Action!, InputDeviceFamily.Gamepad);
            return null;
        }
        return (MenuAction?)Activate.Invoke(menu, [item, menu.SelectedIndex]);
    }
}
