namespace RaylibTackleAlley.Game;

public sealed partial class TackleAlleyConfig
{
    // Fractional Speed-rating influence around the global baseline at rating 50.
    // Returners only; defender speed ratings retain their existing uniform mapping.
    public float ReturnerJogSpeedInfluence { get; set; } = .05f;
    public float ReturnerRunSpeedInfluence { get; set; } = .15f;
    public float ReturnerSprintSpeedInfluence { get; set; } = .30f;

    public void ValidateReturnerSpeedScaling()
    {
        static void Influence(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0 || value >= 1)
                throw new ArgumentOutOfRangeException(name, "Speed influence must be finite, at least zero and less than one.");
        }
        Influence(ReturnerJogSpeedInfluence, nameof(ReturnerJogSpeedInfluence));
        Influence(ReturnerRunSpeedInfluence, nameof(ReturnerRunSpeedInfluence));
        Influence(ReturnerSprintSpeedInfluence, nameof(ReturnerSprintSpeedInfluence));

        // Tier differences are linear on either side of neutral, so checking the
        // endpoints and midpoint guarantees ordered finite speeds for every rating.
        foreach (int rating in new[] { 1, 50, 100 })
        {
            float jog = PlayerSlowSpeed * PlayerMovementAttributes.ReturnerSpeedMultiplier(rating, ReturnerJogSpeedInfluence);
            float run = PlayerForwardSpeed * PlayerMovementAttributes.ReturnerSpeedMultiplier(rating, ReturnerRunSpeedInfluence);
            float sprint = PlayerSprintSpeed * PlayerMovementAttributes.ReturnerSpeedMultiplier(rating, ReturnerSprintSpeedInfluence);
            if (!float.IsFinite(jog) || !float.IsFinite(run) || !float.IsFinite(sprint) ||
                jog < 0 || !(jog < run && run < sprint))
                throw new ArgumentException($"Returner speed scaling must keep finite 0 <= jog < run < sprint at every Speed rating (failed at {rating}).");
        }
    }
}
