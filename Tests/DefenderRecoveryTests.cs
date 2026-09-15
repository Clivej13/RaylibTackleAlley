using System.Numerics;
using System.Reflection;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class DefenderRecoveryTests
{
    private static Opponent Lunge()
    {
        var defender = new Opponent(Vector3.Zero, new());
        defender.Update(new(0, 0, -30), 0.01f);
        defender.Update(defender.Position + new Vector3(0, 0, -4), 0f);
        defender.Update(defender.Position + new Vector3(0, 0, -1.8f), 0f);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
        return defender;
    }

    private static void Advance(Opponent defender, float dt) =>
        defender.Update(new(30, 0, 30), dt, true, Vector2.One, true);

    [Fact]
    public void FallLandsExactlyAndRecoveryIgnoresAllMovementIntent()
    {
        var defender = Lunge();
        float yaw = defender.FacingYawDegrees;
        Vector3 start = defender.Position;
        Advance(defender, 0.6f);
        Assert.Equal(DefenderState.LungeLand, defender.State);
        Assert.True(defender.Position.Y > 0f);
        Assert.True(defender.VerticalVelocity < 0f);
        Assert.Equal(start.X, defender.Position.X);
        Advance(defender, 0.3f);
        Assert.True(defender.IsGrounded);
        Assert.Equal(0f, defender.Position.Y);
        Assert.Equal(0f, defender.VerticalVelocity);
        Assert.Equal(0f, defender.CurrentSpeed);
        Vector3 landed = defender.Position;
        Advance(defender, 0.3f);
        Assert.Equal(DefenderState.Down, defender.State);
        Advance(defender, Opponent.DefenderDownDurationSeconds - 0.01f);
        Assert.Equal(DefenderState.Down, defender.State);
        Assert.Equal(landed, defender.Position);
        Advance(defender, 0.01f);
        Assert.Equal(DefenderState.GetUp, defender.State);
        Advance(defender, 1f);
        Assert.Equal(DefenderState.GetUp, defender.State);
        Assert.Equal(landed, defender.Position);
        Assert.Equal(yaw, defender.FacingYawDegrees);
        Advance(defender, 1f);
        Assert.Equal(DefenderState.Locomotion, defender.State);
        Assert.Equal(landed, defender.Position);
        defender.Update(new(30, 0, 30), 0.1f, false, Vector2.Zero, false);
        Assert.NotEqual(landed, defender.Position);
    }

    [Theory]
    [InlineData(0.1f, DefenderState.LungeTackle)]
    [InlineData(0.7f, DefenderState.LungeLand)]
    [InlineData(1.3f, DefenderState.Down)]
    [InlineData(3.3f, DefenderState.GetUp)]
    public void ResetClearsEveryRecoveryStage(float time, DefenderState state)
    {
        var defender = Lunge();
        Advance(defender, time);
        Assert.Equal(state, defender.State);
        defender.Reset();
        Assert.Equal(DefenderState.Locomotion, defender.State);
        Assert.Equal("Jog", defender.AnimationName);
        Assert.Equal(Vector3.Zero, defender.Position);
        Assert.True(defender.IsGrounded);
        Assert.Equal(0f, defender.VerticalVelocity);
        Assert.Equal(4f, defender.CurrentSpeed);
        Assert.Equal(0f, (float)typeof(Opponent).GetField("_tackleRemaining",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(defender)!);
        Assert.Equal(Vector3.Zero, (Vector3)typeof(Opponent).GetField("_movementDirection",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(defender)!);
    }

    [Fact]
    public void LargeDeltaMatchesSmallStepsAtGroundAndCannotStick()
    {
        var large = Lunge();
        var small = Lunge();
        Advance(large, 1.5f);
        for (int i = 0; i < 150; i++) Advance(small, 0.01f);
        Assert.Equal(DefenderState.Down, large.State);
        Assert.Equal(large.State, small.State);
        Assert.True(Vector3.Distance(large.Position, small.Position) < 0.0001f);
        Advance(large, 100f);
        Assert.Equal(DefenderState.Locomotion, large.State);
        Assert.True(large.IsGrounded);
        Assert.Equal(0f, large.Position.Y);
        Assert.Equal(0f, large.VerticalVelocity);
    }

    [Fact]
    public void LandingClipWaitsForGroundEvenAfterAnimationCompletes()
    {
        var defender = Lunge();
        typeof(Opponent).GetProperty(nameof(Opponent.VerticalVelocity))!.SetValue(defender, 10f);
        Advance(defender, 1.3f);
        Assert.Equal(DefenderState.LungeLand, defender.State);
        Assert.False(defender.IsGrounded);
        Advance(defender, 0.8f);
        Assert.True(defender.IsGrounded);
        Assert.Equal(DefenderState.Down, defender.State);
    }

    [Fact]
    public void SetWrapStaysGroundedAndBypassesRecovery()
    {
        var defender = new Opponent(Vector3.Zero, new());
        defender.Update(new(0, 0, 1), 0f, true, Vector2.Zero, true);
        Advance(defender, 0.2f);
        Assert.Equal(DefenderState.SetWrap, defender.State);
        Assert.True(defender.IsGrounded);
        Assert.Equal(Vector3.Zero, defender.Position);
        Advance(defender, 0.2f);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        Assert.True(defender.IsGrounded);
    }
}
