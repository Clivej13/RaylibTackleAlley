using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RaylibTackleAlley.Game;

public sealed class DefenderProfileCatalog
{
    public IReadOnlyList<DefenderProfile> Profiles { get; }
    private readonly Dictionary<string, DefenderProfile> _byId;

    private DefenderProfileCatalog(DefenderProfile[] profiles)
    {
        if (profiles.Length == 0) throw new InvalidDataException("Profiles must contain at least one defender profile.");
        _byId = new(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            if (profile is null) throw new InvalidDataException("Profiles cannot contain null entries.");
            profile.Validate();
            if (!_byId.TryAdd(profile.Id, profile))
                throw new InvalidDataException($"Duplicate defender behavior profile ID '{profile.Id}'.");
        }
        Profiles = Array.AsReadOnly(profiles);
    }

    public DefenderProfile Resolve(string id) =>
        _byId.TryGetValue(id, out var profile) ? profile :
            throw new ArgumentException($"Unknown defender behavior profile '{id}'.", nameof(id));

    internal static DefenderProfileCatalog LegacyBalanced(TackleAlleyConfig config) =>
        new([DefenderProfile.Balanced(config)]);

    public static DefenderProfileCatalog Load(string path)
    {
        try { return Parse(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        { throw new InvalidDataException($"Invalid defender profile catalog '{path}': {ex.Message}", ex); }
    }

    public static DefenderProfileCatalog Parse(string json)
    {
        try
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
                            // Authored presets must be explicit so a typo/omission cannot
                            // silently revert one tuning knob to a code default.
                            if (type.Type == typeof(DefenderProfile))
                                foreach (var property in type.Properties) property.IsRequired = true;
                        }
                    }
                }
            }) ?? throw new InvalidDataException("Defender profile catalog must be an object.");
            return new(document.Profiles ?? throw new InvalidDataException("Profiles is required."));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid defender profile catalog: {ex.Message}", ex); }
    }

    private sealed class Document
    {
        public required DefenderProfile[] Profiles { get; init; }
    }
}
