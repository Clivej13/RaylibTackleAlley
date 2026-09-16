using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

/// <summary>Temporary opt-in manual harness. Remove with tackle-driven activation.</summary>
public static class RagdollDebugControls
{
    public static bool Enabled { get; } = Environment.GetCommandLineArgs().Contains("--ragdoll-debug");
    public static bool ShowBodies { get; } = Enabled && Environment.GetCommandLineArgs().Contains("--ragdoll-bodies");
    public const float ForwardImpulse = 180f; // N s, along defender's facing
    public const float UpwardImpulse = 120f;  // N s
    public static Opponent? Nearest(IEnumerable<Opponent> defenders, Vector3 carrier) =>
        defenders.Where(d => !d.Ragdoll.IsActive && !d.IsRecovering)
            .MinBy(d => Vector3.DistanceSquared(d.Position, carrier));

    public static Vector3 TestImpulse(float yawDegrees) =>
        Vector3.Transform(-Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f)) *
        ForwardImpulse + Vector3.UnitY * UpwardImpulse;

    public static void Update(IEnumerable<Opponent> defenders, Vector3 carrier)
    {
        // IsKeyPressed is an edge, not a held/repeating key query.
        if (!Enabled || !Raylib.IsKeyPressed(KeyboardKey.R)) return;
        var defender = Nearest(defenders, carrier);
        if (defender is null) return;
        bool shifted = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        defender.ActivateRagdoll(shifted
            ? new RagdollImpulse(1, TestImpulse(defender.FacingYawDegrees)) : null);
    }

    public static void Draw(IEnumerable<Opponent> defenders)
    {
        if (!Enabled) return;
        string states = string.Join(", ", defenders.Select((d, i) => $"{i + 1}: {d.Ragdoll.State}"));
        Raylib.DrawText("R: nearest defender ragdoll | Shift+R: test impulse", 18, 100, 18, Color.Orange);
        Raylib.DrawText(states, 18, 122, 16, Color.White);
    }
}
