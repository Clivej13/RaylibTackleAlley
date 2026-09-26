using System.Numerics;

namespace RaylibTackleAlley.Game;

/// <summary>Global balance modified once by a player's ratings. Distances are metres and time seconds.</summary>
public sealed class PlayerMovementAttributes
{
    public float JogSpeedMultiplier { get; }
    public float RunningSpeedMultiplier { get; }
    public float SprintSpeedMultiplier { get; }
    public float AccelerationMultiplier { get; }
    public float SteeringMultiplier { get; }
    public float ReversalMultiplier { get; }
    public float EvadeMultiplier { get; }
    public float JukeMultiplier { get; }
    public float BaselineJogSpeed { get; }
    public float BaselineRunSpeed { get; }
    public float BaselineSprintSpeed { get; }
    public float BaselineReadySpeed { get; }
    public float JogSpeed { get; }
    public float RunningSpeed { get; }
    public float SprintSpeed { get; }
    public float ReadySpeed { get; }
    public float AccelerationRate { get; }
    public float LateralSpeed { get; }
    public float SteeringResponse { get; }
    public float FacingResponse { get; }
    public float ReadyFacingResponse { get; }
    public float TrackingResponse { get; }
    public float ReversalPenalty { get; }
    public float ReversalDelay { get; }
    public float ReversalAdditionalDelay { get; }
    public float ReversalMaximumDelay { get; }
    public float JukeSpeed { get; }
    public float SpinSpeed { get; }

    public PlayerMovementAttributes(PlayerProfile profile, TackleAlleyConfig config, bool defender = false)
    {
        profile.Validate();
        if (!defender) config.ValidateReturnerSpeedScaling();
        float defenderSpeedMultiplier = RatingMultiplier(profile.Speed, .8f, 1.2f);
        JogSpeedMultiplier = defender ? defenderSpeedMultiplier :
            ReturnerSpeedMultiplier(profile.Speed, config.ReturnerJogSpeedInfluence);
        RunningSpeedMultiplier = defender ? defenderSpeedMultiplier :
            ReturnerSpeedMultiplier(profile.Speed, config.ReturnerRunSpeedInfluence);
        SprintSpeedMultiplier = defender ? defenderSpeedMultiplier :
            ReturnerSpeedMultiplier(profile.Speed, config.ReturnerSprintSpeedInfluence);
        AccelerationMultiplier = RatingMultiplier(profile.Acceleration, .75f, 1.25f);
        SteeringMultiplier = RatingMultiplier(profile.Agility, .85f, 1.15f);
        ReversalMultiplier = RatingMultiplier(profile.Agility, 1.15f, .85f);
        EvadeMultiplier = RatingMultiplier(profile.Agility, .9f, 1.1f);
        JukeMultiplier = RatingMultiplier(profile.Juke, .85f, 1.15f);
        BaselineJogSpeed = defender ? config.OpponentJogSpeed : config.PlayerSlowSpeed;
        BaselineRunSpeed = defender ? config.OpponentRunSpeed : config.PlayerForwardSpeed;
        BaselineSprintSpeed = defender ? config.OpponentSprintSpeed : config.PlayerSprintSpeed;
        BaselineReadySpeed = config.PlayerSlowSpeed;
        JogSpeed = BaselineJogSpeed * JogSpeedMultiplier;
        RunningSpeed = BaselineRunSpeed * RunningSpeedMultiplier;
        SprintSpeed = BaselineSprintSpeed * SprintSpeedMultiplier;
        ReadySpeed = BaselineReadySpeed * JogSpeedMultiplier;
        // Speed never boosts the acceleration ramp: a larger sprint-speed gap takes
        // longer to cover. The separate Acceleration rating still owns this rate.
        AccelerationRate = config.ForwardAcceleration * AccelerationMultiplier;
        LateralSpeed = config.PlayerLateralSpeed * SteeringMultiplier;
        SteeringResponse = config.PlayerSprintSteeringRate * SteeringMultiplier;
        FacingResponse = config.PlayerRunYawResponse * SteeringMultiplier;
        ReadyFacingResponse = config.OpponentReadyTurnDegreesPerSecond * SteeringMultiplier;
        TrackingResponse = config.PursuitDirectionResponse * SteeringMultiplier;
        ReversalPenalty = Math.Clamp(config.PlayerReversalSpeedLoss * ReversalMultiplier, 0, 1);
        ReversalDelay = config.PlayerReversalAccelerationDelay * ReversalMultiplier;
        ReversalAdditionalDelay = config.PlayerReversalAdditionalDelay * ReversalMultiplier;
        ReversalMaximumDelay = config.PlayerReversalMaximumDelay * ReversalMultiplier;
        // Agility supplies general evasion; Juke adds its own displacement factor.
        // Both retain the globally authored action duration and animation timing.
        JukeSpeed = config.PlayerJukeSpeed * EvadeMultiplier * JukeMultiplier;
        SpinSpeed = config.PlayerSpinSpeed * EvadeMultiplier;
    }

    public static float ReturnerSpeedMultiplier(int rating, float influence)
    {
        if (rating is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(rating));
        if (!float.IsFinite(influence) || influence < 0 || influence >= 1)
            throw new ArgumentOutOfRangeException(nameof(influence));
        float offset = (rating - 50) / (rating <= 50 ? 49f : 50f);
        return 1 + offset * influence;
    }

    // Piecewise interpolation keeps the midpoint exactly 1 despite asymmetric 1..100 intervals.
    public static float RatingMultiplier(int rating, float atOne, float atHundred)
    {
        if (rating is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be an integer from 1 to 100.");
        return rating <= 50 ? atOne + (1f - atOne) * ((rating - 1) / 49f)
            : 1f + (atHundred - 1f) * ((rating - 50) / 50f);
    }

    public float BaselineSpeed(int tier) => tier switch { 1 => BaselineJogSpeed, 3 => BaselineSprintSpeed, _ => BaselineRunSpeed };
    public float TierSpeed(int tier) => tier switch { 1 => JogSpeed, 3 => SprintSpeed, _ => RunningSpeed };
    public static float PlaybackRate(float speed, float baseline) =>
        baseline > 0 && float.IsFinite(speed) ? Math.Clamp(Math.Abs(speed) / baseline, .35f, 1.5f) : .35f;

    // Baseline normal steering/pursuit already snaps to its requested direction.
    // Lower agility adds a short lag; at/above 50 retain that responsiveness ceiling.
    public float DirectionBlend(float dt) => dt <= 0 ? 1 :
        1f - MathF.Pow(1f - Math.Min(SteeringMultiplier, 1f), dt * 60f);

    public Vector3 TrackDirection(Vector3 current, Vector3 desired, float dt)
    {
        if (SteeringMultiplier >= 1f || current.LengthSquared() < .000001f) return desired;
        float from = MathF.Atan2(current.X, current.Z);
        float to = MathF.Atan2(desired.X, desired.Z);
        float yaw = from + MathF.IEEERemainder(to - from, MathF.Tau) * DirectionBlend(dt);
        return new(MathF.Sin(yaw), 0, MathF.Cos(yaw));
    }
}
