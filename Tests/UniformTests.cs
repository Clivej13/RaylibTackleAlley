using System.Runtime.InteropServices;
using System.Text.Json;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class UniformTests
{
    private const string AuthoredUniform = "PlayerUniform";
    [DllImport("opengl32.dll", EntryPoint = "glIsTexture")]
    private static extern byte IsGpuTexture(uint texture);

    [Fact]
    public unsafe void UniformsSwapIndependentlyPreserveMeshesAndFollowAssetLifetime()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(64, 64, "Uniform validation");
        var config = AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json"));
        // Exercise both configured team textures through the real asset loader.
        var tuning = JsonSerializer.Deserialize<TackleAlleyConfig>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json")))!;
        string offenseUniform = tuning.OffenseUniform;
        string defenseUniform = tuning.DefenseUniform;
        var assets = new AssetManager(config);
        try
        {
            assets.RequireAssets("FootballPlayer", offenseUniform, defenseUniform);
            while (!assets.ProcessNext()) { }
            var first = assets.CreateModelInstance("FootballPlayer");
            var second = assets.CreateModelInstance("FootballPlayer");
            Model model = first.Model;
            Model other = second.Model;
            Model source = assets.GetModel("FootballPlayer");
            var original = Albedos(model);
            var otherOriginal = Albedos(other);
            var sourceOriginal = Albedos(source);
            var uv = Uvs(model);
            var vertices = Vertices(model);
            var bones = (nint)model.Skeleton.Bones;
            var meshes = (nint)model.Meshes;
            var maps = new MaterialMap[model.MaterialCount][];
            for (int i = 0; i < model.MaterialCount; i++)
            {
                Assert.True(model.Materials[i].Maps != other.Materials[i].Maps);
                maps[i] = new ReadOnlySpan<MaterialMap>(model.Materials[i].Maps, 12).ToArray();
            }

            // Verify against the named material and glTF primitive mapping, not a fixed index.
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, PlayerUniform.ModelPath));
            using var reader = new BinaryReader(stream);
            stream.Position = 12;
            int length = reader.ReadInt32();
            reader.ReadUInt32();
            using var doc = JsonDocument.Parse(reader.ReadBytes(length));
            var materials = doc.RootElement.GetProperty("materials").EnumerateArray().ToArray();
            int uniform = Array.FindIndex(materials, m =>
                m.TryGetProperty("name", out var name) && name.GetString() == "Uniform") + 1;
            Assert.True(uniform > 0);
            Assert.Contains(Enumerable.Range(0, model.MeshCount),
                mesh => model.MeshMaterial[mesh] == uniform);
            var a = assets.GetTexture(offenseUniform);
            var b = assets.GetTexture(defenseUniform);
            Assert.NotEqual(a.Id, b.Id);
            PlayerUniform.ApplyUniform(first, assets, offenseUniform);
            Assert.Equal(a.Id, Albedos(first.Model)[uniform]);
            Assert.Equal(otherOriginal, Albedos(other));
            Assert.Equal(sourceOriginal, Albedos(source));
            PlayerUniform.ApplyUniform(second, assets, offenseUniform);
            PlayerUniform.ApplyUniform(first, assets, defenseUniform);
            PlayerUniform.ApplyUniform(first, assets, defenseUniform);
            Assert.Equal(b.Id, Albedos(first.Model)[uniform]);
            Assert.Equal(a.Id, Albedos(second.Model)[uniform]);
            Assert.Throws<KeyNotFoundException>(() => PlayerUniform.ApplyUniform(first, assets, "Missing"));
            Assert.Equal(b.Id, Albedos(first.Model)[uniform]);
            for (int i = 0; i < model.MaterialCount; i++)
                for (int map = 0; map < 12; map++)
                {
                    var current = first.Model.Materials[i].Maps[map];
                    if (i == uniform && map == (int)MaterialMapIndex.Albedo)
                    {
                        Assert.Equal(maps[i][map].Color, current.Color);
                        Assert.Equal(maps[i][map].Value, current.Value);
                    }
                    else Assert.Equal(maps[i][map], current);
                }
            Assert.Equal(uv, Uvs(first.Model));
            Assert.Equal(vertices, Vertices(first.Model));
            Assert.Equal(bones, (nint)first.Model.Skeleton.Bones);
            Assert.Equal(meshes, (nint)first.Model.Meshes);

            assets.ReleaseModelInstance(first);
            // Releasing a model must not delete textures shared with another instance.
            if (OperatingSystem.IsWindows())
            {
                Assert.NotEqual((byte)0, IsGpuTexture(a.Id));
                Assert.NotEqual((byte)0, IsGpuTexture(b.Id));
            }
            PlayerUniform.ApplyUniform(second, assets, defenseUniform);
            assets.UnloadAll();
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal((byte)0, IsGpuTexture(a.Id));
                Assert.Equal((byte)0, IsGpuTexture(b.Id));
            }
            Assert.Throws<ObjectDisposedException>(() => PlayerUniform.ApplyUniform(second, assets, offenseUniform));
        }
        finally { assets.UnloadAll(); Raylib.CloseWindow(); }
    }

    [Fact]
    public void SelectingBeforeVisualInitializationHasClearError()
    {
        var assets = new AssetManager(new AssetConfig());
        Assert.Throws<InvalidOperationException>(() =>
            new BallCarrier(new TackleAlleyConfig()).ApplyUniform(assets, AuthoredUniform));
        Assert.Throws<InvalidOperationException>(() =>
            new Opponent(System.Numerics.Vector3.Zero, new TackleAlleyConfig()).ApplyUniform(assets, AuthoredUniform));
    }

    private static unsafe uint[] Albedos(Model model)
    {
        var result = new uint[model.MaterialCount];
        for (int i = 0; i < result.Length; i++)
            result[i] = model.Materials[i].Maps[(int)MaterialMapIndex.Albedo].Texture.Id;
        return result;
    }

    private static unsafe float[] Uvs(Model model)
    {
        var values = new List<float>();
        for (int i = 0; i < model.MeshCount; i++)
            if (model.Meshes[i].TexCoords != null)
                values.AddRange(new ReadOnlySpan<float>(model.Meshes[i].TexCoords, model.Meshes[i].VertexCount * 2).ToArray());
        return values.ToArray();
    }

    private static unsafe float[] Vertices(Model model)
    {
        var values = new List<float>();
        for (int i = 0; i < model.MeshCount; i++)
            values.AddRange(new ReadOnlySpan<float>(model.Meshes[i].Vertices, model.Meshes[i].VertexCount * 3).ToArray());
        return values.ToArray();
    }
}
