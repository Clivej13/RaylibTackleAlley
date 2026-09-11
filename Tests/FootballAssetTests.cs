using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class FootballAssetTests
{
    private static JsonDocument Glb(string name)
    {
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets/Models", name)));
        Assert.Equal(0x46546C67u, reader.ReadUInt32());
        Assert.Equal(2u, reader.ReadUInt32());
        reader.ReadUInt32();
        int length = reader.ReadInt32();
        Assert.Equal(0x4E4F534Au, reader.ReadUInt32());
        return JsonDocument.Parse(reader.ReadBytes(length));
    }

    [Fact]
    public void FootballIsStandaloneAndPlayerExportsExcludeIt()
    {
        using var football = Glb("football.glb");
        var root = football.RootElement;
        Assert.False(root.TryGetProperty("skins", out _));
        Assert.False(root.TryGetProperty("animations", out _));
        Assert.Contains(root.GetProperty("nodes").EnumerateArray(), n =>
            n.TryGetProperty("name", out var name) && name.GetString() == "Football");
        Assert.Equal(3, root.GetProperty("materials").GetArrayLength());
        foreach (string file in new[] { "football_player.glb", "football_player_carry_jog.glb",
                     "football_player_carry_run.glb", "football_player_carry_sprint.glb" })
        {
            using var player = Glb(file);
            Assert.DoesNotContain(player.RootElement.GetProperty("nodes").EnumerateArray(), n =>
                n.TryGetProperty("name", out var name) &&
                (name.GetString() == "Football" || name.GetString() == "FootballPreview"));
        }
    }

    [Fact]
    public unsafe void GripReproducesAuthoredInsideTuckAndCarryRootsRemainStationary()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Carry grip asset validation");
        var assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        try
        {
            Matrix4x4 grip = (Matrix4x4)typeof(BallCarrier)
                .GetField("FootballGripLocal", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            foreach (string pace in new[] { "Jog", "Run", "Sprint" })
            {
                string key = "FootballPlayerCarry" + pace + "Animations";
                assets.RequireAssets("FootballPlayer", key);
                while (!assets.ProcessNext()) { }
                var model = assets.CreateModelInstance("FootballPlayer");
                var clip = assets.GetModelAnimations(key).ToArray().Single(c => new string(c.Name) == "Carry" + pace);
                Assert.True(Raylib.IsModelAnimationValid(model.Model, clip));
                var animation = new AnimationPlayer(model, clip, loop: true);
                animation.SeekTime(0);
                Assert.True(animation.TryGetBoneTransform("Hand.R", out var hand));
                Assert.True(animation.TryGetBoneTransform("Chest", out var chest));
                // Chest rest basis is identity at (0, 1.275, .01), from rig_player.py.
                var chestDeformation = Matrix4x4.CreateTranslation(0f, -1.275f, -.01f) * chest;
                Assert.True(Matrix4x4.Invert(chestDeformation, out var inverseChest));
                var ballInChest = grip * hand * inverseChest;
                // Independently documented corrected frame-1 centre, player_football_hand_notes.md.
                Assert.True(Vector3.Distance(new(-.245f, 1.316f, -.194f), ballInChest.Translation) < .002f,
                    $"Carry{pace} authored centre mismatch: {ballInChest.Translation}");
                var axis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, ballInChest));
                float elevation = MathF.Asin(axis.Y) * 180f / MathF.PI;
                Assert.InRange(elevation, 35f, 39f);
                Assert.True(animation.TryGetBoneTransform("Root", out var initialRoot));
                for (int frame = 1; frame < animation.FrameCount; frame++)
                {
                    animation.SeekTime(frame / animation.FramesPerSecond);
                    Assert.True(animation.TryGetBoneTransform("Root", out var rootPose));
                    Assert.Equal(initialRoot, rootPose);
                }
                assets.ReleaseModelInstance(model);
            }
        }
        finally
        {
            assets.UnloadAll();
            Raylib.CloseWindow();
        }
    }
}
