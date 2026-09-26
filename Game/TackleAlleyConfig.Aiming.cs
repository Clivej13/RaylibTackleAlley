namespace RaylibTackleAlley.Game;

public sealed partial class TackleAlleyConfig
{
    public float LungeCorrectionWindowFraction { get; set; } = .4f;
    public float LungeCorrectionRateDegrees { get; set; } = 90f;
    public float LungeMaximumCorrectionDegrees { get; set; } = 18f;
    public float TackleMinimumPredictionConfidence { get; set; } = .2f;
    public float TackleAccelerationPredictionLimit { get; set; } = 12f;
    public int TacklePredictionIterations { get; set; } = 64;
    public bool DrawTackleAimingDebug { get; set; }

    internal TackleAlleyConfig ForDefender(DefenderProfile profile)
    {
        var copy = (TackleAlleyConfig)MemberwiseClone();
        copy.LungeCorrectionWindowFraction = profile.CorrectionWindowFraction;
        copy.LungeCorrectionRateDegrees = profile.CorrectionRateDegrees;
        copy.LungeMaximumCorrectionDegrees = profile.MaximumCorrectionDegrees;
        return copy;
    }

    public void ValidateTackleAiming()
    {
        Bound(LungeCorrectionWindowFraction, 0, .8f, nameof(LungeCorrectionWindowFraction));
        Bound(LungeCorrectionRateDegrees, 0, 360, nameof(LungeCorrectionRateDegrees));
        Bound(LungeMaximumCorrectionDegrees, 0, 45, nameof(LungeMaximumCorrectionDegrees));
        Bound(TackleMinimumPredictionConfidence, 0, 1, nameof(TackleMinimumPredictionConfidence));
        Bound(TackleAccelerationPredictionLimit, .1f, 100, nameof(TackleAccelerationPredictionLimit));
        if (TacklePredictionIterations is < 8 or > 512)
            throw new ArgumentOutOfRangeException(nameof(TacklePredictionIterations));
    }

    private static void Bound(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max) throw new ArgumentOutOfRangeException(name);
    }
}
