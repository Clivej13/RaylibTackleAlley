using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;
using Xunit;

public sealed class SkinnedHelmetTests
{
    [Fact]
    public unsafe void RegeneratedHelmetIsHeadSkinnedAndFollowsAllPacesIndependently()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets/Models/football_player.glb");
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        Assert.Equal(0x46546C67u, reader.ReadUInt32());
        Assert.Equal(2u, reader.ReadUInt32());
        reader.ReadUInt32();
        int jsonLength = reader.ReadInt32();
        Assert.Equal(0x4E4F534Au, reader.ReadUInt32());
        using var document = JsonDocument.Parse(reader.ReadBytes(jsonLength));
        int binaryLength = reader.ReadInt32();
        Assert.Equal(0x004E4942u, reader.ReadUInt32());
        byte[] binary = reader.ReadBytes(binaryLength);
        var root = document.RootElement;
        var nodes = root.GetProperty("nodes");
        int equipmentVertices = 0;
        foreach (string name in new[] { "Helmet", "Facemask", "Visor", "ChinStrap" })
        {
            var node = nodes.EnumerateArray().Single(n =>
                n.TryGetProperty("name", out var value) && value.GetString() == name);
            var skin = root.GetProperty("skins")[node.GetProperty("skin").GetInt32()];
            var joints = skin.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
            int head = Array.FindIndex(joints, j => nodes[j].GetProperty("name").GetString() == "Head");
            Assert.True(head >= 0);
            var mesh = root.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()];
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                var attributes = primitive.GetProperty("attributes");
                var jointAccessor = root.GetProperty("accessors")[attributes.GetProperty("JOINTS_0").GetInt32()];
                var weightAccessor = root.GetProperty("accessors")[attributes.GetProperty("WEIGHTS_0").GetInt32()];
                int count = weightAccessor.GetProperty("count").GetInt32();
                Assert.True(count > 0);
                Assert.Equal(count, jointAccessor.GetProperty("count").GetInt32());
                equipmentVertices += count;
                for (int v = 0; v < count; v++)
                {
                    float total = 0;
                    for (int slot = 0; slot < 4; slot++)
                    {
                        float weight = Component(weightAccessor, v, slot);
                        if (weight > 0)
                            Assert.Equal(head, (int)Component(jointAccessor, v, slot));
                        total += weight;
                    }
                    Assert.Equal(1f, total, 5);
                }
            }
        }

        float Component(JsonElement accessor, int vertex, int slot)
        {
            int type = accessor.GetProperty("componentType").GetInt32();
            int size = type == 5121 ? 1 : type == 5123 ? 2 : 4;
            var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            int offset = (view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0) +
                (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0) +
                vertex * (view.TryGetProperty("byteStride", out var stride) ? stride.GetInt32() : size * 4) + slot * size;
            float value = type switch {
                5121 => binary[offset],
                5123 => BitConverter.ToUInt16(binary, offset),
                5126 => BitConverter.ToSingle(binary, offset),
                _ => throw new InvalidDataException("Unexpected skin component type.")
            };
            if (accessor.TryGetProperty("normalized", out var normalized) && normalized.GetBoolean())
                value /= type == 5121 ? 255f : 65535f;
            return value;
        }

        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Skinned helmet validation");
        var assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        try
        {
            assets.RequireAssets("FootballPlayerAnimations", "FootballPlayerRunAnimations", "FootballPlayerSprintAnimations",
                "FootballPlayerCarryJogAnimations", "FootballPlayerCarryRunAnimations", "FootballPlayerCarrySprintAnimations",
                "FootballPlayerCutLeftAnimations", "FootballPlayerCutRightAnimations");
            while (!assets.ProcessNext()) { }
            var players = new List<(ModelInstance Model, AnimationPlayer Player, Vector3[] Local)>();
            foreach (var (key, name) in new[] {
                ("FootballPlayerAnimations", "Jog"), ("FootballPlayerRunAnimations", "Run"),
                ("FootballPlayerSprintAnimations", "Sprint"), ("FootballPlayerRunAnimations", "Run"),
                ("FootballPlayerCarryJogAnimations", "CarryJog"), ("FootballPlayerCarryRunAnimations", "CarryRun"),
                ("FootballPlayerCarrySprintAnimations", "CarrySprint") })
            {
                var model = assets.CreateModelInstance("FootballPlayer");
                var clip = assets.GetModelAnimations(key).ToArray().Single(c => new string(c.Name) == name);
                var player = new AnimationPlayer(model, clip, loop: true);
                player.SeekTime(0);
                var local = HeadLocalVertices(model, player);
                Assert.True(local.Length >= equipmentVertices, "Native model must retain the head-skinned equipment vertices.");
                players.Add((model, player, local));
            }
            for (int step = 1; step <= 12; step++)
                for (int i = 0; i < players.Count; i++)
                {
                    var (model, player, initial) = players[i];
                    int other = (i + 1) % players.Count;
                    var untouched = HeadLocalVertices(players[other].Model, players[other].Player);
                    float otherTime = players[other].Player.CurrentTime;
                    player.Update(0.073f + i * 0.011f);
                    var current = HeadLocalVertices(model, player);
                    Assert.Equal(initial.Length, current.Length);
                    for (int v = 0; v < current.Length; v++)
                        Assert.True(Vector3.Distance(initial[v], current[v]) < 0.0002f,
                            "Rigid head-skinned vertex moved relative to the animated Head.");
                    Assert.Equal(otherTime, players[other].Player.CurrentTime);
                    Assert.Equal(untouched, HeadLocalVertices(players[other].Model, players[other].Player));
                    Raylib.BeginDrawing();
                    Raylib.DrawModelEx(model.Model, Vector3.Zero, Vector3.UnitY, 0, Vector3.One, Color.White);
                    Raylib.EndDrawing();
                }
        }
        finally
        {
            assets.UnloadAll();
            Raylib.CloseWindow();
        }
    }

    private static unsafe Vector3[] HeadLocalVertices(ModelInstance instance, AnimationPlayer player)
    {
        Assert.True(player.TryGetBoneTransform("Head", out var headTransform));
        Assert.True(Matrix4x4.Invert(headTransform, out var inverse));
        var model = instance.Model;
        int head = -1;
        for (int b = 0; b < model.Skeleton.BoneCount; b++)
            if (new string(model.Skeleton.Bones[b].Name) == "Head") head = b;
        Assert.True(head >= 0);
        var vertices = new List<Vector3>();
        for (int m = 0; m < model.MeshCount; m++)
        {
            var mesh = model.Meshes[m];
            if (mesh.BoneIndices == null || mesh.BoneWeights == null || mesh.AnimVertices == null) continue;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                float headWeight = 0;
                for (int s = 0; s < 4; s++)
                    if (mesh.BoneIndices[v * 4 + s] == head) headWeight += mesh.BoneWeights[v * 4 + s];
                if (headWeight < 0.99999f) continue;
                var point = new Vector3(mesh.AnimVertices[v * 3], mesh.AnimVertices[v * 3 + 1], mesh.AnimVertices[v * 3 + 2]);
                vertices.Add(Vector3.Transform(point, inverse));
            }
        }
        return vertices.ToArray();
    }
}
