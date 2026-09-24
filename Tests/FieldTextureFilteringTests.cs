using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibTackleAlley.Game;
using Xunit;
using Xunit.Abstractions;

public sealed class FieldTextureFilteringTests(ITestOutputHelper output)
{
    // GPU-state inspection is Windows-only; production filtering uses portable Raylib APIs.
    [DllImport("opengl32.dll")] private static extern void glGetTexParameteriv(uint target, uint name, out int value);
    [DllImport("opengl32.dll")] private static extern void glGetTexParameterfv(uint target, uint name, out float value);
    [DllImport("opengl32.dll")] private static extern void glGetFloatv(uint name, out float value);
    [DllImport("opengl32.dll")] private static extern void glFinish();

    private static (int Min, int Mag, float Anisotropy) State(Texture2D texture)
    {
        Rlgl.EnableTexture(texture.Id);
        glGetTexParameteriv(0x0DE1, 0x2801, out int min);
        glGetTexParameteriv(0x0DE1, 0x2800, out int mag);
        glGetTexParameterfv(0x0DE1, 0x84FE, out float anisotropy);
        Rlgl.DisableTexture();
        return (min, mag, anisotropy);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    [Fact]
    public unsafe void FieldUsesMipmapsAndAnisotropyWithoutChangingOtherTextures()
    {
        if (!OperatingSystem.IsWindows()) return;
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(1280, 720, "Field filtering comparison");
        Raylib.SetTargetFPS(0);
        var definitions = AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json"));
        var assets = new AssetManager(definitions);
        try
        {
            assets.RequireAssets(definitions.Assets.Select(a => a.Key).ToArray());
            while (!assets.ProcessNext()) { }
            Model model = assets.GetModel("FootballField");
            int material = model.MeshMaterial[0];
            Texture2D before = model.Materials[material].Maps[(int)MaterialMapIndex.Albedo].Texture;
            var beforeState = State(before);
            output.WriteLine($"Loaded: {before.Width}x{before.Height}, mipmaps={before.Mipmaps}, min=0x{beforeState.Min:X}, mag=0x{beforeState.Mag:X}, anisotropy={beforeState.Anisotropy}");
            var untouched = new Dictionary<uint, (Texture2D Texture, (int Min, int Mag, float Anisotropy) State)>();
            foreach (string key in new[] { "Stadium", "Football", "FootballPlayer" })
            {
                Model other = assets.GetModel(key);
                for (int m = 0; m < other.MaterialCount; m++)
                {
                    Texture2D texture = other.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture;
                    if (texture.Id != 0) untouched.TryAdd(texture.Id, (texture, State(texture)));
                }
            }
            Texture2D normal = model.Materials[material].Maps[(int)MaterialMapIndex.Normal].Texture;
            untouched.TryAdd(normal.Id, (normal, State(normal)));

            var config = JsonSerializer.Deserialize<TackleAlleyConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json")))!;
            var input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
            var game = new TackleAlleyGame(config, input, assets);
            game.InitializeVisuals(assets);
            var camera = Field<ThirdPersonCamera>(game, "_camera");
            Camera3D gameplayCamera = camera.Camera;
            void Draw()
            {
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(12, 22, 32, 255));
                game.Draw();
                Raylib.EndDrawing();
            }
            Draw(); // Exercises the production field initialization, including in-place metadata.
            Texture2D filtered = model.Materials[material].Maps[(int)MaterialMapIndex.Albedo].Texture;
            glGetFloatv(0x84FF, out float maximum);
            var state = State(filtered);
            Assert.Equal(before.Id, filtered.Id);
            Assert.Equal(before.Width, filtered.Width);
            Assert.Equal(before.Height, filtered.Height);
            Assert.Equal(1 + (int)Math.Floor(Math.Log2(Math.Max(filtered.Width, filtered.Height))), filtered.Mipmaps);
            Assert.Equal(0x2703, state.Min); // GL_LINEAR_MIPMAP_LINEAR
            Assert.Equal(0x2601, state.Mag); // GL_LINEAR
            Assert.Equal(Math.Min(16, maximum), state.Anisotropy);
            foreach (var other in untouched.Values) Assert.Equal(other.State, State(other.Texture));
            Draw();
            Assert.Equal(filtered.Mipmaps, model.Materials[material].Maps[(int)MaterialMapIndex.Albedo].Texture.Mipmaps);
            Assert.Equal(state, State(filtered));
            output.WriteLine($"Filtered: mipmaps={filtered.Mipmaps}, min=0x{state.Min:X}, mag=0x{state.Mag:X}, anisotropy={state.Anisotropy}, device maximum={maximum}");
            output.WriteLine($"Unchanged texture states: {untouched.Count} (stadium, football, player, default texture and field normal map).");

            // Comparison changes only GPU sampling state, never assets or production camera settings.
            void Mode(int mode)
            {
                Rlgl.TextureParameters(filtered.Id, Rlgl.TEXTURE_FILTER_ANISOTROPIC, 1);
                if (mode == 0)
                {
                    Texture2D baseOnly = filtered;
                    baseOnly.Mipmaps = 1;
                    Raylib.SetTextureFilter(baseOnly, TextureFilter.Point);
                }
                else
                {
                    Raylib.SetTextureFilter(filtered, TextureFilter.Trilinear);
                    if (mode == 2) Raylib.SetTextureFilter(filtered, TextureFilter.Anisotropic16X);
                }
            }
            void Camera(Camera3D value) => typeof(ThirdPersonCamera).GetProperty(nameof(ThirdPersonCamera.Camera))!.SetValue(camera, value);
            string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/field-filtering"));
            Directory.CreateDirectory(directory);
            var views = new (string Name, Camera3D Camera)[]
            {
                ("normal-near", gameplayCamera),
                ("shallow-near", new() { Position = new(0, 1.2f, 6.75f), Target = new(0, .3f, -15), Up = Vector3.UnitY, FovY = config.CameraFovY, Projection = CameraProjection.Perspective }),
                ("normal-far", new() { Position = new(0, 5.5f, -77), Target = new(0, 1.1f, -64.75f), Up = Vector3.UnitY, FovY = config.CameraFovY, Projection = CameraProjection.Perspective }),
                ("shallow-far", new() { Position = new(0, 1.2f, -77), Target = new(0, .3f, -55.25f), Up = Vector3.UnitY, FovY = config.CameraFovY, Projection = CameraProjection.Perspective })
            };
            foreach (var view in views)
            {
                Camera(view.Camera);
                for (int mode = 0; mode < 3; mode++)
                {
                    Mode(mode);
                    // Read before swapping the window buffers, matching the existing native probes.
                    Raylib.BeginDrawing();
                    Raylib.ClearBackground(new Color(12, 22, 32, 255));
                    game.Draw();
                    Image image = Raylib.LoadImageFromScreen();
                    Raylib.EndDrawing();
                    try { Assert.True(Raylib.ExportImage(image, Path.Combine(directory, $"{view.Name}-{mode}.png"))); }
                    finally { Raylib.UnloadImage(image); }
                }
            }
            // Small camera translations expose subpixel popping in the distant markings.
            // Compare the same static scene and pixel regions; exclude the central player.
            foreach (var view in views)
            {
                int top = view.Name.StartsWith("shallow") ? 350 : 180;
                for (int mode = 0; mode <= 2; mode += 2)
                {
                    Mode(mode);
                    byte[]? previous = null;
                    double total = 0;
                    int samples = 0;
                    for (int frame = 0; frame < 24; frame++)
                    {
                        Camera3D moving = view.Camera;
                        Vector3 offset = new(0, 0, frame * .01f);
                        moving.Position += offset;
                        moving.Target += offset;
                        Camera(moving);
                        Raylib.BeginDrawing();
                        Raylib.ClearBackground(new Color(12, 22, 32, 255));
                        game.Draw();
                        Image image = Raylib.LoadImageFromScreen();
                        Raylib.EndDrawing();
                        Color* pixels = Raylib.LoadImageColors(image);
                        var current = new byte[160 * 40];
                        int index = 0;
                        for (int y = top; y < top + 40; y++)
                        for (int x = 500; x < 780; x++)
                        {
                            if (x >= 580 && x < 700) continue;
                            Color pixel = pixels[y * 1280 + x];
                            current[index++] = (byte)((pixel.R + pixel.G + pixel.B) / 3);
                        }
                        Raylib.UnloadImageColors(pixels);
                        Raylib.UnloadImage(image);
                        if (previous != null)
                        {
                            for (int p = 0; p < current.Length; p++)
                                total += Math.Abs(current[p] - previous[p]);
                            samples += current.Length;
                        }
                        previous = current;
                    }
                    output.WriteLine($"{view.Name}, mode {mode}: distant-field mean frame-to-frame brightness change {total / samples:F3}/255 (small camera translation).");
                }
            }
            // Force GPU completion; report medians without a noisy timing assertion.
            Camera(gameplayCamera);
            foreach (int mode in new[] { 0, 1, 2, 0, 2 })
            {
                Mode(mode);
                for (int i = 0; i < 20; i++) Draw();
                var timings = new double[90];
                for (int i = 0; i < timings.Length; i++)
                {
                    glFinish();
                    long start = Stopwatch.GetTimestamp();
                    Draw();
                    glFinish();
                    timings[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }
                Array.Sort(timings);
                output.WriteLine($"Mode {mode}: median full-scene draw + GPU completion {timings[timings.Length / 2]:F3} ms, p95 {timings[(int)(timings.Length * .95)]:F3} ms.");
            }
            Mode(1);
            Assert.Equal((0x2703, 0x2601, 1f), State(filtered)); // Explicit trilinear fallback state.
            Mode(2);
            Camera(gameplayCamera);
            for (int frame = 0; frame < 120; frame++)
            {
                input.Update();
                game.Update(1f / 60);
                Draw();
            }
            Assert.Equal(state, State(filtered));
            output.WriteLine($"Ran 120 gameplay update/draw frames. Comparison captures: {directory}");
        }
        finally { assets.UnloadAll(); Raylib.CloseWindow(); }
    }
}
