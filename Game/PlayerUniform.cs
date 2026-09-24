using System.Text.Json;
using Raylib_cs;
using RaylibGameFramework.Assets;

namespace RaylibTackleAlley.Game;

/// <summary>Applies borrowed AssetManager textures to instances of the shared player GLB.</summary>
public static class PlayerUniform
{
    public const string ModelPath = "Assets/Models/football_player.glb";
    private static readonly Lazy<(int Index, int Count)> UniformMaterial = new(ReadUniformMaterial);

    /// <summary>
    /// Call on the graphics thread after loading the texture asset. Keep the texture
    /// required while any player uses it; AssetManager.UnloadAll owns final cleanup.
    /// </summary>
    public static unsafe void ApplyUniform(ModelInstance instance, AssetManager assets, string textureKey)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureKey);
        Model model = instance.Model; // Also rejects released instances.
        var material = UniformMaterial.Value;
        if (model.Materials == null || model.MaterialCount != material.Count ||
            model.Materials[material.Index].Maps == null)
            throw new InvalidDataException("Uniforms require an instance of the shared FootballPlayer model.");

        Texture2D texture = assets.GetTexture(textureKey);
        if (!Raylib.IsTextureValid(texture))
            throw new InvalidDataException($"Uniform texture '{textureKey}' is not valid.");

        // Model instances have independent material maps. The asset manager owns the
        // texture; neither replacing this borrowed handle nor UnloadModel releases it.
        // Preserve the authored colour factor, all other maps, and every mesh attribute.
        model.Materials[material.Index].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
    }

    private static (int Index, int Count) ReadUniformMaterial()
    {
        // raylib's Material does not retain glTF names. Resolve the name in the GLB's
        // JSON instead; raylib 6 prepends its default material at index zero.
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, ModelPath));
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x46546C67 || reader.ReadUInt32() != 2 ||
            reader.ReadUInt32() != stream.Length)
            throw new InvalidDataException("FootballPlayer must be a valid GLB version 2.");
        uint length = reader.ReadUInt32();
        if (reader.ReadUInt32() != 0x4E4F534A || length > stream.Length - stream.Position ||
            length > int.MaxValue)
            throw new InvalidDataException("FootballPlayer GLB must start with a JSON chunk.");
        using var document = JsonDocument.Parse(reader.ReadBytes((int)length));
        var materials = document.RootElement.GetProperty("materials");
        int index = -1;
        for (int i = 0; i < materials.GetArrayLength(); i++)
        {
            if (!materials[i].TryGetProperty("name", out var name) || name.GetString() != "Uniform")
                continue;
            if (index >= 0)
                throw new InvalidDataException("FootballPlayer contains more than one material named Uniform.");
            index = i + 1;
        }
        if (index < 0)
            throw new InvalidDataException("FootballPlayer has no material named Uniform.");
        return (index, materials.GetArrayLength() + 1);
    }
}
