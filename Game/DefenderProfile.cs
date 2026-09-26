using System.Text.RegularExpressions;

namespace RaylibTackleAlley.Game;

/// <summary>Immutable decision parameters, separate from a defender's body/movement PlayerProfile.
/// Every profile is interpreted by the same DefenderDecision pipeline.</summary>
public sealed record DefenderProfile
{
    public string Id { get; init; } = "balanced";
    public string Name { get; init; } = "Balanced";
    // Reaction travel reserved before braking, matching the existing AI. Not an input polling delay.
    public float ReactionTime { get; init; } = .15f;
    public float PursuitPredictionStrength { get; init; } = 1;
    public float PursuitAggression { get; init; } = 1;
    public float ContainBias { get; init; }
    public float ApproachDistance { get; init; } = 8;
    public float BreakdownDistance { get; init; } = 4;
    public float BreakdownExitDistance { get; init; } = 5;
    public float TackleCommitDistance { get; init; } = 4.5f;
    public float WrapPreference { get; init; } = 1;
    public float LungePreference { get; init; } = 1;
    public float CorrectionWindowFraction { get; init; } = .4f;
    public float CorrectionRateDegrees { get; init; } = 90;
    public float MaximumCorrectionDegrees { get; init; } = 18;

    public void Validate()
    {
        if (Id is null || !Regex.IsMatch(Id, @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z"))
            throw new ArgumentException("DefenderProfile.Id needs a stable lowercase ID.");
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException($"DefenderProfile '{Id}' needs a Name.");
        void Range(float value, float min, float max, string field)
        {
            if (!float.IsFinite(value) || value < min || value > max)
                throw new ArgumentException($"DefenderProfile '{Id}'.{field} must be finite in [{min}, {max}].");
        }
        Range(ReactionTime, 0, 2, nameof(ReactionTime));
        Range(PursuitPredictionStrength, 0, 2, nameof(PursuitPredictionStrength));
        Range(PursuitAggression, .25f, 2, nameof(PursuitAggression));
        Range(ContainBias, 0, 1, nameof(ContainBias));
        Range(ApproachDistance, .1f, 50, nameof(ApproachDistance));
        Range(BreakdownDistance, .1f, 25, nameof(BreakdownDistance));
        Range(BreakdownExitDistance, .1f, 30, nameof(BreakdownExitDistance));
        Range(TackleCommitDistance, .1f, 10, nameof(TackleCommitDistance));
        Range(WrapPreference, 0, 1, nameof(WrapPreference));
        Range(LungePreference, 0, 1, nameof(LungePreference));
        Range(CorrectionWindowFraction, 0, .8f, nameof(CorrectionWindowFraction));
        Range(CorrectionRateDegrees, 0, 360, nameof(CorrectionRateDegrees));
        Range(MaximumCorrectionDegrees, 0, 45, nameof(MaximumCorrectionDegrees));
        if (BreakdownExitDistance < BreakdownDistance || ApproachDistance < BreakdownExitDistance)
            throw new ArgumentException($"DefenderProfile '{Id}' requires ApproachDistance >= BreakdownExitDistance >= BreakdownDistance.");
        if (WrapPreference == 0 && LungePreference == 0)
            throw new ArgumentException($"DefenderProfile '{Id}' must enable at least one tackle preference.");
    }

    // Standalone/legacy callers preserve their existing customized tuning. Authored
    // games resolve explicit values from defender-profiles.json instead.
    public static DefenderProfile Balanced(TackleAlleyConfig config) => new()
    {
        ReactionTime = config.OpponentBreakdownReactionSeconds,
        ApproachDistance = Math.Max(config.OpponentSprintDistance, config.OpponentReadyExitDistance),
        BreakdownDistance = config.OpponentReadyEnterDistance,
        BreakdownExitDistance = config.OpponentReadyExitDistance,
        TackleCommitDistance = config.OpponentMaximumLungeReachDistance,
        CorrectionWindowFraction = config.LungeCorrectionWindowFraction,
        CorrectionRateDegrees = config.LungeCorrectionRateDegrees,
        MaximumCorrectionDegrees = config.LungeMaximumCorrectionDegrees
    };
}
