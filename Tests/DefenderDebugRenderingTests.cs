using System.Numerics;
using Raylib_cs;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class DefenderDebugRenderingTests
{
    [Theory]
    [InlineData(640, 480)]
    [InlineData(1280, 720)]
    public void DiagnosticGeometryAndLabelsRenderForEveryProfile(int width, int height)
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(width, height, "Defender AI diagnostics");
        var target = Raylib.LoadRenderTexture(width, height);
        try
        {
            var profiles = DefenderProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
            string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/defender-ai"));
            Directory.CreateDirectory(directory);
            foreach (var profile in profiles.Profiles)
            {
                var config = new TackleAlleyConfig { DrawTackleAimingDebug = true };
                using var d = new Opponent(Vector3.Zero, config, behaviorProfile: profile);
                d.Update(new(0, 0, -30), .01f, false, Vector2.Zero, false);
                d.Update(d.Position + new Vector3(0, 0, -4), 0, true, Vector2.Zero, false);
                d.Update(d.Position + new Vector3(0, 0, -1.8f), 0);
                d.Update(d.Position + new Vector3(0, 0, -1.8f), .25f);
                Assert.True(d.DirectionLocked);
                foreach (bool enabled in new[] { false, true })
                {
                    config.DrawTackleAimingDebug = enabled;
                    Raylib.BeginDrawing();
                    Raylib.BeginTextureMode(target);
                    Color background = new(12, 22, 32, 255);
                    Raylib.ClearBackground(background);
                    Raylib.BeginMode3D(new Camera3D(new(9, 9, 10), new(0, 0, -2), Vector3.UnitY, 45, CameraProjection.Perspective));
                    d.DrawTackleAiming();
                    Raylib.EndMode3D();
                    if (enabled) Opponent.DrawTackleAimingLegend();
                    d.DrawTackleAimingLabel(122);
                    Raylib.EndTextureMode();
                    Raylib.EndDrawing();
                    Image image = Raylib.LoadImageFromTexture(target.Texture);
                    try
                    {
                        int drawn = 0, red = 0, gold = 0, cyan = 0, magenta = 0;
                        for (int y = 0; y < height; y++)
                            for (int x = 0; x < width; x++)
                            {
                                Color pixel = Raylib.GetImageColor(image, x, y);
                                if (!pixel.Equals(background)) drawn++;
                                if (pixel.Equals(Color.Red)) red++;
                                if (pixel.Equals(Color.Gold)) gold++;
                                if (pixel.Equals(new Color(0, 235, 220, 255))) cyan++;
                                if (pixel.Equals(Color.Magenta)) magenta++;
                            }
                        if (!enabled) Assert.Equal(0, drawn);
                        else
                        {
                            Assert.True(drawn > 1000);
                            Assert.True(red > 0 && gold > 0 && cyan > 0 && magenta > 0,
                                "Draw locked direction, breakdown range, pursuit and selected contact.");
                            Raylib.ImageFlipVertical(ref image);
                            Assert.True(Raylib.ExportImage(image, Path.Combine(directory, $"{profile.Id}-{width}x{height}.png")));
                        }
                    }
                    finally { Raylib.UnloadImage(image); }
                }
            }
        }
        finally { Raylib.UnloadRenderTexture(target); Raylib.CloseWindow(); }
    }
}
