using System.Text.Json.Serialization;

namespace RaylibTackleAlley.Game;

/// <summary>Fractional rating influence around the baseline at rating 50.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MovementTierInfluence
{
    public float Jog { get; set; }
    public float Run { get; set; }
    public float Sprint { get; set; }

    internal void Validate(string path)
    {
        Check(Jog, nameof(Jog));
        Check(Run, nameof(Run));
        Check(Sprint, nameof(Sprint));
        void Check(float value, string tier)
        {
            if (!float.IsFinite(value) || value < 0 || value >= 1)
                throw new ArgumentException($"{path}.{tier} must be finite, at least zero and less than one.");
        }
    }
}

/// <summary>Role-wide rules only; player profiles contain no scaling overrides.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MovementScaling
{
    public MovementTierInfluence SpeedTierInfluence { get; set; } = new();
    public MovementTierInfluence AccelerationTierInfluence { get; set; } =
        new() { Jog = .25f, Run = .25f, Sprint = .25f };

    internal void Validate(string path, float jogBaseline, float runBaseline,
        float sprintBaseline, float readyBaseline, float accelerationBaseline)
    {
        if (SpeedTierInfluence is null) throw new ArgumentException($"{path}.SpeedTierInfluence is required.");
        if (AccelerationTierInfluence is null) throw new ArgumentException($"{path}.AccelerationTierInfluence is required.");
        SpeedTierInfluence.Validate($"{path}.SpeedTierInfluence");
        AccelerationTierInfluence.Validate($"{path}.AccelerationTierInfluence");

        // Each result and tier difference is linear on either side of neutral.
        // Endpoints and midpoint therefore cover the entire supported rating range.
        foreach (int rating in new[] { 1, 50, 100 })
        {
            float jog = Scale(jogBaseline, rating, SpeedTierInfluence.Jog);
            float run = Scale(runBaseline, rating, SpeedTierInfluence.Run);
            float sprint = Scale(sprintBaseline, rating, SpeedTierInfluence.Sprint);
            float ready = Scale(readyBaseline, rating, SpeedTierInfluence.Jog);
            if (!float.IsFinite(jog) || !float.IsFinite(run) || !float.IsFinite(sprint) ||
                jog < 0 || !(jog < run && run < sprint) || !float.IsFinite(ready) || ready < 0)
                throw new ArgumentException($"{path}.SpeedTierInfluence must keep finite 0 <= jog < run < sprint and nonnegative ready speed at every Speed rating (failed at {rating}).");
            foreach (var (tier, influence) in new[] {
                ("Jog", AccelerationTierInfluence.Jog), ("Run", AccelerationTierInfluence.Run),
                ("Sprint", AccelerationTierInfluence.Sprint) })
            {
                float acceleration = Scale(accelerationBaseline, rating, influence);
                if (!float.IsFinite(acceleration) || acceleration < 0)
                    throw new ArgumentException($"{path}.AccelerationTierInfluence.{tier} with ForwardAcceleration must produce finite nonnegative acceleration at every Acceleration rating (failed at {rating}).");
            }
        }
    }

    private static float Scale(float baseline, int rating, float influence) =>
        baseline * PlayerMovementAttributes.InfluenceMultiplier(rating, influence);
}

public sealed partial class TackleAlleyConfig
{
    public MovementScaling ReturnerMovementScaling { get; set; } = new()
    {
        SpeedTierInfluence = new() { Jog = .05f, Run = .15f, Sprint = .30f }
    };
    // Preserve the existing defender mapping independently of returner balance.
    public MovementScaling DefenderMovementScaling { get; set; } = new()
    {
        SpeedTierInfluence = new() { Jog = .20f, Run = .20f, Sprint = .20f }
    };

    public void ValidateMovementScaling()
    {
        if (ReturnerMovementScaling is null) throw new ArgumentException("ReturnerMovementScaling is required.");
        if (DefenderMovementScaling is null) throw new ArgumentException("DefenderMovementScaling is required.");
        ReturnerMovementScaling.Validate(nameof(ReturnerMovementScaling), PlayerSlowSpeed,
            PlayerForwardSpeed, PlayerSprintSpeed, PlayerSlowSpeed, ForwardAcceleration);
        DefenderMovementScaling.Validate(nameof(DefenderMovementScaling), OpponentJogSpeed,
            OpponentRunSpeed, OpponentSprintSpeed, PlayerSlowSpeed, ForwardAcceleration);
    }
}
