namespace RaylibTackleAlley.Game;

/// <summary>Optional refinements of pursuit, braking and wrap selection. No movement or reach overrides.</summary>
public sealed record DefenderApproachTuning
{
    public required float MaximumPredictionSeconds { get; init; }
    public required float ReadyHalfAngleDegrees { get; init; }
    public required float WrapLookAheadSeconds { get; init; }
    public required float SquareUpHalfAngleDegrees { get; init; }

    public void Validate()
    {
        Range(MaximumPredictionSeconds, 0, 1, nameof(MaximumPredictionSeconds));
        Range(ReadyHalfAngleDegrees, 0, 90, nameof(ReadyHalfAngleDegrees));
        Range(WrapLookAheadSeconds, 0, .6f, nameof(WrapLookAheadSeconds));
        Range(SquareUpHalfAngleDegrees, 0, 45, nameof(SquareUpHalfAngleDegrees));
        static void Range(float value, float min, float max, string name)
        {
            if (!float.IsFinite(value) || value < min || value > max)
                throw new ArgumentException($"ApproachTuning.{name} must be finite in [{min}, {max}].");
        }
    }
}
