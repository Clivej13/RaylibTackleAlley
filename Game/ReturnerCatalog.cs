using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace RaylibTackleAlley.Game;

/// <summary>Stable selection key plus the same immutable profile used by gameplay and previews.</summary>
public sealed record ReturnerDefinition
{
    public required string Id { get; init; }
    public required PlayerProfile Profile { get; init; }
    public string? Uniform { get; init; }
    public string? Taunt { get; init; }
}

public sealed class ReturnerCatalog
{
    public IReadOnlyList<ReturnerDefinition> Returners { get; }
    private readonly Dictionary<string, ReturnerDefinition> _byId;

    private ReturnerCatalog(ReturnerDefinition[] returners)
    {
        if (returners.Length == 0) throw new InvalidDataException("Returners must contain at least one player.");
        _byId = new(StringComparer.Ordinal);
        foreach (var entry in returners)
        {
            if (entry is null || entry.Id is null || !Regex.IsMatch(entry.Id, @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z"))
                throw new InvalidDataException("Each returner needs a stable lowercase ID (letters, digits and hyphens).");
            if (!_byId.TryAdd(entry.Id, entry))
                throw new InvalidDataException($"Duplicate returner ID '{entry.Id}'.");
            if (entry.Profile is null) throw new InvalidDataException($"Returner '{entry.Id}' needs a Profile.");
            if (entry.Uniform is not null && string.IsNullOrWhiteSpace(entry.Uniform))
                throw new InvalidDataException($"Returner '{entry.Id}' has an empty Uniform asset key.");
            if (entry.Taunt is not null && string.IsNullOrWhiteSpace(entry.Taunt))
                throw new InvalidDataException($"Returner '{entry.Id}' has an empty Taunt asset key.");
            try { entry.Profile.Validate($"Returners[{entry.Id}].Profile"); }
            catch (ArgumentException ex) { throw new InvalidDataException(ex.Message, ex); }
        }
        Returners = Array.AsReadOnly(returners);
    }

    /// <summary>Exclude unavailable optional exports before the asset pipeline validates the manifest.</summary>
    public RaylibGameFramework.Assets.AssetConfig PrepareAssets(
        RaylibGameFramework.Assets.AssetConfig assets, string offenseUniform)
    {
        var optionalKeys = Returners.SelectMany(entry => new[] { entry.Uniform, entry.Taunt })
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        // The base jersey and original taunt remain required, even if assigned explicitly.
        optionalKeys.Remove(offenseUniform);
        optionalKeys.Remove(DefaultTaunt);
        return new()
        {
            Assets = assets.Assets.Where(asset => !optionalKeys.Contains(asset.Key) ||
                File.Exists(Path.Combine(AppContext.BaseDirectory, asset.Path))).ToList()
        };
    }

    public const string DefaultTaunt = "FootballPlayerTauntBicepFlexAnimations";

    /// <summary>Resolve optional artwork before AssetManager queues native loads.
    /// The authored catalog stays unchanged; preview and carrier share this resolved catalog.</summary>
    public ReturnerCatalog ResolveAvailableAssets(RaylibGameFramework.Assets.AssetConfig assets,
        string offenseUniform, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        bool Available(string? key, string type) => key is not null &&
            assets.Assets.Any(asset => asset.Key == key && asset.Type.ToString() == type &&
                fileExists(Path.Combine(AppContext.BaseDirectory, asset.Path)));
        return new(Returners.Select(entry => entry with
        {
            Uniform = Available(entry.Uniform, "Texture") ? entry.Uniform : offenseUniform,
            Taunt = Available(entry.Taunt, "ModelAnimations") ? entry.Taunt : DefaultTaunt
        }).ToArray());
    }

    public ReturnerDefinition Resolve(string id) =>
        _byId.TryGetValue(id, out var entry) ? entry :
            throw new ArgumentException($"Unknown returner ID '{id}'.", nameof(id));

    public static ReturnerCatalog Load(string path)
    {
        try { return Parse(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        { throw new InvalidDataException($"Invalid returner config '{path}': {ex.Message}", ex); }
    }

    public static ReturnerCatalog Parse(string json)
    {
        var document = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    type =>
                    {
                        // Catalog entries must be explicit; legacy tuning profiles may
                        // still use PlayerProfile defaults elsewhere in the game.
                        if (type.Type == typeof(PlayerProfile))
                            foreach (var property in type.Properties) property.IsRequired = true;
                    }
                }
            }
        }) ?? throw new InvalidDataException("Returner config must be an object.");
        return new(document.Returners ?? throw new InvalidDataException("Returners is required."));
    }

    private sealed class Document
    {
        public required ReturnerDefinition[] Returners { get; init; }
    }
}
