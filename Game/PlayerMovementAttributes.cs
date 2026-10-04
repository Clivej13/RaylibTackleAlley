using System.Numerics;

namespace RaylibTackleAlley.Game;

/// <summary>Global balance modified once by a player's ratings. Distances are metres and time seconds.</summary>
public sealed class PlayerMovementAttributes
{
    public float JogSpeedMultiplier { get; }
    public float RunningSpeedMultiplier { get; }
    public float SprintSpeedMultiplier { get; }
    public float JogAccelerationMultiplier { get; }
    // Compatibility accessor: the normal run tier.
    public float AccelerationMultiplier { get; }
    public float SprintAccelerationMultiplier { get; }
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
    public float JogAccelerationRate { get; }
    public float AccelerationRate { get; }
    public float SprintAccelerationRate { get; }
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

    public PlayerMovementAttributes(PlayerProfile profile, TackleAlleyConfig config)
    {
        profile.Validate();
        config.ValidateMovementScaling();
        var scaling = config.MovementScaling;
        JogSpeedMultiplier = InfluenceMultiplier(profile.Speed, scaling.SpeedTierInfluence.Jog);
        RunningSpeedMultiplier = InfluenceMultiplier(profile.Speed, scaling.SpeedTierInfluence.Run);
        SprintSpeedMultiplier = InfluenceMultiplier(profile.Speed, scaling.SpeedTierInfluence.Sprint);
        JogAccelerationMultiplier = InfluenceMultiplier(profile.Acceleration, scaling.AccelerationTierInfluence.Jog);
        AccelerationMultiplier = InfluenceMultiplier(profile.Acceleration, scaling.AccelerationTierInfluence.Run);
        SprintAccelerationMultiplier = InfluenceMultiplier(profile.Acceleration, scaling.AccelerationTierInfluence.Sprint);
        SteeringMultiplier = RatingMultiplier(profile.Agility, .85f, 1.15f);
        ReversalMultiplier = RatingMultiplier(profile.Agility, 1.15f, .85f);
        EvadeMultiplier = RatingMultiplier(profile.Agility, .9f, 1.1f);
        JukeMultiplier = RatingMultiplier(profile.Juke, .85f, 1.15f);
        BaselineJogSpeed = config.PlayerSlowSpeed;
        BaselineRunSpeed = config.PlayerForwardSpeed;
        BaselineSprintSpeed = config.PlayerSprintSpeed;
        BaselineReadySpeed = BaselineJogSpeed;
        JogSpeed = BaselineJogSpeed * scaling.SpeedScale * JogSpeedMultiplier;
        RunningSpeed = BaselineRunSpeed * scaling.SpeedScale * RunningSpeedMultiplier;
        SprintSpeed = BaselineSprintSpeed * scaling.SpeedScale * SprintSpeedMultiplier;
        ReadySpeed = BaselineReadySpeed * scaling.SpeedScale * JogSpeedMultiplier;
        // Speed never boosts the acceleration ramp: a larger sprint-speed gap takes
        // longer to cover. The separate Acceleration rating still owns this rate.
        JogAccelerationRate = config.ForwardAcceleration * scaling.AccelerationScale * JogAccelerationMultiplier;
        AccelerationRate = config.ForwardAcceleration * scaling.AccelerationScale * AccelerationMultiplier;
        SprintAccelerationRate = config.ForwardAcceleration * scaling.AccelerationScale * SprintAccelerationMultiplier;
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

    public static float InfluenceMultiplier(int rating, float influence)
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

    // Acceleration follows the requested tier, including recovery from rest or a penalty.
    public float TierAcceleration(int tier) => tier switch { 1 => JogAccelerationRate, 3 => SprintAccelerationRate, _ => AccelerationRate };

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
