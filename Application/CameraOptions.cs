using RaylibTackleAlley.Game;

namespace RaylibTackleAlley.Application;

internal static class CameraOptions
{
    public static string Label(CameraMode mode) => mode == CameraMode.ThirdPerson ? "Third Person" : mode.ToString();
    public static CameraMode Parse(string label) => label switch
    {
        "Third Person" => CameraMode.ThirdPerson,
        "Close" => CameraMode.Close,
        "Medium" => CameraMode.Medium,
        "Far" => CameraMode.Far,
        _ => throw new ArgumentException("Unknown camera option.", nameof(label))
    };
}
