using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

public sealed partial class Opponent
{
    private void DrawDecisionDebug()
    {
        var debug = DecisionDebug;
        Vector3 origin = _position + Vector3.UnitY * .08f;
        Vector3 Ground(Vector3 target) => new(target.X, origin.Y, target.Z);
        void Range(float radius, Color color) =>
            Raylib.DrawCircle3D(origin, radius, Vector3.UnitX, 90, color);

        Range(BehaviorProfile.BreakdownDistance, Color.Gold);
        Range(BehaviorProfile.BreakdownExitDistance, new Color(150, 125, 55, 255));
        Range(BehaviorProfile.TackleCommitDistance * Physical.HeightRatio, Color.Orange);
        Range(Physical.WrapReach, Color.Green);
        Range(Physical.DiveReach, Color.Purple);
        Raylib.DrawLine3D(origin, Ground(debug.PredictedTarget), Color.SkyBlue);
        Raylib.DrawSphereWires(Ground(debug.PredictedTarget), .12f, 6, 8, Color.SkyBlue);
        Raylib.DrawLine3D(origin, Ground(debug.PursuitTarget), new Color(0, 235, 220, 255));
        Raylib.DrawSphere(Ground(debug.PursuitTarget), .08f, new Color(0, 235, 220, 255));
        Raylib.DrawLine3D(origin, origin + debug.ApproachDirection * 2, Color.White);
        if (debug.SelectedTackleTarget is { } selected)
        {
            Raylib.DrawSphereWires(selected, .13f, 6, 8, Color.Magenta);
            Raylib.DrawLine3D(_position + Vector3.UnitY * Physical.TackleContactHeight, selected, Color.Magenta);
        }
        if (DirectionLocked)
            Raylib.DrawLine3D(_position + Vector3.UnitY * Physical.TackleContactHeight,
                _position + Vector3.UnitY * Physical.TackleContactHeight + debug.LockedDirection * 3, Color.Red);
    }

    public static void DrawTackleAimingLegend()
    {
        Raylib.DrawText("AI: cyan pursuit | blue prediction | white approach | magenta tackle",
            22, 92, 10, Color.White);
        Raylib.DrawText("Ranges: gold breakdown | orange commit | green wrap | purple dive | red locked",
            22, 104, 10, Color.White);
    }

    public void DrawTackleAimingLabel(int y)
    {
        if (!_config.DrawGameplayDebug || !_config.DrawTackleAimingDebug) return;
        var debug = DecisionDebug;
        int width = Math.Max(1, Raylib.GetScreenWidth() - 40);
        Raylib.DrawRectangle(18, y - 2, width + 4, 50, new Color(8, 15, 23, 220));
        string first = $"{Profile.Name} [{debug.Profile}] {debug.State} / {State}: {debug.Reason}";
        string second = $"Wrap {(_wrapReachable ? "YES" : "no")} / Lunge {(_lungeReachable ? "YES" : "no")}  " +
            $"stop {debug.RequiredStoppingDistance:0.0}m  t={CurrentContactSolution.ContactTime:0.00}s  " +
            $"conf={PredictionConfidence:0.00}  {(DirectionLocked ? "LOCKED" : "unlocked")}  TOI={LastSweptContact?.Time.ToString("0.000") ?? "-"}";
        DrawFit(first, y, Color.White);
        DrawFit(second, y + 16, Color.Lime);
        string time = float.IsFinite(debug.EstimatedTimeToContact) ? debug.EstimatedTimeToContact.ToString("0.00") : "-";
        DrawFit($"d={debug.CarrierDistance:0.0}m closing={debug.RelativeClosingSpeed:0.0}m/s TTC={time}s  " +
            $"reach W/L={debug.WrapReach:0.00}/{debug.LungeReach:0.00}m  " +
            $"correct={debug.CorrectionSecondsRemaining:0.00}s/{debug.CorrectionDegreesRemaining:0.0}deg  " +
            $"commit: {debug.CommitmentReason ?? "-"}", y + 32, Color.White);
        void DrawFit(string text, int top, Color color)
        {
            int size = 14;
            while (size > 9 && Raylib.MeasureText(text, size) > width) size--;
            Raylib.DrawText(text, 22, top, size, color);
        }
    }
}
