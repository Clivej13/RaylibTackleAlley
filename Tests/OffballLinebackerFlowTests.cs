using System.Numerics;
using System.Text.Json;
using RaylibTackleAlley.Game;
using Xunit;
using Xunit.Abstractions;

public sealed class OffballLinebackerFlowTests(ITestOutputHelper output)
{
    private static TackleAlleyConfig Config() => JsonSerializer.Deserialize<TackleAlleyConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.json")))!;
    private static DefenderProfile Linebacker() => DefenderProfileCatalog.Load(
        Path.Combine(AppContext.BaseDirectory, "defender-profiles.json")).Resolve("offball-linebacker");
    private static PlayerProfile Returner(string id) => ReturnerCatalog.Load(
        Path.Combine(AppContext.BaseDirectory, "returners.json")).Resolve(id).Profile;
    private static void Speed(Opponent defender, float speed) =>
        typeof(Opponent).GetProperty(nameof(Opponent.CurrentSpeed))!.SetValue(defender, speed);

    [Fact]
    public void SideInterceptionKeepsBoundedLeadOutsideBreakdownCone()
    {
        var config = Config();
        var profile = Linebacker();
        Vector3 carrier = new(0, 0, -10), velocity = new(8, 0, 0);
        var approach = DefenderDecision.PlanApproach(profile, config, Vector3.Zero, carrier,
            carrier + velocity, Vector3.UnitX, 9, 6.5f, 1, false, velocity);
        Assert.False(approach.InReadyCone);
        Assert.True(approach.PursuitTarget.X > 0);
        Assert.InRange(Vector3.Distance(carrier, approach.PursuitTarget), 0,
            velocity.Length() * profile.ApproachTuning!.MaximumPredictionSeconds);
        var legacy = DefenderDecision.PlanApproach(profile with { ApproachTuning = null }, config,
            Vector3.Zero, carrier, carrier + velocity, Vector3.UnitX, 9, 6.5f, 1, false, velocity);
        Assert.Equal(carrier, legacy.PursuitTarget);
    }

    [Fact]
    public void ReversalDropsStaleLeadAndLowConfidenceCannotOverLead()
    {
        var profile = Linebacker();
        var config = Config();
        Vector3 carrier = new(0, 0, -8);
        DefenderApproach Plan(Vector3 velocity, float confidence) => DefenderDecision.PlanApproach(
            profile, config, Vector3.Zero, carrier, carrier + Vector3.UnitX * 20,
            Vector3.UnitX, 9, 6.5f, 1, false, velocity, confidence);
        Assert.Equal(carrier, Plan(-Vector3.UnitX * 12, 1).PursuitTarget);
        Assert.Equal(carrier, Plan(Vector3.UnitX * 12, 0).PursuitTarget);
        Assert.True(Vector3.Distance(carrier, Plan(Vector3.UnitX * 12, .1f).PursuitTarget) <
            Vector3.Distance(carrier, Plan(Vector3.UnitX * 12, 1).PursuitTarget) * .11f);
    }

    [Fact]
    public void SuppressedPredictionDoesNotTogglePreparationAndStationaryCarrierCanBeSquaredUp()
    {
        var config = Config();
        var profile = Linebacker();
        var moving = DefenderDecision.PlanApproach(profile, config, Vector3.Zero, new(0, 0, -4),
            new(0, 0, -4), Vector3.Zero, 9, 6.5f, 1, false, Vector3.UnitZ * 10, 0);
        Assert.True(moving.Ready);
        Assert.Equal(new Vector3(0, 0, -4), moving.PursuitTarget);
        var stationary = DefenderDecision.PlanApproach(profile, config, Vector3.Zero, new(3, 0, 0),
            new(3, 0, 0), Vector3.Zero, 4, 6.5f, 1, false, Vector3.Zero, 0);
        Assert.True(stationary.Ready);
    }

    [Fact]
    public void ClosingTravelStartsBrakingEarlierWithoutExpandingReach()
    {
        var config = Config();
        var balanced = Linebacker();
        DefenderApproach Plan(float carrierSpeed, float distance) => DefenderDecision.PlanApproach(
            balanced, config, Vector3.Zero, new(0, 0, -distance), null, Vector3.UnitZ,
            10, 6.5f, 1, false, Vector3.UnitZ * carrierSpeed);
        var slow = Plan(6.5f, 6);
        var fast = Plan(14.8f, 6);
        Assert.True(fast.RequiredStoppingDistance > slow.RequiredStoppingDistance);
        Assert.True(fast.Ready);
        Assert.False(fast.CanBreakDown);
        Assert.True(Plan(14.8f, 2).Ready); // Too late still brakes instead of continuing sprint.
    }

    [Fact]
    public void CloseAlignedWrapDoesNotNeedAnExtraReadyFrame()
    {
        var config = Config();
        using var defender = new Opponent(Vector3.Zero, config, behaviorProfile: Linebacker());
        defender.Update(new(0, 0, .8f), 0, carrierVelocity: Vector3.Zero, carrierPredictedDirection: -Vector3.UnitZ);
        Assert.Equal(DefenderState.SetWrap, defender.State);
        Assert.Contains("wrap", defender.DecisionDebug.CommitmentReason!);
        Assert.True(defender.DecisionDebug.WrapReachable);
    }

    [Fact]
    public void ReachableGroundedApproachDefersDiveButDoesNotGrantEarlyWrap()
    {
        var config = Config();
        using var defender = new Opponent(Vector3.Zero, config, behaviorProfile: Linebacker());
        defender.Update(new(0, 0, 1.8f), 0, carrierVelocity: Vector3.Zero, carrierPredictedDirection: -Vector3.UnitZ);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        Assert.True(defender.DecisionDebug.LungeReachable);
        Assert.False(defender.DecisionDebug.WrapReachable);
        Assert.Contains("grounded wrap", defender.DecisionDebug.Reason);
        for (int i = 0; i < 60 && defender.State != DefenderState.SetWrap; i++)
            defender.Update(new(0, 0, 1.8f), 1f / 60, carrierVelocity: Vector3.Zero, carrierPredictedDirection: -Vector3.UnitZ);
        Assert.Equal(DefenderState.SetWrap, defender.State);
    }

    [Fact]
    public void PoorCloseAngleMustSquareUpBeforeWrapping()
    {
        var config = Config();
        using var defender = new Opponent(Vector3.Zero, config, behaviorProfile: Linebacker());
        Vector3 target = new(.4f, 0, .693f); // 30 degrees, inside old 45-degree wrap cone.
        defender.Update(target, 0, carrierVelocity: Vector3.Zero);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        Assert.False(defender.DecisionDebug.WrapReachable);
        for (int i = 0; i < 10 && defender.State != DefenderState.SetWrap; i++)
            defender.Update(target, 1f / 60, carrierVelocity: Vector3.Zero);
        Assert.Equal(DefenderState.SetWrap, defender.State);
    }

    [Fact]
    public void LateFastApproachCanDiveAndCannotHomeAfterEarlyWindow()
    {
        var config = Config();
        using var defender = new Opponent(Vector3.Zero, config, behaviorProfile: Linebacker());
        Speed(defender, 9);
        defender.Update(new(0, 0, 1.8f), 0, carrierVelocity: Vector3.Zero, carrierPredictedDirection: -Vector3.UnitZ);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
        Vector3 initial = defender.InitialLaunchDirection;
        string reason = defender.DecisionDebug.CommitmentReason!;
        defender.Update(new(.7f, 0, 1.8f), .13f, carrierVelocity: Vector3.UnitX * 10);
        Assert.True(defender.DirectionLocked);
        Assert.Equal(0, defender.DecisionDebug.CorrectionSecondsRemaining);
        Assert.Equal(0, defender.DecisionDebug.CorrectionDegreesRemaining);
        Vector3 locked = defender.CorrectedDirection;
        float deviation = MathF.Acos(Math.Clamp(Vector3.Dot(initial, locked), -1, 1)) * 180 / MathF.PI;
        Assert.InRange(deviation, 0, 4); // 60 deg/s * 0.12s / 2, before agility.
        defender.Update(new(-10, 0, -10), .2f, carrierVelocity: -Vector3.UnitX * 20);
        Assert.Equal(locked, defender.CorrectedDirection);
        Assert.Equal(reason, defender.DecisionDebug.CommitmentReason);
    }

    [Theory]
    [InlineData("darius-stone", 1)]
    [InlineData("darius-stone", 2)]
    [InlineData("darius-stone", 3)]
    [InlineData("marcus-reed", 1)]
    [InlineData("marcus-reed", 2)]
    [InlineData("marcus-reed", 3)]
    public void ShippedApproachMatrixPreservesMomentumAndPhysicalReach(string returnerId, int tier)
    {
        var config = Config();
        var carrier = Returner(returnerId);
        var movement = new PlayerMovementAttributes(carrier, config);
        var physical = new PlayerPhysicalAttributes(carrier, config);
        var defenders = LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), config).Resolve("level-1").Defenders;
        foreach (var entry in defenders)
        {
            var tuned = Simulate(Linebacker());
            var previous = Simulate(new DefenderProfile());
            output.WriteLine($"{returnerId} tier {tier} vs {entry.Profile.Name}: speed={movement.TierSpeed(tier):F3}, " +
                $"old breakdown={previous.Breakdown:F3} choice={previous.Choice}, " +
                $"new breakdown={tuned.Breakdown:F3} readyMin={tuned.MinimumSpeed:F3} choice={tuned.Choice} at={tuned.Distance:F3}m");
            Assert.NotNull(tuned.Breakdown);
            Assert.True(tuned.MinimumSpeed > 0);
            Assert.Equal(1, tuned.PreparationEntries); // No breakdown/pursuit chatter.
            Assert.True(tuned.Choice is DefenderState.SetWrap or DefenderState.LungeTackle);

            (float? Breakdown, float MinimumSpeed, DefenderState Choice, float Distance, int PreparationEntries) Simulate(DefenderProfile behavior)
            {
                using var defender = new Opponent(new(0, 0, -12), config, entry.Profile, behavior);
                var prediction = new CarrierPursuitPrediction(config);
                Vector3 position = Vector3.Zero, velocity = -Vector3.UnitZ * movement.TierSpeed(tier);
                prediction.Reset(position);
                float? breakdownDistance = null;
                float minimumPreparingSpeed = float.PositiveInfinity;
                int entries = 0;
                bool preparing = false;
                float initialWrapReach = defender.Physical.WrapReach, initialDiveReach = defender.Physical.DiveReach;
                for (int frame = 0; frame < 180; frame++)
                {
                    position += velocity / 60;
                    var predicted = prediction.Observe(position, tier, 1f / 60, movement.TierSpeed(tier));
                    defender.Update(position, 1f / 60, predicted, velocity, -Vector3.UnitZ, physical);
                    if (defender.State == DefenderState.TackleReady)
                    {
                        if (!preparing) entries++;
                        breakdownDistance ??= defender.DecisionDebug.CarrierDistance;
                        minimumPreparingSpeed = Math.Min(minimumPreparingSpeed, defender.CurrentSpeed);
                    }
                    preparing = defender.State == DefenderState.TackleReady;
                    Assert.Equal(initialWrapReach, defender.Physical.WrapReach);
                    Assert.Equal(initialDiveReach, defender.Physical.DiveReach);
                    if (defender.State is DefenderState.SetWrap or DefenderState.LungeTackle)
                    {
                        Assert.True(defender.CurrentContactSolution.Reachable);
                        break;
                    }
                }
                return (breakdownDistance, minimumPreparingSpeed, defender.State,
                    defender.DecisionDebug.CarrierDistance, entries);
            }
        }
    }

    [Fact]
    public void HigherSprintSpeedLeavesLessTravelTimeWithoutChangingReachOrReactionSettings()
    {
        var config = Config();
        var low = new PlayerMovementAttributes(Returner("darius-stone"), config);
        var high = new PlayerMovementAttributes(Returner("marcus-reed"), config);
        float slowWindowRatio = low.JogSpeed / high.JogSpeed;
        float runWindowRatio = low.RunningSpeed / high.RunningSpeed;
        float sprintWindowRatio = low.SprintSpeed / high.SprintSpeed;
        Assert.True(sprintWindowRatio < runWindowRatio && runWindowRatio < slowWindowRatio);
        Assert.True(sprintWindowRatio < .8f);
        Assert.Equal(.15f, Linebacker().ReactionTime);
        Assert.Equal(.2f, config.DefenderMovementScaling.SpeedTierInfluence.Sprint);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void RepeatedCutsNeverChaseStalePredictionAndLeadRecoversAtSteadyPace(int hz)
    {
        var config = Config();
        var carrier = Returner("marcus-reed");
        var movement = new PlayerMovementAttributes(carrier, config);
        using var defender = new Opponent(new(0, 0, -30), config, behaviorProfile: Linebacker());
        var prediction = new CarrierPursuitPrediction(config);
        Vector3 position = Vector3.Zero;
        prediction.Reset(position);
        float dt = 1f / hz;
        for (int frame = 0; frame < hz * 2; frame++)
        {
            float side = frame < hz / 2 || frame >= hz ? 1 : -1;
            Vector3 velocity = new(side * 8, 0, -movement.RunningSpeed);
            position += velocity * dt;
            Vector3 predicted = prediction.Observe(position, 2, dt, movement.RunningSpeed);
            defender.Update(position, dt, predicted, velocity, Vector3.Normalize(velocity));
            var lead = defender.DecisionDebug.PursuitTarget - position;
            Assert.True(Vector3.Dot(lead, velocity) >= -.0001f);
            Assert.True(lead.Length() <= velocity.Length() * Linebacker().ApproachTuning!.MaximumPredictionSeconds + .001f);
            if (frame == hz / 2 || frame == hz)
                Assert.True(lead.Length() < .1f, "A direction surprise should suppress the lead immediately.");
        }
        Assert.True(Vector3.Distance(defender.DecisionDebug.PursuitTarget, position) > .1f);
    }

    [Fact]
    public void BadLungeAngleFailsAndEscapingCarrierDoesNotProduceAnImaginaryWrap()
    {
        var config = Config();
        using var defender = new Opponent(Vector3.Zero, config, behaviorProfile: Linebacker());
        Speed(defender, 9);
        defender.Update(new(1.8f, 0, 0), 0, carrierVelocity: Vector3.Zero);
        Assert.False(defender.DecisionDebug.LungeReachable);
        Assert.NotEqual(DefenderState.LungeTackle, defender.State);

        var profile = Linebacker();
        var approach = DefenderDecision.PlanApproach(profile, config, Vector3.Zero,
            new(0, 0, -2), null, null, 6.5f, 6.5f, 1, true, new(0, 0, -14.8f));
        Assert.False(DefenderDecision.CanApproachWrap(profile, config, approach,
            Vector3.Zero, -Vector3.UnitZ, new(0, 0, -2), new(0, 0, -14.8f), 6.5f, 6.5f, 1, 360));
    }

    [Fact]
    public void DebugShowsSignedClosingTimeBodyTargetsAndResetClearsCommitment()
    {
        var config = Config();
        using var defender = new Opponent(Vector3.Zero, config, behaviorProfile: Linebacker());
        defender.Update(new(0, 0, 20), .01f, carrierVelocity: new(0, 0, -8));
        var debug = defender.DecisionDebug;
        Assert.True(debug.RelativeClosingSpeed > 8);
        Assert.Equal((debug.CarrierDistance - debug.WrapReach) / debug.RelativeClosingSpeed,
            debug.EstimatedTimeToContact, 5);
        Assert.True(debug.TackleAimTarget.Y > 0);
        Assert.Equal(defender.Physical.DiveReach, debug.LungeReach);
        defender.Update(new(0, 0, 20), 0, carrierVelocity: new(0, 0, 20));
        Assert.True(defender.DecisionDebug.RelativeClosingSpeed < 0);
        Assert.True(float.IsPositiveInfinity(defender.DecisionDebug.EstimatedTimeToContact));
        defender.Reset();
        Assert.Null(defender.DecisionDebug.CommitmentReason);
        Assert.Equal(0, defender.DecisionDebug.CorrectionSecondsRemaining);
    }

    [Fact]
    public void UntunedPresetsRetainEveryAuthoredValue()
    {
        var profiles = DefenderProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
        Assert.Equal(new DefenderProfile
        {
            Id = "aggressive", Name = "Aggressive", ReactionTime = .1f,
            PursuitPredictionStrength = 1.2f, PursuitAggression = 1.2f,
            BreakdownDistance = 3.5f, BreakdownExitDistance = 4.5f,
            WrapPreference = .4f, CorrectionRateDegrees = 110, MaximumCorrectionDegrees = 22
        }, profiles.Resolve("aggressive"));
        Assert.Equal(new DefenderProfile
        {
            Id = "contain", Name = "Contain", ReactionTime = .25f,
            PursuitPredictionStrength = .65f, PursuitAggression = .85f, ContainBias = .6f,
            ApproachDistance = 10, BreakdownDistance = 5, BreakdownExitDistance = 6,
            TackleCommitDistance = 3, LungePreference = .35f, CorrectionWindowFraction = .3f,
            CorrectionRateDegrees = 65, MaximumCorrectionDegrees = 12
        }, profiles.Resolve("contain"));
    }

    [Fact]
    public void OnlyLinebackerOptsInAndIdentityDoesNotChooseRules()
    {
        var profiles = DefenderProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
        Assert.NotNull(profiles.Resolve("offball-linebacker").ApproachTuning);
        foreach (string id in new[] { "aggressive", "contain" })
            Assert.Null(profiles.Resolve(id).ApproachTuning);
        var balanced = Linebacker();
        var renamed = balanced with { Id = "renamed", Name = "Renamed" };
        DefenderApproach Plan(DefenderProfile p) => DefenderDecision.PlanApproach(p, Config(),
            Vector3.Zero, new(0, 0, -6), new(1, 0, -6), Vector3.UnitZ, 9, 6.5f, 1, false, Vector3.UnitZ * 12);
        Assert.Equal(Plan(balanced), Plan(renamed));
    }

    [Fact]
    public void ApproachTuningRejectsInvalidAndMissingValues()
    {
        var tuning = Linebacker().ApproachTuning!;
        foreach (var property in typeof(DefenderApproachTuning).GetProperties())
        {
            var copy = tuning with { };
            property.SetValue(copy, float.NaN);
            Assert.Throws<ArgumentException>(copy.Validate);
        }
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DefenderApproachTuning>("{}"));
    }
}
