using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class FootballRenderTests
{
    private static T Field<T>(object target, string name) =>
        (T)typeof(BallCarrier).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    [Fact]
    public unsafe void DrawMatchesNativeTrsReferenceAtAnimatedHandWithoutDistortion()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Football render boundary regression");
        var assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        RenderTexture2D target = Raylib.LoadRenderTexture(640, 480);
        try
        {
            assets.RequireAssets("Football", "FootballPlayer", "FootballPlayerCarryJogAnimations",
                "FootballPlayerCarryRunAnimations", "FootballPlayerCarrySprintAnimations",
                "FootballPlayerCutLeftAnimations", "FootballPlayerCutRightAnimations");
            while (!assets.ProcessNext()) { }
            var player = new BallCarrier(new TackleAlleyConfig());
            player.InitializeVisual(assets);
            Model football = assets.GetModel("Football");
            BoundingBox bounds = Raylib.GetModelBoundingBox(football);
            Assert.InRange(bounds.Max.Z - bounds.Min.Z, .27f, .29f);

            foreach (int tier in new[] { 1, 2, 3 })
            foreach (float yaw in new[] { -30f, 0f, 110f })
            {
                typeof(BallCarrier).GetProperty("SpeedTier")!.SetValue(player, tier);
                typeof(BallCarrier).GetMethod("SelectAnimation", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(player, null);
                typeof(BallCarrier).GetField("_currentRunYaw", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(player, yaw);
                player.RunIntoEndZone(.13f, -20f);
                Matrix4x4 world = Field<Matrix4x4>(player, "_footballWorldTransform");
                Assert.True(Matrix4x4.Decompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 position));
                Assert.InRange((bounds.Max.Z - bounds.Min.Z) * scale.Z, .26f, .30f);
                rotation = Quaternion.Normalize(rotation);
                float angle = 2f * MathF.Acos(Math.Clamp(rotation.W, -1f, 1f));
                Vector3 axis = Vector3.Normalize(new Vector3(rotation.X, rotation.Y, rotation.Z));
                var camera = new Camera3D
                {
                    Position = position + new Vector3(-.8f, .35f, -.9f),
                    Target = position,
                    Up = Vector3.UnitY,
                    FovY = 45f,
                    Projection = CameraProjection.Perspective
                };

                void DrawPlayer()
                {
                    var model = Field<ModelInstance>(player, "_model").Model;
                    float visualYaw = (float)typeof(BallCarrier).GetProperty("VisualYawDegrees",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
                    Raylib.DrawModelEx(model,
                        player.Position + new Vector3(0, Field<float>(player, "_groundOffset"), 0),
                        Vector3.UnitY, visualYaw, new Vector3(Field<float>(player, "_visualScale")), Color.White);
                }

                byte[] Capture(Action draw)
                {
                    Raylib.BeginTextureMode(target);
                    Raylib.ClearBackground(Color.Magenta);
                    Raylib.BeginMode3D(camera);
                    draw();
                    Raylib.EndMode3D();
                    Raylib.EndTextureMode();
                    Image image = Raylib.LoadImageFromTexture(target.Texture);
                    Color* pixels = Raylib.LoadImageColors(image);
                    try
                    {
                        var bytes = new byte[640 * 480 * 4];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)pixels, bytes, 0, bytes.Length);
                        return bytes;
                    }
                    finally
                    {
                        Raylib.UnloadImageColors(pixels);
                        Raylib.UnloadImage(image);
                    }
                }

                byte[] actual = Capture(player.Draw);
                // Independent native reference: no managed matrix crosses the boundary.
                byte[] expected = Capture(() =>
                {
                    DrawPlayer();
                    Raylib.DrawModelEx(football, position, axis, angle * 180f / MathF.PI, scale, Color.White);
                });
                byte[] withoutBall = Capture(DrawPlayer);
                int visibleBallPixels = 0;
                int mismatchedPixels = 0;
                for (int p = 0; p < actual.Length; p += 4)
                {
                    bool visible = false;
                    bool mismatch = false;
                    for (int c = 0; c < 3; c++)
                    {
                        visible |= Math.Abs(expected[p + c] - withoutBall[p + c]) > 8;
                        mismatch |= Math.Abs(expected[p + c] - actual[p + c]) > 8;
                    }
                    if (visible) visibleBallPixels++;
                    if (mismatch) mismatchedPixels++;
                }
                Assert.True(visibleBallPixels > 100, $"Reference football must be visible: tier {tier}, yaw {yaw}.");
                Assert.True(mismatchedPixels < visibleBallPixels * .02f,
                    $"Render boundary mismatch: {mismatchedPixels}/{visibleBallPixels} pixels, tier {tier}, yaw {yaw}.");
                Assert.Equal(Matrix4x4.Identity, assets.GetModel("Football").Transform);
            }
        }
        finally
        {
            Raylib.UnloadRenderTexture(target);
            assets.UnloadAll();
            Raylib.CloseWindow();
        }
    }
}
