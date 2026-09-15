using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class CarrierPursuitPredictionTests
{
    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"Expected {expected}, got {actual}");

    [Theory]
    [InlineData(1, 1f)]
    [InlineData(2, 2f)]
    [InlineData(3, 3f)]
    public void CarrierTierSelectsLead(int tier, float distance)
    {
        var prediction = new CarrierPursuitPrediction();
        prediction.Reset(Vector3.Zero);
        var position = new Vector3(0, 0, -0.5f);
        Near(position - Vector3.UnitZ * distance, prediction.Observe(position, tier, 0.1f));
    }

    [Fact]
    public void DiagonalUsesActualWorldDirection()
    {
        var prediction = new CarrierPursuitPrediction();
        prediction.Reset(Vector3.Zero);
        var position = new Vector3(0.3f, 0, -0.4f);
        Near(position + new Vector3(1.2f, 0, -1.6f), prediction.Observe(position, 2, 0.1f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.001f)]
    public void StationaryClearsPreviousLead(float displacement)
    {
        var prediction = new CarrierPursuitPrediction();
        prediction.Reset(Vector3.Zero);
        prediction.Observe(Vector3.UnitX, 3, 0.1f);
        var position = Vector3.UnitX + new Vector3(displacement, 0, 0);
        Near(position, prediction.Observe(position, 3, 0.1f));
    }

    [Fact]
    public void DirectionChangeIsSmoothedThenConverges()
    {
        var prediction = new CarrierPursuitPrediction();
        prediction.Reset(Vector3.Zero);
        var position = Vector3.UnitX;
        prediction.Observe(position, 2, 0.1f);
        position -= Vector3.UnitZ * 0.1f;
        var lead = prediction.Observe(position, 2, 0.01f) - position;
        Assert.True(lead.X > 0f && lead.Z < 0f);
        Assert.True(lead.X > -lead.Z);
        Assert.Equal(2f, lead.Length(), 4);
        for (int i = 0; i < 150; i++)
        {
            position -= Vector3.UnitZ * 0.1f;
            lead = prediction.Observe(position, 2, 0.01f) - position;
        }
        Near(-Vector3.UnitZ * 2f, lead);
    }

    [Fact]
    public void VerticalMovementNeverChangesLead()
    {
        var flat = new CarrierPursuitPrediction();
        var airborne = new CarrierPursuitPrediction();
        flat.Reset(Vector3.Zero);
        airborne.Reset(Vector3.Zero);
        var position = new Vector3(0.3f, 0, -0.4f);
        Near(flat.Observe(position, 3, 0.1f) + Vector3.UnitY * 8f,
            airborne.Observe(position + Vector3.UnitY * 8f, 3, 0.1f));
        position += Vector3.UnitY * 12f;
        Near(position, airborne.Observe(position, 3, 0.1f));
    }

    [Fact]
    public void ResetAndMissingElapsedTimeDoNotInventMovement()
    {
        var prediction = new CarrierPursuitPrediction();
        Near(Vector3.One, prediction.Observe(Vector3.One, 3, 0.1f));
        prediction.Reset(Vector3.Zero);
        Near(Vector3.One, prediction.Observe(Vector3.One, 3, 0f));
        Near(Vector3.One, prediction.Observe(Vector3.One, 3, 0.1f));
    }

    [Fact]
    public void PursuitSteersAtLeadButPaceStillUsesCarrierSeparation()
    {
        var opponent = new Opponent(Vector3.Zero, new());
        var carrier = new Vector3(0, 0, -20);
        var prediction = new CarrierPursuitPrediction();
        prediction.Reset(carrier - Vector3.UnitX);
        var target = prediction.Observe(carrier, 3, 0.1f);
        opponent.Update(carrier, 0.1f, target);
        Assert.Equal(DefenderState.Locomotion, opponent.State);
        Assert.Equal(OpponentPace.Run, opponent.Pace);
        Near(Vector3.Normalize(target), Vector3.Normalize(opponent.Position));
    }

    [Theory]
    [InlineData(0.9f, DefenderState.SetWrap)]
    [InlineData(1.8f, DefenderState.LungeTackle)]
    public void PredictionSteersReadyButDoesNotChangeTackleSelectionOrCommitRange(
        float commitDistance, DefenderState expectedState)
    {
        var actual = new Opponent(Vector3.Zero, new());
        var carrier = new Vector3(0, 0, -4);
        // A prediction inside wrap range cannot trigger a distant tackle.
        actual.Update(carrier, 0.05f, Vector3.UnitX);
        actual.Update(carrier, 0.05f, Vector3.UnitX);
        Assert.Equal(DefenderState.TackleReady, actual.State);
        Assert.True(actual.Position.X > 0f);
        Assert.Equal(0f, actual.Position.Z, 4);

        var baseline = new Opponent(Vector3.Zero, new());
        actual.Reset();
        actual.Update(new(0, 0, -30), 0.01f);
        baseline.Update(new(0, 0, -30), 0.01f);
        carrier = actual.Position + new Vector3(0, 0, -4);
        actual.Update(carrier, 0f, carrier + Vector3.UnitX * 3f);
        baseline.Update(carrier, 0f);
        carrier = actual.Position + new Vector3(0, 0, -commitDistance);
        for (int i = 0; i < 4; i++)
        {
            actual.Update(carrier, 0.05f, carrier + Vector3.UnitX * 3f);
            baseline.Update(carrier, 0.05f);
            Assert.Equal(expectedState, actual.State);
            Assert.Equal(baseline.AnimationName, actual.AnimationName);
            Assert.Equal(baseline.FacingYawDegrees, actual.FacingYawDegrees);
            Near(baseline.Position, actual.Position);
            Assert.Equal(baseline.IsTouching(carrier), actual.IsTouching(carrier));
        }
    }
}
