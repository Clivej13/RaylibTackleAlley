using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class DefenderReadyConeTests
{
    [Theory]
    [InlineData(-30, true)]
    [InlineData(30, true)]
    [InlineData(-30.1f, false)]
    [InlineData(30.1f, false)]
    [InlineData(90, false)]
    [InlineData(180, false)]
    public void ReadyRequiresDefenderInCarrierForwardCone(float angle, bool ready)
    {
        foreach (float yaw in new[] { 0f, 90f, 180f })
        {
            var rotation = Matrix4x4.CreateRotationY(yaw * MathF.PI / 180f);
            var forward = Vector3.Transform(-Vector3.UnitZ, rotation);
            float radians = angle * MathF.PI / 180f;
            var position = Vector3.Transform(new Vector3(MathF.Sin(radians), 0, -MathF.Cos(radians)) * 4, rotation);
            var defender = new Opponent(position, new());
            defender.Update(Vector3.Zero, 0, carrierPredictedDirection: forward);
            Assert.Equal(ready ? DefenderState.TackleReady : DefenderState.Locomotion, defender.State);
        }
    }

    [Fact]
    public void PassedDefenderLeavesReadyAndKeepsAcceleratingDespiteChangingCarrierVelocity()
    {
        var defender = new Opponent(new(0, 0, -4), new());
        defender.Update(Vector3.Zero, 0, carrierVelocity: new(0, 0, -9), carrierPredictedDirection: -Vector3.UnitZ);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        // Carrier has passed the defender. Prediction deliberately points sideways.
        for (int i = 0; i < 30; i++)
        {
            var target = defender.Position - Vector3.UnitZ * 3;
            defender.Update(target, 1f / 60, target + Vector3.UnitX * 5,
                new Vector3(i % 2 == 0 ? 8 : -8, 0, -9), -Vector3.UnitZ);
            Assert.Equal(DefenderState.Locomotion, defender.State);
            Assert.Equal("Sprint", defender.AnimationName);
            Assert.Equal(0, defender.Position.X);
        }
        Assert.True(defender.CurrentSpeed > defender.TackleReadySpeed);
        Assert.Equal(9, defender.TargetSpeed);
    }

    [Fact]
    public void ConeFollowsPredictionPointAndDropsReadyWhenPredictionStops()
    {
        var prediction = new CarrierPursuitPrediction();
        prediction.Reset(new(-1, 0, 0));
        var carrier = Vector3.Zero;
        var predicted = prediction.Observe(carrier, 2, .1f);
        var defender = new Opponent(new(4, 0, 0), new());
        defender.Update(carrier, 0, predicted, carrierPredictedDirection: predicted - carrier);
        Assert.Equal(DefenderState.TackleReady, defender.State);

        // The runner turns away: the defender stays in the same world position.
        carrier = new(0, 0, -1);
        predicted = prediction.Observe(carrier, 2, .1f);
        defender.Update(carrier, 0, predicted, carrierPredictedDirection: predicted - carrier);
        Assert.Equal(DefenderState.Locomotion, defender.State);

        predicted = prediction.Observe(carrier, 2, .1f);
        Assert.Equal(carrier, predicted);
        defender.Update(carrier, 0, predicted, carrierPredictedDirection: predicted - carrier);
        Assert.Equal(DefenderState.Locomotion, defender.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PursuitCanDiveWithoutEnteringReady(bool predictionStopped)
    {
        var defender = new Opponent(Vector3.Zero, new()
        {
            OpponentInitialYawDegrees = 0,
            OpponentJogSpeed = 9
        });
        defender.Update(new(0, 0, -1.8f), 0,
            carrierPredictedDirection: predictionStopped ? Vector3.Zero : -Vector3.UnitZ);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
    }

    [Fact]
    public void LeavingReadyConeDoesNotCancelReachableWrapOrDive()
    {
        foreach (float distance in new[] { .8f, 1.8f })
        {
            var defender = new Opponent(Vector3.Zero, new() { OpponentInitialYawDegrees = 0 });
            defender.Update(new(0, 0, 4), 0, carrierPredictedDirection: -Vector3.UnitZ);
            Assert.Equal(DefenderState.TackleReady, defender.State);
            defender.Update(new(0, 0, -distance), 0, carrierPredictedDirection: -Vector3.UnitZ);
            Assert.Equal(distance < 1 ? DefenderState.SetWrap : DefenderState.LungeTackle, defender.State);
        }
    }
}
