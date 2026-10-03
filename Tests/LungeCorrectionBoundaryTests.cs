using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class LungeCorrectionBoundaryTests
{
    [Theory]
    [InlineData(.08f)]
    [InlineData(.12f)]
    [InlineData(.2f)]
    [InlineData(.35f)]
    [InlineData(.5f)]
    public void NearWindowEndNeverReversesOrExceedsRemainingCorrection(float window)
    {
        var config = new TackleAlleyConfig
        {
            LungeCorrectionWindowFraction = 1,
            LungeCorrectionRateDegrees = 90,
            LungeMaximumCorrectionDegrees = 15
        };
        float elapsed = window;
        for (int i = 0; i < 2048; i++)
        {
            elapsed = MathF.BitDecrement(elapsed);
            var direction = TackleAiming.Correct(Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitX,
                elapsed, 1f / 240, window, 50, config);
            Assert.True(float.IsFinite(direction.X) && float.IsFinite(direction.Z));
            float angle = MathF.Atan2(direction.X, direction.Z);
            double remaining = (double)window - elapsed;
            double maximum = 99 * remaining * remaining / (2 * window) * Math.PI / 180;
            Assert.InRange((double)angle, 0, maximum + 1e-10);
        }
    }

    [Fact]
    public void CrossingWindowMatchesSplittingStepAndLocksAfterward()
    {
        var config = new TackleAlleyConfig
        {
            LungeCorrectionWindowFraction = .2f,
            LungeCorrectionRateDegrees = 90,
            LungeMaximumCorrectionDegrees = 15
        };
        Vector3 Correct(Vector3 current, float elapsed, float dt) =>
            TackleAiming.Correct(Vector3.UnitZ, current, Vector3.UnitX, elapsed, dt, 1, 50, config);
        var whole = Correct(Vector3.UnitZ, .1f, .2f);
        var split = Correct(Correct(Vector3.UnitZ, .1f, .05f), .15f, .15f);
        Assert.True(Vector3.Distance(whole, split) < 1e-6f);
        Assert.Equal(whole, Correct(whole, .2f, .1f));
        Assert.Equal(whole, Correct(whole, .3f, .1f));
    }
}
