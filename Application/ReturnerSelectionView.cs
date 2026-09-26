using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Menus;
using RaylibTackleAlley.Game;

namespace RaylibTackleAlley.Application;

/// <summary>Presentation adapter for the pinned Menus 0.1.3 ListWithDetail layout.
/// The package owns selection, scrolling and activation. Its layout has no public
/// styling hook; these two read-only layout queries keep drawing and hit testing aligned.</summary>
internal sealed class ReturnerSelectionView(ReturnerCatalog catalog, MenuDefinition definition, ReturnerPreview preview)
{
    private static readonly PropertyInfo NativeLayout = typeof(MenuManager).GetProperty(
        "CurrentLayoutBounds", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("Menus ListWithDetail layout API changed.");
    private static readonly MethodInfo NativeRow = typeof(MenuManager).GetMethod(
        "GetItemBounds", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("Menus ListWithDetail row API changed.");
    private static readonly Color Background = new(12, 22, 32, 255);
    private static readonly Color Card = new(17, 29, 42, 255);
    private static readonly Color Muted = new(155, 174, 192, 255);

    private static Rectangle NativeList(MenuManager menu) =>
        ((ValueTuple<Rectangle, Rectangle?>)NativeLayout.GetValue(menu)!).Item1;

    internal static (Rectangle List, Rectangle Detail) Layout()
    {
        float width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
        float listWidth = Math.Clamp(width * .27f, 174, 300);
        return (new(16, 100, listWidth, height - 148),
            new(32 + listWidth, 100, width - listWidth - 48, height - 148));
    }

    public MenuAction? Update(MenuManager menu)
    {
        if (menu.CurrentMenuName != "Returners") return menu.Update();
        var native = NativeList(menu);
        var list = Layout().List;
        Vector2 mouse = Raylib.GetMousePosition();
        // Translate only the menu's mouse query. InputController already sampled raw
        // input, and gameplay sees the default transform again before this returns.
        Vector2 mapped = Raylib.CheckCollisionPointRec(mouse, list)
            ? new(native.X + (mouse.X - list.X) * native.Width / list.Width,
                native.Y + (mouse.Y - list.Y) * native.Height / list.Height)
            : new(-1000, -1000);
        Raylib.SetMouseOffset((int)MathF.Round(mapped.X - mouse.X), (int)MathF.Round(mapped.Y - mouse.Y));
        try { return menu.Update(); }
        finally { Raylib.SetMouseOffset(0, 0); }
    }

    public void Draw(MenuManager menu)
    {
        if (menu.CurrentMenuName != "Returners") { preview.Hide(); return; }
        var (list, detail) = Layout();
        Raylib.ClearBackground(Background);
        Text("SELECT RETURNER", 16, 30, 30, Color.White, Raylib.GetScreenWidth() - 32);
        Text("CHOOSE YOUR PLAYMAKER", 18, 68, 12, Muted, list.Width + detail.Width);
        DrawList(menu, list);
        Raylib.DrawRectangleRec(detail, Card);
        Raylib.DrawRectangleLinesEx(detail, 1, new Color(48, 65, 82, 255));
        if (menu.SelectedItem is { Function: "SelectReturner" } item)
            DrawPlayer(catalog.Resolve(item.Value.GetString()!), detail);
        else
        {
            preview.Hide();
            Text("RETURN TO MAIN MENU", detail.X + 16, detail.Y + 22, 20, Color.White, detail.Width - 32);
            Text("Choose a returner to view their profile.", detail.X + 16, detail.Y + 58, 14, Muted, detail.Width - 32);
        }
        Text("A / ENTER  Select      B / ESC  Back", 16, Raylib.GetScreenHeight() - 29,
            14, Muted, Raylib.GetScreenWidth() - 32);
    }

    private void DrawList(MenuManager menu, Rectangle list)
    {
        var native = NativeList(menu);
        float sy = list.Height / native.Height;
        Raylib.BeginScissorMode((int)list.X, (int)list.Y, (int)list.Width, (int)list.Height);
        for (int i = 0; i < definition.Items.Count; i++)
        {
            var row = (Rectangle)NativeRow.Invoke(menu, [definition, i])!;
            row = new(list.X, list.Y + (row.Y - native.Y) * sy, list.Width, row.Height * sy);
            if (row.Y + row.Height <= list.Y || row.Y >= list.Y + list.Height) continue;
            bool selected = i == menu.SelectedIndex;
            var entry = definition.Items[i].Function == "SelectReturner"
                ? catalog.Resolve(definition.Items[i].Value.GetString()!) : null;
            Color accent = entry is null ? Muted : preview.Accent(entry);
            Raylib.DrawRectangleRec(row, selected ? new Color(32, 48, 64, 255) : Card);
            Raylib.DrawRectangleLinesEx(row, 1, selected ? accent : new Color(38, 53, 69, 255));
            Raylib.DrawRectangle((int)row.X, (int)row.Y, selected ? 4 : 2, (int)row.Height, accent);
            if (entry is null)
                Text("BACK", row.X + 14, row.Y + row.Height / 2 - 8, 16, selected ? Color.White : Muted, row.Width - 28);
            else
            {
                Text($"#{entry.Profile.JerseyNumber:00}", row.X + 12, row.Y + 10, 23, accent, 50);
                Text(entry.Profile.Name, row.X + 61, row.Y + 12, 18, Color.White, row.Width - 70);
                Text(selected ? "HIGHLIGHTED" : "RETURNER", row.X + 61, row.Y + 35, 10, Muted, row.Width - 70);
            }
        }
        Raylib.EndScissorMode();
        // Native scrolling stays available on small windows; indicate roster position.
        Text($"{Math.Min(menu.SelectedIndex + 1, catalog.Returners.Count)} / {catalog.Returners.Count}   UP / DOWN",
            list.X, list.Y + list.Height + 5, 10, Muted, list.Width);
    }

    private void DrawPlayer(ReturnerDefinition entry, Rectangle card)
    {
        var p = entry.Profile;
        Color accent = preview.Accent(entry);
        float x = card.X + 14, y = card.Y + 14, inner = card.Width - 28;
        Raylib.DrawRectangle((int)card.X, (int)card.Y, (int)card.Width, 3, accent);
        Text($"#{p.JerseyNumber:00}", x, y, 32, accent, 72);
        Text(p.Name.ToUpperInvariant(), x + 76, y + 3, 28, Color.White, inner - 76);
        // These are the exact profile dimensions also used by PlayerVisualProfile and gameplay.
        Text(FormattableString.Invariant($"HEIGHT  {p.Height:0.00} m    WEIGHT  {p.Weight:0.#} kg"),
            x, y + 43, 14, Muted, inner);
        Raylib.DrawLine((int)x, (int)y + 70, (int)(x + inner), (int)y + 70, new Color(48, 65, 82, 255));
        float bodyY = y + 84, bodyHeight = card.Y + card.Height - 14 - bodyY;
        float statsWidth = Math.Clamp(inner * .36f, 132, 210);
        var modelBounds = new Rectangle(x + statsWidth + 12, bodyY - 6,
            inner - statsWidth - 12, bodyHeight + 6);
        preview.Draw(entry, modelBounds);
        float rowHeight = Math.Min(52, bodyHeight / 5);
        int font = card.Width < 500 ? 14 : 18;
        int index = 0;
        foreach (var (label, rating) in new[] {
            ("Speed", p.Speed), ("Acceleration", p.Acceleration), ("Agility", p.Agility),
            ("Juke", p.Juke), ("Strength", p.Strength) })
        {
            float ry = bodyY + index++ * rowHeight;
            Text(label, x, ry, font, Muted, statsWidth - 29);
            Text(rating.ToString(), x + statsWidth - 26, ry, font, Color.White, 26);
            var bar = new Rectangle(x, ry + font + 6, statsWidth, 5);
            Raylib.DrawRectangleRec(bar, new Color(42, 57, 73, 255));
            bar.Width *= rating / 100f;
            Raylib.DrawRectangleRec(bar, accent);
        }
    }

    private static void Text(string text, float x, float y, int size, Color color, float width)
    {
        while (size > 9 && Raylib.MeasureText(text, size) > width) size--;
        Raylib.DrawText(text, (int)x, (int)y, size, color);
    }
}
