using Raylib_cs;

// Raylib automation device state can survive a hidden-window test. Start native
// integration fixtures neutral so their controls don't depend on test ordering.
internal static class NativeInputTestState
{
    public static unsafe void Clear()
    {
        foreach (var key in Enum.GetValues<KeyboardKey>())
        {
            AutomationEvent e = new() { Type = 1 };
            e.Params[0] = (int)key;
            Raylib.PlayAutomationEvent(e);
        }
        for (int pad = 0; pad < 4; pad++)
        {
            foreach (var button in Enum.GetValues<GamepadButton>())
            {
                AutomationEvent up = new() { Type = 11 };
                up.Params[0] = pad; up.Params[1] = (int)button;
                Raylib.PlayAutomationEvent(up);
            }
            foreach (var axis in Enum.GetValues<GamepadAxis>())
            {
                AutomationEvent neutral = new() { Type = 13 };
                neutral.Params[0] = pad; neutral.Params[1] = (int)axis;
                Raylib.PlayAutomationEvent(neutral);
            }
            AutomationEvent disconnect = new() { Type = 10 };
            disconnect.Params[0] = pad;
            Raylib.PlayAutomationEvent(disconnect);
        }
    }
}
