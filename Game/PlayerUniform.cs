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

    /// <summary>Caller owns the returned texture and must unload it before closing the graphics context.</summary>
    public static unsafe Texture2D ApplyNumberedUniform(ModelInstance instance, AssetManager assets, string textureKey, int number)
    {
        if (number is < 0 or > 99) throw new ArgumentOutOfRangeException(nameof(number));
        // Validate before generating a texture or mutating the instance.
        Model model = instance.Model;
        var material = UniformMaterial.Value;
        if (model.MaterialCount != material.Count || model.Materials == null || model.Materials[material.Index].Maps == null)
            throw new InvalidDataException("Uniforms require the shared FootballPlayer model.");
        Texture2D source = assets.GetTexture(textureKey);
        if (!Raylib.IsTextureValid(source)) throw new InvalidDataException($"Uniform texture '{textureKey}' is invalid.");
        Image atlas = Raylib.LoadImageFromTexture(source);
        Texture2D texture = default;
        try
        {
            PaintNumber(ref atlas, number);
            texture = Raylib.LoadTextureFromImage(atlas);
            if (!Raylib.IsTextureValid(texture)) throw new InvalidDataException("Could not create numbered uniform.");
            Raylib.GenTextureMipmaps(ref texture);
            Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
            model.Materials[material.Index].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
            return texture;
        }
        catch
        {
            if (texture.Id != 0) Raylib.UnloadTexture(texture);
            throw;
        }
        finally { Raylib.UnloadImage(atlas); }
    }

    private static readonly string[] Digits =
    [
        "01110/11011/11011/11011/11011/11011/01110",
        "00110/01110/00110/00110/00110/00110/01111",
        "01110/11011/00011/00110/01100/11000/11111",
        "11110/00011/00011/01110/00011/00011/11110",
        "11011/11011/11011/11111/00011/00011/00011",
        "11111/11000/11000/11110/00011/00011/11110",
        "01110/11000/11000/11110/11011/11011/01110",
        "11111/00011/00110/00110/01100/01100/01100",
        "01110/11011/11011/01110/11011/11011/01110",
        "01110/11011/11011/01111/00011/00011/01110"
    ];

    public static void PaintNumber(ref Image atlas, int number)
    {
        if (number is < 0 or > 99) throw new ArgumentOutOfRangeException(nameof(number));
        string digits = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Fixed Chest/Back atlas panels. Preserve team artwork outside the number patches.
        foreach (float center in new[] { .16f, .48f })
        {
            int cx = (int)MathF.Round(atlas.Width * center);
            int cy = (int)MathF.Round(atlas.Height * .205f);
            int cellX = Math.Max(1, (int)MathF.Round(atlas.Width * 46f / 2048f));
            int cellY = Math.Max(1, (int)MathF.Round(atlas.Height * 46f / 2048f));
            Color background = Raylib.GetImageColor(atlas, cx, Math.Max(0, cy - 5 * cellY));
            // Contrast follows the selected jersey colour, including custom atlases.
            Color ink = background.R * .299f + background.G * .587f + background.B * .114f > 140
                ? new Color(16, 35, 68, 255) : new Color(245, 245, 245, 255);
            Raylib.ImageDrawRectangle(ref atlas, cx - 6 * cellX, cy - 4 * cellY, 12 * cellX, 8 * cellY, background);
            int left = cx - (digits.Length * 6 - 1) * cellX / 2;
            int top = cy - 7 * cellY / 2;
            for (int d = 0; d < digits.Length; d++)
            {
                var rows = Digits[digits[d] - '0'].Split('/');
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                        if (rows[row][col] == '1')
                            Raylib.ImageDrawRectangle(ref atlas, left + (d * 6 + col) * cellX,
                                top + row * cellY, cellX, cellY, ink);
            }
        }
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
