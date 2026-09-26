using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class LungeInterceptionTests
{
    [Theory]
    [InlineData(-1f, 1f / 30)]
    [InlineData(1f, 1f / 60)]
    [InlineData(-1f, 1f / 120)]
    public void SuddenCloseRangeEvadeDoesNotTriggerFreshDive(float side, float dt)
    {
        var defender = new Opponent(Vector3.Zero, new() { OpponentInitialYawDegrees = 0 });
        defender.Update(new(0, 0, -7), .6f, false, Vector2.Zero, false);
        defender.Update(defender.Position + new Vector3(0, 0, -10), dt, null, new Vector3(0, 0, 9));
        var carrier = defender.Position + new Vector3(0, 0, -1.5f);
        defender.Update(carrier, dt, null, new Vector3(side * 4, 0, 0));
        Assert.NotEqual(DefenderState.LungeTackle, defender.State);
        Assert.Equal(0, defender.VerticalVelocity);
        Assert.True(defender.HasEngaged);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void UnreachableSidewaysBurstDoesNotTriggerDive(float side)
    {
        var defender = new Opponent(Vector3.Zero, new() { OpponentInitialYawDegrees = 0 });
        defender.Update(new(0, 0, -4), 0, true, Vector2.Zero, false);
        defender.Update(new(0, 0, -2), .01f, new Vector3(side * 3, 0, -2), new Vector3(side * 10, 0, 0));
        Assert.Equal(DefenderState.TackleReady, defender.State);
        Assert.Equal(0, defender.VerticalVelocity);
        Assert.Equal(Math.Sign(side), Math.Sign(defender.Position.X));
        Assert.True(defender.Position.Z < 0);
    }

    [Fact]
    public void StableMovementAllowsTacklingAgainAfterAnEvade()
    {
        var defender = new Opponent(Vector3.Zero, new() { OpponentInitialYawDegrees = 0 });
        defender.Update(new(0, 0, -7), .6f, false, Vector2.Zero, false);
        defender.Update(defender.Position + new Vector3(0, 0, -10), .01f, null, new Vector3(0, 0, 9));
        defender.Update(defender.Position + new Vector3(0, 0, -1.5f), .01f, null, new Vector3(4, 0, 0));
        Assert.Equal(DefenderState.TackleReady, defender.State);
        // Keep observing the new path at a safe distance, then test a reachable
        // crossing opportunity after the initial surprise has passed.
        for (int i = 0; i < 60; i++)
            defender.Update(defender.Position + new Vector3(0, 0, -10), 1f / 60, null, new Vector3(4, 0, 0));
        defender.Update(defender.Position + new Vector3(0, 0, -1.5f), .01f, null, new Vector3(4, 0, 0));
        Assert.Equal(DefenderState.LungeTackle, defender.State);
    }

    [Fact]
    public void ContactMustBeReachableWithinTheAvailableWindow()
    {
        Assert.False(LungeInterception.TryTarget(Vector3.Zero, new(0, 0, -4),
            Vector3.Zero, 4, .6f, out _));
        Assert.True(LungeInterception.TryTarget(Vector3.Zero, new(0, 0, -2),
            Vector3.Zero, 4, .6f, out _));
        Assert.False(LungeInterception.TryTarget(Vector3.Zero, new(0, 0, -2),
            Vector3.Zero, 4, .2f, out _));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void CrossingRunnerGetsLeadAtTheReachableMeetingPoint(float side)
    {
        Vector3 defender = new(side * 3, 0, 0), carrier = Vector3.Zero, velocity = new(0, 0, -6);
        Vector3 target = LungeInterception.Target(defender, carrier, velocity, 9);
        float time = -target.Z / 6;
        Assert.InRange(time, .1f, new TackleAlleyConfig().LungeMaximumPredictionSeconds);
        Assert.True(Vector3.Distance(target, carrier + velocity * time) < .0001f);
        Assert.Equal(9 * time, Vector3.Distance(defender, target), 4);
        Assert.True(Vector3.Distance(target, carrier) > 1);
    }

    [Fact]
    public void EqualSpeedHeadOnApproachUsesPositiveLinearSolution()
    {
        Vector3 target = LungeInterception.Target(new(0, 0, -4), Vector3.Zero, new(0, 0, -9), 9);
        Assert.Equal(new Vector3(0, 0, -2), target);
    }

    [Fact]
    public void FasterApproachingRunnerUsesEarliestIntersection()
    {
        Vector3 target = LungeInterception.Target(new(0, 0, -3), Vector3.Zero, new(0, 0, -10), 5);
        Assert.True(Vector3.Distance(new(0, 0, -2), target) < .0001f);
    }

    [Fact]
    public void StationaryAndVerticalOnlyMotionDoNotAddHorizontalLead()
    {
        Vector3 carrier = new(2, 3, 4);
        Assert.Equal(carrier, LungeInterception.Target(Vector3.Zero, carrier, Vector3.Zero, 9));
        Assert.Equal(carrier, LungeInterception.Target(Vector3.Zero, carrier, Vector3.UnitY * 10, 9));
        Assert.Equal(carrier, LungeInterception.Target(Vector3.Zero, carrier, Vector3.One, 0));
    }

    [Theory]
    [InlineData(0f, -20f)]
    [InlineData(20f, 0f)]
    public void UnreachableRunnerGetsFiniteBoundedLead(float x, float z)
    {
        Vector3 carrier = new(0, 0, -4), velocity = new(x, 0, z);
        Vector3 target = LungeInterception.Target(Vector3.Zero, carrier, velocity, 4);
        Assert.True(float.IsFinite(target.LengthSquared()));
        Assert.InRange(Vector3.Distance(target, carrier), 0, velocity.Length() * new TackleAlleyConfig().LungeMaximumPredictionSeconds + .0001f);
        Assert.True(Vector3.Dot(target - carrier, velocity) > 0);
    }

    [Fact]
    public void CurrentObservedDirectionChangesTheAim()
    {
        Vector3 defender = new(0, 0, -3);
        Vector3 left = LungeInterception.Target(defender, Vector3.Zero, new(-5, 0, 0), 9);
        Vector3 right = LungeInterception.Target(defender, Vector3.Zero, new(5, 0, 0), 9);
        Assert.True(left.X < 0 && right.X > 0);
        Assert.Equal(-left.X, right.X, 4);
    }

    [Theory]
    [InlineData(-20f, 0f)]
    [InlineData(20f, 0f)]
    [InlineData(0f, 8.9f)]
    public void ClosingDistanceShortensLeadEvenAfterFastMovement(float x, float z)
    {
        Vector3 velocity = new(x, 0, z);
        float previousLead = float.PositiveInfinity;
        foreach (float distance in new[] { 3f, 2f, 1f, .5f, .1f })
        {
            Vector3 target = LungeInterception.Target(new(0, 0, -distance),
                Vector3.Zero, velocity, 9);
            float lead = target.Length();
            Assert.True(lead < previousLead, $"Lead {lead} did not shrink at distance {distance}");
            Assert.InRange(lead, 0, distance + .0001f);
            if (distance <= 1) Assert.InRange(lead, 0, distance * .34f);
            Assert.True(Vector3.Dot(target, velocity) > 0);
            previousLead = lead;
        }
    }

    [Fact]
    public void CloseRangeQuickReversalKeepsBothTargetsNearTheCarrier()
    {
        Vector3 defender = new(0, 10, -1), carrier = new(0, 2, 0);
        Vector3 left = LungeInterception.Target(defender, carrier, new(-20, 0, 0), 9);
        Vector3 right = LungeInterception.Target(defender, carrier, new(20, 0, 0), 9);
        Assert.InRange(left.X, -.34f, -.01f);
        Assert.InRange(right.X, .01f, .34f);
        Assert.Equal(-left.X, right.X, 4);
        Assert.Equal(carrier.Y, left.Y);
        Assert.Equal(carrier.Y, right.Y);
    }

    [Fact]
    public void UnreachableEarlyReversalDoesNotRedirectCommittedDive()
    {
        var defender = new Opponent(Vector3.Zero, new());
        defender.Update(new(0, 0, -20), .01f, false, Vector2.Zero, false);
        defender.Update(defender.Position + new Vector3(0, 0, -4), 0, true, Vector2.Zero, false);
        Vector3 carrier = defender.Position + new Vector3(1, 0, -2);
        Vector3 velocity = new(0, 0, 2f); // Approaching and reachable before the dive ends.
        defender.Update(carrier, 0, null, velocity);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
        Vector3 launched = defender.Velocity; launched.Y = 0;
        Assert.True(defender.InitialContactSolution.Reachable);
        Assert.True(Vector3.Distance(defender.InitialContactSolution.LaunchDirection, Vector3.Normalize(launched)) < .0001f);
        Vector3 start = defender.Position;
        defender.Update(carrier + Vector3.UnitX * 10, .1f, null, Vector3.UnitX * 8);
        Vector3 after = defender.Velocity; after.Y = 0;
        Assert.Equal(launched, after);
        Assert.Equal(start.X + launched.X * .1f, defender.Position.X, 4);
        Assert.Equal(start.Z + launched.Z * .1f, defender.Position.Z, 4);
    }
}
