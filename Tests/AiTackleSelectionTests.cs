using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class AiTackleSelectionTests
{
    private static Opponent Running(float speed = 6.5f)
    {
        var defender = new Opponent(Vector3.Zero, new()
        {
            OpponentJogSpeed = speed,
            OpponentSprintDistance = 0f
        });
        defender.Update(new(0, 0, -10), 0.01f, false, Vector2.Zero, false);
        Assert.Equal(OpponentPace.Run, defender.Pace);
        return defender;
    }

    [Theory]
    [InlineData(false, 0, "Forward")]
    [InlineData(false, -30, "Left")]
    [InlineData(false, 30, "Right")]
    [InlineData(true, 0, "Forward")]
    [InlineData(true, -30, "Left")]
    [InlineData(true, 30, "Right")]
    public void ReadySelectsWrapOrLungeAndLocksCommit(bool lunge, float degrees, string direction)
    {
        foreach (float yaw in new[] { 0f, 90f, 180f })
        {
            var defender = Running(4f);
            var rotation = Matrix4x4.CreateRotationY(yaw * MathF.PI / 180f);
            defender.Update(defender.Position + Vector3.Transform(new(0, 0, -10), rotation),
                0.01f, false, Vector2.Zero, false);
            defender.Update(defender.Position + Vector3.Transform(new(0, 0, -4), rotation), 0);
            Assert.Equal(DefenderState.TackleReady, defender.State);
            float radians = degrees * MathF.PI / 180f;
            var offset = Vector3.Transform(new Vector3(MathF.Sin(radians), 0, -MathF.Cos(radians)) *
                (lunge ? 1.8f : 0.9f), rotation);
            var start = defender.Position;
            float facing = defender.FacingYawDegrees;
            float launchSpeed = lunge ? Math.Max(4f, defender.CurrentSpeed) : 0f;
            defender.Update(start + offset, 0);
            var state = lunge ? DefenderState.LungeTackle : DefenderState.SetWrap;
            Assert.Equal(state, defender.State);
            Assert.Equal(state + direction, defender.AnimationName);
            defender.Update(start - offset, 0.1f);
            defender.Update(start - offset, 0.1f, false, Vector2.One, true);
            Assert.Equal(state, defender.State);
            Assert.Equal(state + direction, defender.AnimationName);
            Assert.True(Vector3.Distance(start + Vector3.Normalize(offset) * launchSpeed * 0.2f,
                new Vector3(defender.Position.X, start.Y, defender.Position.Z)) < 0.00001f);
            Assert.Equal(facing, defender.FacingYawDegrees);
            Assert.Equal(launchSpeed, defender.MovementSpeed);
            Assert.True(defender.IsTouching(defender.Position + Vector3.UnitX * 1.399f));
            Assert.False(defender.IsTouching(defender.Position + Vector3.UnitX * 1.401f));
            defender.Update(start + new Vector3(0, 0, 30), 0.5f);
            Assert.Equal(lunge ? DefenderState.LungeLand : DefenderState.Locomotion, defender.State);
        }
    }

    [Theory]
    [InlineData(0f, 4f)]
    [InlineData(2f, 4f)]
    [InlineData(6.5f, 6.5f)]
    [InlineData(9f, 9f)]
    public void DiveKeepsLaunchSpeedWithJogMinimum(float approachSpeed, float expectedSpeed)
    {
        var defender = new Opponent(Vector3.Zero, new());
        defender.Update(new(0, 0, -30), 0.01f);
        defender.Update(defender.Position + new Vector3(0, 0, -4), 0f);
        typeof(Opponent).GetProperty(nameof(Opponent.CurrentSpeed))!.SetValue(defender, approachSpeed);
        Vector3 start = defender.Position;
        defender.Update(start + new Vector3(0, 0, -1.8f), 0f);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
        Assert.Equal(expectedSpeed, defender.CurrentSpeed);
        for (int i = 0; i < 3; i++)
            defender.Update(start + new Vector3(10, 0, 10), 0.1f, false, Vector2.Zero, false);
        Assert.Equal(start.Z - expectedSpeed * 0.3f, defender.Position.Z, 4);
        Assert.Equal(start.X, defender.Position.X);
        Assert.True(defender.Position.Y > start.Y);
        Assert.False(defender.IsGrounded);
        Assert.Equal(expectedSpeed, defender.CurrentSpeed);
        defender.Update(start + new Vector3(10, 0, 10), 0.3f, false, Vector2.Zero, false);
        Assert.Equal(start.Z - expectedSpeed * 0.6f, defender.Position.Z, 4);
        defender.Reset();
        Assert.Equal(Vector3.Zero, defender.Position);
        Assert.Equal(4f, defender.CurrentSpeed);
    }

    [Fact]
    public void RunLungesWhenThereIsNotEnoughBrakingSpace()
    {
        var defender = Running();
        defender.Update(defender.Position + new Vector3(0, 0, -1.8f), 0);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
        Assert.Equal("LungeTackleForward", defender.AnimationName);
    }

    [Fact]
    public void RunBreaksDownWhenSpaceIsAvailable()
    {
        var defender = Running();
        defender.Update(defender.Position + new Vector3(0, 0, -4f), 0);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        Assert.Equal(6.5f, defender.CurrentSpeed);
    }

    [Theory]
    [InlineData(2.26f, 0f)]
    [InlineData(1.8f, 46f)]
    [InlineData(1.8f, 180f)]
    public void ReadyKeepsClosingOutsideLaunchReach(float distance, float degrees)
    {
        var defender = Running(4f);
        defender.Update(defender.Position + new Vector3(0, 0, -4), 0);
        float radians = degrees * MathF.PI / 180f;
        defender.Update(defender.Position + new Vector3(MathF.Sin(radians), 0, -MathF.Cos(radians)) * distance, 0);
        Assert.Equal(DefenderState.TackleReady, defender.State);
    }

    [Fact]
    public void ReadyDoesNotWrapBeforeSpeedIsControlled()
    {
        var defender = Running();
        defender.Update(defender.Position + new Vector3(0, 0, -4), 0);
        defender.Update(defender.Position + new Vector3(0, 0, -0.9f), 0);
        Assert.Equal(DefenderState.TackleReady, defender.State);
    }
}
