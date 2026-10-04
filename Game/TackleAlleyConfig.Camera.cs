using System.Text.Json.Serialization;

namespace RaylibTackleAlley.Game;

[JsonConverter(typeof(JsonStringEnumConverter<CameraMode>))]
public enum CameraMode { ThirdPerson, Close, Medium, Far }

public sealed class FixedCameraPreset
{
    public float Distance { get; set; } = 6.75f;
    public float Height { get; set; } = 5.5f;
    public float PitchDegrees { get; set; } = 20;
    public float LookAhead { get; set; } = 2;
    public float FollowSmoothing { get; set; } = 10;
    public float FovY { get; set; } = 55;
}

public sealed class ThirdPersonCameraTuning
{
    public float Distance { get; set; } = 3.2f;
    public float SprintDistance { get; set; } = 2.8f;
    public float ZoomSmoothing { get; set; } = 10;
    public float TargetHeight { get; set; } = 1.55f;
    public float ShoulderOffset { get; set; } = 0.65f;
    public float ShoulderAngleDegrees { get; set; }
    public static bool ValidShoulderAngle(float value) => float.IsFinite(value) && value >= 0 && value <= 180;
    public float InitialYawDegrees { get; set; }
    public float InitialPitchDegrees { get; set; } = 20;
    public float MinPitchDegrees { get; set; } = -10;
    public float MaxPitchDegrees { get; set; } = 75;
    public float YawSpeedDegrees { get; set; } = 110;
    public float PitchSpeedDegrees { get; set; } = 75;
    public float FollowSmoothing { get; set; } = 24;
    public float LookSmoothing { get; set; } = 12;
    public float FovY { get; set; } = 55;
}

public sealed partial class TackleAlleyConfig
{
    public CameraMode DefaultCameraMode { get; set; } = CameraMode.Medium;
    public ThirdPersonCameraTuning ThirdPersonCamera { get; set; } = new();
    public FixedCameraPreset CloseCamera { get; set; } = new()
        { Distance = 4.5f, Height = 3.5f, PitchDegrees = 18, LookAhead = 1 };
    public FixedCameraPreset MediumCamera { get; set; } = new();
    public FixedCameraPreset FarCamera { get; set; } = new()
        { Distance = 10, Height = 8, PitchDegrees = 25, LookAhead = 3 };

    public void ValidateCamera()
    {
        if (!Enum.IsDefined(DefaultCameraMode)) throw new ArgumentException("DefaultCameraMode is invalid.");
        if (ThirdPersonCamera is null) throw new ArgumentException("ThirdPersonCamera is required.");
        ValidatePreset(CloseCamera, nameof(CloseCamera));
        ValidatePreset(MediumCamera, nameof(MediumCamera));
        ValidatePreset(FarCamera, nameof(FarCamera));
        var third = ThirdPersonCamera;
        Positive(third.Distance, "ThirdPersonCamera.Distance");
        Positive(third.SprintDistance, "ThirdPersonCamera.SprintDistance");
        Positive(third.ZoomSmoothing, "ThirdPersonCamera.ZoomSmoothing");
        if (third.SprintDistance > third.Distance)
            throw new ArgumentException("ThirdPersonCamera.SprintDistance must not exceed Distance.");
        Nonnegative(third.TargetHeight, "ThirdPersonCamera.TargetHeight");
        Finite(third.ShoulderOffset, "ThirdPersonCamera.ShoulderOffset");
        if (!ThirdPersonCameraTuning.ValidShoulderAngle(third.ShoulderAngleDegrees))
            throw new ArgumentOutOfRangeException("ThirdPersonCamera.ShoulderAngleDegrees");
        Finite(third.InitialYawDegrees, "ThirdPersonCamera.InitialYawDegrees");
        Pitch(third.MinPitchDegrees, "ThirdPersonCamera.MinPitchDegrees");
        Pitch(third.MaxPitchDegrees, "ThirdPersonCamera.MaxPitchDegrees");
        Pitch(third.InitialPitchDegrees, "ThirdPersonCamera.InitialPitchDegrees");
        if (third.MinPitchDegrees > third.InitialPitchDegrees || third.InitialPitchDegrees > third.MaxPitchDegrees)
            throw new ArgumentException("ThirdPersonCamera pitch limits must contain the initial pitch.");
        Positive(third.YawSpeedDegrees, "ThirdPersonCamera.YawSpeedDegrees");
        Positive(third.PitchSpeedDegrees, "ThirdPersonCamera.PitchSpeedDegrees");
        Positive(third.FollowSmoothing, "ThirdPersonCamera.FollowSmoothing");
        Positive(third.LookSmoothing, "ThirdPersonCamera.LookSmoothing");
        Fov(third.FovY, "ThirdPersonCamera.FovY");

        static void Pitch(float value, string name)
        {
            Finite(value, name);
            if (value <= -89 || value >= 89) throw new ArgumentOutOfRangeException(name, "Pitch must be between -89 and 89 degrees.");
        }
        static void Fov(float value, string name)
        {
            Positive(value, name);
            if (value >= 180) throw new ArgumentOutOfRangeException(name, "FOV must be below 180.");
        }
        static void ValidatePreset(FixedCameraPreset? preset, string name)
        {
            if (preset is null) throw new ArgumentException($"{name} is required.");
            Positive(preset.Distance, $"{name}.Distance");
            Nonnegative(preset.Height, $"{name}.Height");
            Nonnegative(preset.LookAhead, $"{name}.LookAhead");
            Positive(preset.FollowSmoothing, $"{name}.FollowSmoothing");
            Pitch(preset.PitchDegrees, $"{name}.PitchDegrees");
            Fov(preset.FovY, $"{name}.FovY");
        }
    }
}
