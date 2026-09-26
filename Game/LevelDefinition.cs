using System.Numerics;

namespace RaylibTackleAlley.Game;

/// <summary>Immutable run setup with resolved body/behavior profiles and no progression state.</summary>
public sealed class LevelDefinition
{
    public string Id { get; }
    public string Name { get; }
    public float FieldWidth { get; }
    public Vector3 PlayerSpawn { get; }
    public IReadOnlyList<LevelDefender> Defenders { get; }

    internal LevelDefinition(string id, string name, float fieldWidth, Vector3 playerSpawn,
        IEnumerable<LevelDefender> defenders)
    {
        Id = id;
        Name = name;
        FieldWidth = fieldWidth;
        PlayerSpawn = playerSpawn;
        Defenders = Array.AsReadOnly(defenders.ToArray());
    }

    // Recheck geometry against the actual global field/asset dimensions when a game
    // applies a catalog loaded with a different configuration.
    internal void ValidateGeometry(TackleAlleyConfig tuning)
    {
        if (Defenders.Count == 0)
            throw new InvalidDataException($"Level '{Id}' requires a nonempty Defenders list.");
        if (!float.IsFinite(tuning.FieldAssetWidth) || !float.IsFinite(tuning.StadiumAssetWidth) ||
            !float.IsFinite(tuning.FieldAssetLength) || !float.IsFinite(tuning.StadiumAssetLength) ||
            !float.IsFinite(tuning.PlayerBoundaryRadius) || tuning.PlayerBoundaryRadius < 0 ||
            tuning.FieldAssetWidth <= 0 || tuning.StadiumAssetWidth <= 0 ||
            tuning.FieldAssetLength < tuning.FieldLength || tuning.StadiumAssetLength < tuning.FieldLength)
            throw new InvalidDataException($"Level '{Id}': invalid turf/stadium dimensions or player boundary radius.");
        float maxWidth = Math.Min(tuning.FieldAssetWidth, tuning.StadiumAssetWidth);
        if (!float.IsFinite(FieldWidth) || FieldWidth <= 0 || FieldWidth > maxWidth ||
            FieldWidth <= tuning.PlayerBoundaryRadius * 2)
            throw new InvalidDataException($"Level '{Id}': FieldWidth must fit the turf and be wider than the player's diameter.");
        if (!float.IsFinite(tuning.FieldLength) || !float.IsFinite(tuning.EndZoneLength) ||
            tuning.EndZoneLength <= 0 || tuning.FieldLength <= tuning.EndZoneLength)
            throw new InvalidDataException($"Level '{Id}': invalid field length/end zone dimensions.");
        float goalLine = -(tuning.FieldLength - tuning.EndZoneLength);
        void Spawn(Vector3 position, string path)
        {
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
                position.Y != 0 || Math.Abs(position.X) >= FieldWidth / 2 ||
                position.Z > 0 || position.Z <= goalLine)
                throw new InvalidDataException($"Level '{Id}': {path} must be on the ground, inside the sidelines and between the start and goal line.");
        }
        Spawn(PlayerSpawn, "PlayerSpawn");
        for (int i = 0; i < Defenders.Count; i++) Spawn(Defenders[i].Position, $"Defenders[{i}]");
    }

    /// <summary>Compatibility for standalone callers/tests that still construct legacy tuning.
    /// Authored startup levels always go through LevelCatalog's stricter validation.</summary>
    internal static LevelDefinition FromLegacyConfiguration(TackleAlleyConfig config)
    {
        config.Validate();
        return new("legacy-setup", "Legacy setup", config.FieldWidth, config.PlayerSpawn.Position,
            config.OpponentSpawns.Select((s, i) => new LevelDefender(s.Position, $"legacy-{i}", s.Profile, DefenderProfile.Balanced(config))));
    }
}

/// <summary>Independent player identity/body reference and shared AI behavior profile.</summary>
public sealed record LevelDefender(Vector3 Position, string ProfileReference, PlayerProfile Profile,
    DefenderProfile BehaviorProfile);
