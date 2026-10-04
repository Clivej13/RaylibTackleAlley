using Raylib_cs;

namespace RaylibTackleAlley.Application;

internal static class TitleScreen
{
    public static void Draw()
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        Raylib.ClearBackground(new Color(9, 20, 29, 255));
        // Field markings lead the eye toward the title.
        for (int i = 0; i < 8; i++)
        {
            int y = h / 2 + i * h / 14;
            Raylib.DrawLine(w / 8, y, w * 7 / 8, y, new Color(24, 48, 54, 255));
        }
        Center("TACKLE ALLEY", h / 4, Math.Min(72, w / 12), Color.White);
        Center("ONE RUN. BREAK THROUGH.", h / 4 + 90, 20, Color.SkyBlue);
        Center("PRESS START / MENU", h * 3 / 5, 26, Color.Gold);
        Center("Controller", h * 3 / 5 + 36, 17, Color.LightGray);
        Center("PRESS SPACE", h * 3 / 5 + 85, 26, Color.White);
        Center("Keyboard + mouse", h * 3 / 5 + 121, 17, Color.LightGray);
        Center("Your choice is used for this session.", h - 35, 16, Color.LightGray);
    }

    private static void Center(string text, int y, int size, Color color) =>
        Raylib.DrawText(text, (Raylib.GetScreenWidth() - Raylib.MeasureText(text, size)) / 2, y, size, color);
}
