namespace RaylibTackleAlley.Game;

public sealed class TackleAlleyConfig
{
    public float PlayerForwardSpeed { get; set; } = 9f;
    public float PlayerLateralSpeed { get; set; } = 8f;
    public float PlayerSlowSpeed { get; set; } = 6.5f;
    public float PlayerSprintSpeed { get; set; } = 11.5f;
    public float PlayerJukeSpeed { get; set; } = 8f;
    public float PlayerJukeDuration { get; set; } = 0.15f;
    public float CameraFollowDistance { get; set; } = 9f;
    public float CameraHeight { get; set; } = 5.5f;
    public float CameraSmoothing { get; set; } = 10f;
    public float FieldWidth { get; set; } = 24f;
    // Gameplay corridor; the visual field and stadium have independent dimensions below.
    public float FieldLength { get; set; } = 84f;
    public float EndZoneLength { get; set; } = 12f;
    public float FieldAssetWidth { get; set; } = 53.333f;
    public float FieldAssetLength { get; set; } = 120f;
    // Include room for the stands beyond the full turf and both end zones.
    public float StadiumAssetWidth { get; set; } = 110f;
    public float StadiumAssetLength { get; set; } = 180f;
    public float OpponentSpeed { get; set; } = 6.5f;
    public float OpponentTriggerDistance { get; set; } = 12f;
    public float TackleDistance { get; set; } = 1.8f;
    public bool DrawGameplayDebug { get; set; }
}
