using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

/// <summary>Immutable identity, body dimensions and gameplay ratings shared by returners and defenders.
/// Dimensions use metres/kilograms, build uses [-1, 1], and ratings use [1, 100].</summary>
public sealed record PlayerProfile
{
    public string Name { get; init; } = "Player";
    public int JerseyNumber { get; init; } = 10;
    public float Height { get; init; } = 2f;
    public float Weight { get; init; } = 110f;
    public float Build { get; init; }
    public int Strength { get; init; } = 50;
    public int Speed { get; init; } = 50;
    public int Acceleration { get; init; } = 50;
    public int Agility { get; init; } = 50;
    public int Juke { get; init; } = 50;

    public void Validate(string path = "PlayerProfile")
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException($"{path}.Name is required.");
        if (JerseyNumber is < 0 or > 99)
            throw new ArgumentException($"{path}.JerseyNumber must be between 0 and 99.");
        Check(Height, 1.65f, 2.05f, $"{path}.Height (metres)");
        Check(Weight, 70f, 160f, $"{path}.Weight (kilograms)");
        Check(Build, -1f, 1f, $"{path}.Build");
        Check(Strength, 1, 100, $"{path}.Strength");
        Check(Speed, 1, 100, $"{path}.Speed");
        Check(Acceleration, 1, 100, $"{path}.Acceleration");
        Check(Agility, 1, 100, $"{path}.Agility");
        Check(Juke, 1, 100, $"{path}.Juke");
    }

    private static void Check(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name} must be finite and between {min} and {max}.");
    }
}

/// <summary>Calculated once per player. Build affects width/depth, never vertical scale.</summary>
public sealed class PlayerVisualProfile
{
    public const float ReferenceHeight = 2f; // Existing default PlayerVisualHeight.
    public PlayerProfile Profile { get; }
    public float HeightRatio { get; }
    public float BuildValue { get; }
    public float WidthRatio { get; }
    public float DepthRatio { get; }
    public string BuildDescription => BuildValue < -.2f ? "Lean" :
        BuildValue < .25f ? "Average" : BuildValue < .65f ? "Stocky" : "Heavy";

    public PlayerVisualProfile(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        Profile = profile;
        HeightRatio = profile.Height / ReferenceHeight;
        float referenceWeight = 110f * HeightRatio * HeightRatio * HeightRatio;
        BuildValue = Math.Clamp((profile.Weight / referenceWeight - 1f) * .9f + profile.Build * .5f, -1f, 1f);
        WidthRatio = Math.Clamp(1f + .14f * BuildValue, .86f, 1.14f);
        DepthRatio = Math.Clamp(1f + .18f * BuildValue, .82f, 1.18f);
    }

    public Vector3 ModelScale(BoundingBox bounds)
    {
        float height = bounds.Max.Y - bounds.Min.Y;
        if (!float.IsFinite(height) || height <= 0f)
            throw new InvalidDataException("FootballPlayer must have finite positive model height.");
        float scale = ReferenceHeight / height * HeightRatio;
        return new(scale * WidthRatio, scale, scale * DepthRatio);
    }

    public float GroundOffset(BoundingBox bounds) => -bounds.Min.Y * ModelScale(bounds).Y;
}
