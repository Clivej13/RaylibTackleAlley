using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;
using Xunit;

public sealed class AnimatedHeadTests
{
    [Fact]
    public unsafe void FootballClipsExposeCurrentHeadWithoutRestartingPlayback()
    {
        Assert.Equal(new Version(0, 1, 1, 0), typeof(AnimationPlayer).Assembly.GetName().Version);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Animated Head API validation");
        var assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        try
        {
            foreach (string key in new[] { "FootballPlayerAnimations", "FootballPlayerRunAnimations", "FootballPlayerSprintAnimations" })
            {
                assets.RequireAssets(key);
                while (!assets.ProcessNext()) { }
                string name = key == "FootballPlayerAnimations" ? "Jog" :
                    key == "FootballPlayerRunAnimations" ? "Run" : "Sprint";
                ModelAnimation clip = assets.GetModelAnimations(key).ToArray().Single(c => new string(c.Name) == name);
                var instance = assets.CreateModelInstance("FootballPlayer");
                var player = new AnimationPlayer(instance, clip, loop: true);
                player.SeekTime(0);
                Assert.True(player.TryGetBoneTransform("Head", out Matrix4x4 initial));
                player.Update(0.15f);
                float time = player.CurrentTime;
                float frame = player.CurrentFrame;
                Assert.True(player.TryGetBoneTransform("Head", out Matrix4x4 current));
                Assert.NotEqual(initial, current);
                Assert.True(Matrix4x4.Invert(current, out _));
                Assert.Equal(time, player.CurrentTime);
                Assert.Equal(frame, player.CurrentFrame);
                Assert.True(player.TryGetBoneTransform("Head", out Matrix4x4 repeated));
                Assert.Equal(current, repeated);
                assets.ReleaseModelInstance(instance);
            }
        }
        finally
        {
            assets.UnloadAll();
            Raylib.CloseWindow();
        }
    }
}
