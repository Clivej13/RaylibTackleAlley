using System.Numerics;

namespace RaylibTackleAlley.Game;

/// <summary>Immutable per-player SI dimensions and force factors, calculated at construction.</summary>
public sealed class PlayerPhysicalAttributes
{
    public const float ReferenceMass = 110f;
    public const float MaximumHeightRatio = 2.05f / PlayerVisualProfile.ReferenceHeight;
    public const float MinimumRadiusScale = .90f, MaximumRadiusScale = 1.05f;
    public float HeightRatio { get; }
    public float RadiusScale { get; }
    public float TotalMass { get; }
    public IReadOnlyList<float> BodyPartMasses { get; }
    public IReadOnlyList<float> BodyPartRadii { get; }
    public IReadOnlyList<float> BodyPartContactRadii { get; }
    public float CollisionHeight { get; }
    public float TorsoRadius { get; }
    public float PelvisRadius { get; }
    public float BoundaryRadius { get; }
    public float WrapReach { get; }
    public float DiveReach { get; }
    public float TackleContactHeight { get; }
    public float StrengthMultiplier { get; }
    public float TackleForceMultiplier { get; }
    public float TackleResistanceMultiplier { get; }
    public float BalanceSupportMultiplier { get; }
    public float ActiveMotorMultiplier { get; }
    public float ImpulseLimitScale { get; }
    public float RecoveryRate { get; }

    public PlayerPhysicalAttributes(PlayerProfile profile, TackleAlleyConfig config)
    {
        profile.Validate();
        var visual = new PlayerVisualProfile(profile);
        HeightRatio = visual.HeightRatio;
        RadiusScale = Math.Clamp(MathF.Sqrt(HeightRatio) * MathF.Sqrt(visual.WidthRatio * visual.DepthRatio),
            MinimumRadiusScale, MaximumRadiusScale);
        TotalMass = profile.Weight;
        float[] proportions = [config.RagdollPelvisMass, config.RagdollChestMass, config.RagdollHeadMass,
            config.RagdollUpperArmMass, config.RagdollLowerArmMass,
            config.RagdollUpperArmMass, config.RagdollLowerArmMass,
            config.RagdollUpperLegMass, config.RagdollLowerLegMass,
            config.RagdollUpperLegMass, config.RagdollLowerLegMass];
        foreach (float part in proportions) Positive(part);
        float sum = proportions.Sum();
        Positive(sum);
        var masses = proportions.Select(m => TotalMass * (m / sum)).ToArray();
        masses[^1] = TotalMass - masses.Take(masses.Length - 1).Sum();
        BodyPartMasses = Array.AsReadOnly(masses);
        BodyPartRadii = Array.AsReadOnly(new[] { config.RagdollPelvisRadius, config.RagdollChestRadius,
            config.RagdollHeadRadius, config.RagdollUpperArmRadius, config.RagdollLowerArmRadius,
            config.RagdollUpperArmRadius, config.RagdollLowerArmRadius,
            config.RagdollUpperLegRadius, config.RagdollLowerLegRadius,
            config.RagdollUpperLegRadius, config.RagdollLowerLegRadius }.Select(r => r * RadiusScale).ToArray());
        CollisionHeight = profile.Height;
        TorsoRadius = config.ContactTorsoRadius * RadiusScale;
        PelvisRadius = config.ContactPelvisRadius * RadiusScale;
        BodyPartContactRadii = Array.AsReadOnly(BodyPartRadii.Select((r, i) => i == 0 ? PelvisRadius : i == 1 ? TorsoRadius : r * config.ContactLimbRadiusScale).ToArray());
        BoundaryRadius = config.PlayerBoundaryRadius * RadiusScale;
        WrapReach = config.OpponentWrapCommitDistance * HeightRatio;
        DiveReach = config.OpponentLungeReachDistance * HeightRatio;
        TackleContactHeight = 1.1f * HeightRatio;
        StrengthMultiplier = PlayerMovementAttributes.RatingMultiplier(profile.Strength, .8f, 1.2f);
        TackleForceMultiplier = TackleResistanceMultiplier = ActiveMotorMultiplier = StrengthMultiplier;
        BalanceSupportMultiplier = PlayerMovementAttributes.RatingMultiplier(profile.Strength, .9f, 1.1f);
        ImpulseLimitScale = Math.Clamp(TotalMass / ReferenceMass, .65f, 1.45f);
        RecoveryRate = Math.Clamp(BalanceSupportMultiplier / MathF.Pow(TotalMass / ReferenceMass, .15f), .85f, 1.15f);
        Validate();
    }

    public Vector3 Momentum(Vector3 velocity)
    {
        if (!float.IsFinite(velocity.LengthSquared())) throw new ArgumentException("Invalid velocity.");
        return TotalMass * velocity;
    }

    public void Validate()
    {
        foreach (float value in new[] { HeightRatio, RadiusScale, TotalMass, CollisionHeight, TorsoRadius,
            PelvisRadius, BoundaryRadius, WrapReach, DiveReach, TackleContactHeight, StrengthMultiplier,
            TackleForceMultiplier, TackleResistanceMultiplier, BalanceSupportMultiplier,
            ActiveMotorMultiplier, ImpulseLimitScale, RecoveryRate }.Concat(BodyPartMasses).Concat(BodyPartRadii).Concat(BodyPartContactRadii))
            Positive(value);
        if (Math.Abs(BodyPartMasses.Sum() - TotalMass) > .001f)
            throw new ArgumentException("Body masses must sum to player weight.");
    }

    private static void Positive(float value)
    {
        if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("Physical attributes must be finite and positive.");
    }
}
