using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RaylibTackleAlley.Game;

/// <summary>Loads authored levels and resolves body and behavior profile references at startup.</summary>
public sealed class LevelCatalog
{
    public IReadOnlyList<LevelDefinition> Levels { get; }
    private readonly Dictionary<string, LevelDefinition> _byId;

    private LevelCatalog(LevelDefinition[] levels)
    {
        Levels = Array.AsReadOnly(levels);
        _byId = levels.ToDictionary(level => level.Id, StringComparer.Ordinal);
    }

    public LevelDefinition Resolve(string id) =>
        _byId.TryGetValue(id, out var level) ? level :
            throw new ArgumentException($"Unknown level ID '{id}'.", nameof(id));

    public static LevelCatalog Load(string path, TackleAlleyConfig tuning, DefenderProfileCatalog? behaviors = null)
    {
        try { return Parse(File.ReadAllText(path), tuning, behaviors); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid level catalog '{path}': {ex.Message}", ex); }
    }

    public static LevelCatalog Parse(string json, TackleAlleyConfig tuning, DefenderProfileCatalog? behaviors = null)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        // Compatibility callers keep their customized Balanced tuning; startup passes the authored registry.
        behaviors ??= DefenderProfileCatalog.LegacyBalanced(tuning);
        try
        {
            var document = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new InvalidDataException("Level catalog must be an object.");
            if (document.DefenderProfiles is not { Length: > 0 })
                throw new InvalidDataException("DefenderProfiles must contain at least one named profile.");
            if (document.Levels is not { Length: > 0 })
                throw new InvalidDataException("Levels must contain at least one level.");
            var profiles = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);
            foreach (var entry in document.DefenderProfiles)
            {
                if (entry is null) throw new InvalidDataException("DefenderProfiles cannot contain null entries.");
                ValidateId(entry.Id, "profile");
                if (entry.Profile is null) throw new InvalidDataException($"Profile '{entry.Id}' is required.");
                entry.Profile.Validate($"DefenderProfiles[{entry.Id}]");
                if (!profiles.TryAdd(entry.Id, entry.Profile))
                    throw new InvalidDataException($"Duplicate defender profile ID '{entry.Id}'.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var levels = new List<LevelDefinition>();
            foreach (var entry in document.Levels)
            {
                if (entry is null) throw new InvalidDataException("Levels cannot contain null entries.");
                ValidateId(entry.Id, "level");
                if (!ids.Add(entry.Id)) throw new InvalidDataException($"Duplicate level ID '{entry.Id}'.");
                if (string.IsNullOrWhiteSpace(entry.Name))
                    throw new InvalidDataException($"Level '{entry.Id}' requires a Name.");
                if (entry.PlayerSpawn is null)
                    throw new InvalidDataException($"Level '{entry.Id}' requires PlayerSpawn.");
                if (entry.Defenders is not { Length: > 0 })
                    throw new InvalidDataException($"Level '{entry.Id}' requires a nonempty Defenders list.");
                var defenders = entry.Defenders.Select((spawn, index) =>
                {
                    if (spawn is null || string.IsNullOrWhiteSpace(spawn.Profile) ||
                        !profiles.TryGetValue(spawn.Profile, out var profile))
                        throw new InvalidDataException($"Level '{entry.Id}': Defenders[{index}] has an invalid Profile reference '{spawn?.Profile}'.");
                    if (string.IsNullOrWhiteSpace(spawn.BehaviorProfile))
                        throw new InvalidDataException($"Level '{entry.Id}': Defenders[{index}] requires a BehaviorProfile reference.");
                    DefenderProfile behavior;
                    try { behavior = behaviors.Resolve(spawn.BehaviorProfile); }
                    catch (ArgumentException ex)
                    { throw new InvalidDataException($"Level '{entry.Id}': Defenders[{index}].BehaviorProfile: {ex.Message}", ex); }
                    return new LevelDefender(new(spawn.X, 0, spawn.Z), spawn.Profile, profile, behavior);
                });
                var level = new LevelDefinition(entry.Id, entry.Name, entry.FieldWidth,
                    new Vector3(entry.PlayerSpawn.X, 0, entry.PlayerSpawn.Z), defenders);
                level.ValidateGeometry(tuning);
                levels.Add(level);
            }
            return new(levels.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid level catalog: {ex.Message}", ex); }
    }

    private static void ValidateId(string? id, string kind)
    {
        if (id is null || !Regex.IsMatch(id, @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z"))
            throw new InvalidDataException($"Each {kind} needs a stable lowercase ID (letters, digits and hyphens).");
    }

    private sealed class Document
    {
        public required ProfileEntry[] DefenderProfiles { get; init; }
        public required LevelEntry[] Levels { get; init; }
    }

    private sealed class ProfileEntry
    {
        public required string Id { get; init; }
        public required PlayerProfile Profile { get; init; }
    }

    private sealed class LevelEntry
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required float FieldWidth { get; init; }
        public required PositionEntry PlayerSpawn { get; init; }
        public required DefenderEntry[] Defenders { get; init; }
    }

    private class PositionEntry
    {
        public required float X { get; init; }
        public required float Z { get; init; }
    }

    private sealed class DefenderEntry : PositionEntry
    {
        public required string Profile { get; init; }
        public required string BehaviorProfile { get; init; }
    }
}
