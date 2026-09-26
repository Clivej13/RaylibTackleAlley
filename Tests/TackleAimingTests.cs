using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class TackleAimingTests
{
    private readonly TackleAlleyConfig _config = new();
    private PlayerPhysicalAttributes Physical(PlayerProfile? p = null) => new(p ?? new(), _config);
    private ContactSolution Solve(Vector3 position, Vector3 velocity = default, float confidence = 1,
        float duration = .6f, float speed = 4, float acceleration = 8, PlayerProfile? carrier = null) =>
        TackleAiming.Solve(Vector3.Zero, -Vector3.UnitZ * speed, -Vector3.UnitZ, position, velocity,
            Vector3.Zero, Physical(), TackleAiming.Target(position, Physical(carrier)), acceleration,
            speed, duration, confidence, _config);

    [Fact]
    public void StationaryCarrierHasDirectEarliestReachableContact()
    {
        var hit = Solve(new(0, 0, -2));
        Assert.True(hit.Reachable);
        Assert.Equal(-Vector3.UnitZ, hit.LaunchDirection);
        Assert.Equal(TackleBodyRegion.PelvisLowerChest, hit.Region);
        Assert.InRange(hit.ContactPoint.Y, Physical().TackleContactHeight, Physical().CollisionHeight * .72f);
        float radius = Physical().TorsoRadius * 2;
        Assert.Equal((2 - radius) / 4, hit.ContactTime, 4);
        Assert.False(Solve(new(0, 0, -2), duration: hit.ContactTime - .0001f).Reachable);
    }

    [Fact]
    public void StableCrossingMotionLeadsAndEscapingCarrierIsUnreachable()
    {
        var hit = Solve(new(0, 0, -2), Vector3.UnitX * 2);
        Assert.True(hit.Reachable);
        Assert.True(hit.ContactPoint.X > 0);
        Assert.True(hit.LaunchDirection.X > 0);
        Assert.False(Solve(new(0, 0, -2), -Vector3.UnitZ * 12).Reachable);
        Assert.False(Solve(new(0, 0, -20)).Reachable);
    }

    [Fact]
    public void ConfidenceDropsOnAccelerationSteeringAndEvadesThenRecovers()
    {
        var history = new TackleMotionObservation();
        history.Observe(-Vector3.UnitZ * 6, 1f / 60, false, _config);
        Assert.Equal(1, history.Confidence);
        history.Observe(Vector3.UnitX * 6, 1f / 60, false, _config);
        Assert.True(history.Confidence < .2f);
        for (int i = 0; i < 120; i++) history.Observe(Vector3.UnitX * 6, 1f / 60, false, _config);
        Assert.True(history.Confidence > .99f);
        history.Observe(Vector3.UnitX * 6, 1f / 60, true, _config);
        Assert.True(history.Confidence <= .15f);
        Assert.InRange(history.Acceleration.Length(), 0, _config.TackleAccelerationPredictionLimit);
    }

    [Fact]
    public void LowConfidenceRequiresEarlierContact()
    {
        float lead = _config.OpponentLungeAnimationLeadSeconds;
        Assert.True(TackleAiming.Horizon(.6f, .3f, lead, _config) < TackleAiming.Horizon(.6f, 1, lead, _config));
        Assert.True(Solve(new(0, 0, -2.5f)).Reachable);
        Assert.False(Solve(new(0, 0, -2.5f), confidence: .3f).Reachable);
        Assert.False(Solve(new(0, 0, -.7f), confidence: .1f).Reachable);
    }

    [Fact]
    public void WrapUsesBothScaledBodiesFacingHeightAndRelativeMotion()
    {
        var small = Physical(new() { Height = 1.65f, Build = -1, Weight = 70 });
        var large = Physical(new() { Height = 2.05f, Build = 1, Weight = 160 });
        var waist = TackleAiming.Target(new(0, 0, -.96f), large, TackleBodyRegion.Waist);
        bool Wrap(PlayerPhysicalAttributes p, Vector3 facing, Vector3 velocity, TackleTarget target) =>
            TackleAiming.TryWrap(Vector3.Zero, Vector3.Zero, facing, velocity, p, target, _config, out _);
        Assert.False(Wrap(small, -Vector3.UnitZ, Vector3.Zero, waist));
        Assert.True(Wrap(large, -Vector3.UnitZ, Vector3.Zero, waist));
        Assert.False(Wrap(large, Vector3.UnitZ, Vector3.Zero, waist));
        Assert.False(Wrap(large, -Vector3.UnitZ, -Vector3.UnitZ * 10, waist));
        Assert.False(Wrap(large, -Vector3.UnitZ, Vector3.Zero, waist with { Point = waist.Point + Vector3.UnitY * 3 }));
        Assert.True(large.PelvisRadius > small.PelvisRadius);
        Assert.True(large.WrapReach > small.WrapReach);
    }

    [Theory]
    [InlineData(TackleBodyRegion.Waist)]
    [InlineData(TackleBodyRegion.PelvisLowerChest)]
    [InlineData(TackleBodyRegion.ThighHip)]
    [InlineData(TackleBodyRegion.Torso)]
    public void TargetRegionsUsePhysicalDimensionsAndAnimatedCapsules(TackleBodyRegion region)
    {
        var p = Physical(new() { Height = 1.65f, Build = -1 });
        var pose = RagdollContactTests.Create(new(1, 2, 3), Vector3.Zero, physical: p);
        var target = TackleAiming.Target(Vector3.Zero, p, region, pose);
        Assert.Equal(region, target.Region);
        Assert.True(target.Point.Y > 2);
        Assert.InRange(target.Point.X, .5f, 1.5f);
        Assert.True(target.Radius > 0);
    }

    private Vector3 Correct(float elapsed, float dt, int agility = 50, Vector3? current = null) =>
        TackleAiming.Correct(-Vector3.UnitZ, current ?? -Vector3.UnitZ, Vector3.UnitX,
            elapsed, dt, .6f, agility, _config);
    private static float Angle(Vector3 direction) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(-Vector3.UnitZ, direction), -1, 1)) * 180 / MathF.PI;

    [Fact]
    public void CorrectionTurnsTowardTargetWithRateFadeAndTotalBounds()
    {
        var first = Correct(0, .05f);
        Assert.True(first.X > 0);
        Assert.InRange(Angle(first), 0, 4.5f);
        Assert.True(Angle(Correct(.18f, .05f)) < Angle(first));
        _config.LungeCorrectionRateDegrees = 360;
        Assert.InRange(Angle(Correct(0, 1)), 17.99f, 18.001f);
    }

    [Theory]
    [InlineData(1, .9f)]
    [InlineData(50, 1f)]
    [InlineData(100, 1.1f)]
    public void AgilityOnlyModifiesCorrectionWithinBounds(int agility, float multiplier)
    {
        Assert.InRange(Angle(Correct(0, .1f, agility)) / Angle(Correct(0, .1f)), multiplier - .001f, multiplier + .001f);
    }

    [Fact]
    public void CorrectionEndsAndDirectionLocksExactlyAtWindowEnd()
    {
        var direction = Correct(0, .24f);
        Assert.Equal(direction, Correct(.24f, .1f, current: direction));
        Assert.Equal(direction, Correct(.5f, 1, current: direction));
        Assert.Equal(-Vector3.UnitZ, Correct(.24f, .01f));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void IntegratedCorrectionBudgetIsIndependentOfFrameRate(int fps)
    {
        Vector3 direction = -Vector3.UnitZ;
        for (int i = 0; i < fps; i++) direction = Correct(i / (float)fps, 1f / fps, current: direction);
        Assert.InRange(Vector3.Distance(Correct(0, 1), direction), 0, .00001f);
    }

    [Fact]
    public void IndividualSpeedAndAccelerationChangeReach()
    {
        var slow = new PlayerMovementAttributes(new() { Speed = 1, Acceleration = 1 }, _config, true);
        var fast = new PlayerMovementAttributes(new() { Speed = 100, Acceleration = 100 }, _config, true);
        Assert.True(TackleAiming.Reach(0, fast.AccelerationRate, fast.JogSpeed, .5f) >
            TackleAiming.Reach(0, slow.AccelerationRate, slow.JogSpeed, .5f));
        Assert.True(TackleAiming.Reach(1, 10, 8, .5f) > TackleAiming.Reach(1, 2, 8, .5f));
        Assert.True(TackleAiming.Reach(4, 10, 8, .5f) > TackleAiming.Reach(1, 10, 8, .5f));
    }

    [Fact]
    public void BuildChangesContactDimensionsWithoutChangingHeight()
    {
        var lean = Physical(new() { Build = -1 });
        var heavy = Physical(new() { Build = 1 });
        Assert.Equal(lean.CollisionHeight, heavy.CollisionHeight);
        Assert.True(heavy.TorsoRadius > lean.TorsoRadius);
        Assert.True(heavy.BodyPartContactRadii[7] > lean.BodyPartContactRadii[7]);
    }

    [Fact]
    public void AimingSettingsRejectNonfiniteAndOutOfRangeValues()
    {
        foreach (string property in new[] { "LungeCorrectionWindowFraction", "LungeCorrectionRateDegrees",
            "LungeMaximumCorrectionDegrees", "TackleMinimumPredictionConfidence", "TackleAccelerationPredictionLimit" })
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, -1f, 1000f })
        {
            var config = new TackleAlleyConfig();
            typeof(TackleAlleyConfig).GetProperty(property)!.SetValue(config, value);
            Assert.Throws<ArgumentOutOfRangeException>(config.ValidateTackleAiming);
        }
        foreach (int value in new[] { 0, 7, 513, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(new TackleAlleyConfig { TacklePredictionIterations = value }.ValidateTackleAiming);
    }

    [Fact]
    public void ZeroTimeSamplesDoNotInventAcceleration()
    {
        var history = new TackleMotionObservation();
        history.Observe(-Vector3.UnitZ * 6, .01f, false, _config);
        history.Observe(Vector3.Zero, 1e-9f, false, _config);
        history.Observe(-Vector3.UnitZ * 6, .01f, false, _config);
        Assert.Equal(1, history.Confidence);
        Assert.Equal(Vector3.Zero, history.Acceleration);
    }

    [Fact]
    public void AnimationLeadAndSupportedDurationBoundContact()
    {
        Assert.False(Solve(new(0, 0, -1), duration: .1f).Reachable);
        Assert.True(Solve(new(0, 0, -1)).ContactTime >= _config.OpponentLungeAnimationLeadSeconds);
        _config.LungeMaximumPredictionSeconds = .3f;
        Assert.False(Solve(new(0, 0, -2)).Reachable);
    }

    [Fact]
    public void CorrectedTravelIsStableAcrossSupportedFrameRates()
    {
        (Vector3 Position, Vector3 Direction) Simulate(int fps)
        {
            using var d = new Opponent(Vector3.Zero, new() { OpponentInitialYawDegrees = 0 });
            d.Update(new(0, 0, -4), 0, true, Vector2.Zero, false);
            d.Update(new(0, 0, -2), 0, null, Vector3.Zero);
            for (int i = 0; i < fps / 2; i++) d.Update(new(.3f, 0, -2), 1f / fps, null, Vector3.Zero);
            Assert.True(d.DirectionLocked);
            return (d.Position, d.CorrectedDirection);
        }
        var reference = Simulate(240);
        foreach (int fps in new[] { 30, 60, 120 })
        {
            var actual = Simulate(fps);
            Assert.InRange(Vector3.Distance(reference.Position, actual.Position), 0, .001f);
            Assert.InRange(Vector3.Distance(reference.Direction, actual.Direction), 0, .001f);
        }
    }

    [Fact]
    public void OpponentStoresCommitmentCorrectsEarlyAndLocksAfterward()
    {
        using var defender = new Opponent(Vector3.Zero, new() { OpponentInitialYawDegrees = 0 });
        defender.Update(new(0, 0, -4), 0, true, Vector2.Zero, false);
        defender.Update(new(0, 0, -2), 0, null, Vector3.Zero);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
        Assert.True(defender.InitialContactSolution.Reachable);
        Assert.Equal(4, defender.LaunchSpeed);
        defender.Update(new(.2f, 0, -2), .1f, null, Vector3.Zero);
        Assert.True(defender.CorrectedDirection.X > 0);
        defender.Update(new(.2f, 0, -2), .15f, null, Vector3.Zero);
        Assert.True(defender.DirectionLocked);
        var locked = defender.CorrectedDirection;
        defender.Update(new(-3, 0, -2), .1f, null, -Vector3.UnitX * 8);
        Assert.Equal(locked, defender.CorrectedDirection);
        Assert.Equal(-Vector3.UnitZ, defender.InitialLaunchDirection);
        defender.Reset();
        Assert.Null(defender.LastSweptContact);
        Assert.False(defender.InitialContactSolution.Reachable);
        Assert.Equal(1, defender.PredictionConfidence);
    }
}
