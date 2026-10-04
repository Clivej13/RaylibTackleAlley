namespace RaylibTackleAlley.Game;

public sealed partial class TackleAlleyConfig
{
    public PlayerProfile BallCarrierProfile { get; set; } = new();
    public PlayerProfile[] BallCarrierProfileExamples { get; set; } = [];
    public bool ShowPlayerProfiles { get; set; }
    public string OffenseUniform { get; set; } = "OffenseUniform";
    public string DefenseUniform { get; set; } = "DefenseUniform";
    public float PlayerForwardSpeed { get; set; } = 6.5f;
    public float PlayerLateralSpeed { get; set; } = 8f;
    public float PlayerSlowSpeed { get; set; } = 4f;
    public float PlayerSprintSpeed { get; set; } = 9f;
    public float PlayerJukeSpeed { get; set; } = 4f;
    public float PlayerJukeDuration { get; set; } = 0.35f;
    // Stick units per mouse pixel per frame; 20 pixels reaches full deflection.
    public float MouseGestureSensitivity { get; set; } = 0.05f;
    // 1 = linear; smaller values soften partial stick deflection, retaining full speed.
    public float LeftStickSensitivity { get; set; } = 1f;
    // Units/s²: adjacent 2.5-unit tiers take 0.30s up and 0.20s down.
    public float ForwardAcceleration { get; set; } = 2.5f / 0.30f;
    public float ForwardDeceleration { get; set; } = 2.5f / 0.20f;
    // Compatibility values for standalone components/legacy callers.
    // Authored games project these from LevelDefinition; config.json no longer owns them.
    public float FieldWidth { get; set; } = 24f;

    internal TackleAlleyConfig ForLevel(LevelDefinition level)
    {
        var copy = (TackleAlleyConfig)MemberwiseClone();
        copy.FieldWidth = level.FieldWidth;
        copy.PlayerSpawn = new() { X = level.PlayerSpawn.X, Z = level.PlayerSpawn.Z };
        copy.OpponentSpawns = level.Defenders.Select(d => new DefenderSpawn
        { X = d.Position.X, Z = d.Position.Z, Profile = d.Profile }).ToArray();
        return copy;
    }
    // Gameplay corridor; the visual field and stadium have independent dimensions below.
    public float FieldLength { get; set; } = 84f;
    public float EndZoneLength { get; set; } = 12f;
    public float FieldAssetWidth { get; set; } = 53.333f;
    public float FieldAssetLength { get; set; } = 120f;
    // Include room for the stands beyond the full turf and both end zones.
    public float StadiumAssetWidth { get; set; } = 110f;
    public float StadiumAssetLength { get; set; } = 180f;
    public float OpponentRunDistance { get; set; } = 20f;
    public float OpponentSprintDistance { get; set; } = 8f;
    // Extra separation required before dropping to a slower pace.
    public float OpponentPaceHysteresis { get; set; } = 0.5f;

    public void ValidateOpponentLocomotion()
    {
        if (!float.IsFinite(OpponentSprintDistance) || OpponentSprintDistance < 0f ||
            !float.IsFinite(OpponentRunDistance) || OpponentSprintDistance >= OpponentRunDistance)
            throw new ArgumentException("OpponentSprintDistance must be nonnegative and smaller than OpponentRunDistance.");
        if (!float.IsFinite(OpponentPaceHysteresis) || OpponentPaceHysteresis < 0f ||
            OpponentPaceHysteresis >= OpponentRunDistance - OpponentSprintDistance)
            throw new ArgumentException("OpponentPaceHysteresis must be nonnegative and smaller than the distance band gap.");
        ValidateMovementScaling();
    }
    public float RagdollDownDuration { get; set; } = 2f;
    public float RagdollDownBlendDuration { get; set; } = .6f;
    public void ValidateRagdollRecovery()
    {
        if (!float.IsFinite(RagdollDownDuration) || !float.IsFinite(RagdollDownBlendDuration) ||
            RagdollDownBlendDuration <= 0 || RagdollDownDuration < RagdollDownBlendDuration)
            throw new ArgumentException("Down duration must be finite and at least the positive recovery blend duration.");
    }
    public float TackleDistance { get; set; } = 1.4f;
    public bool DrawGameplayDebug { get; set; }
}
