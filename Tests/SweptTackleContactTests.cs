using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class SweptTackleContactTests
{
    private static ContactCapsule Capsule(float x, float z = 0, int body = 1, float radius = .2f) =>
        new(new(x, 1, z), new(x, 1.4f, z), radius, body);

    [Fact]
    public void DetectsPassThroughBetweenFramesAndEarliestBodyPair()
    {
        var hit = SweptTackleContact.Find([Capsule(-2)], [Capsule(2)],
            [Capsule(1, body: 0), Capsule(0, body: 1)], [Capsule(1, body: 0), Capsule(0, body: 1)], .1f);
        Assert.NotNull(hit);
        Assert.InRange(hit.Value.Time, .39999f, .40001f);
        Assert.Equal(1, hit.Value.CarrierBody);
        Assert.Equal(TackleBodyRegion.Torso, hit.Value.CarrierRegion);
        Assert.True(Vector3.Distance(Vector3.UnitX, hit.Value.Normal) < .0001f);
        Assert.True(Vector3.Distance(hit.Value.DefenderPoint, hit.Value.CarrierPoint) < .0001f);
        Assert.Equal(40, hit.Value.RelativeVelocity.X, 3);
    }

    [Fact]
    public void MovingCarrierAndRotatingCapsuleAreSweptTogether()
    {
        var hit = SweptTackleContact.Find([Capsule(-2)], [Capsule(0)],
            [Capsule(2)], [Capsule(-2)], .2f);
        Assert.NotNull(hit);
        Assert.InRange(hit.Value.Time, .59999f, .60001f);
        Assert.Equal(30, hit.Value.RelativeVelocity.X, 3);
        ContactCapsule arm0 = new(new(-1, 1, 0), new(-1, 2, 0), .1f, 3);
        ContactCapsule arm1 = new(new(-1, 1, 0), new(1, 2, 0), .1f, 3);
        var stationary = new ContactCapsule(new(0, 1.8f, 0), new(0, 1.8f, 0), .1f, 0);
        Assert.NotNull(SweptTackleContact.Find([arm0], [arm1], [stationary], [stationary], .1f));
    }

    [Fact]
    public void InitialOverlapAndZeroLengthCapsulesAreSupported()
    {
        var sphere = new ContactCapsule(Vector3.Zero, Vector3.Zero, .2f, 0);
        Assert.Equal(0, SweptTackleContact.Find([sphere], [sphere], [sphere], [sphere], .1f)!.Value.Time);
        Assert.Null(SweptTackleContact.Find([sphere], [sphere], [sphere], [sphere], 0));
    }

    [Fact]
    public void LockedDirectionLetsLateJukeMiss()
    {
        var lockedStart = Capsule(-2);
        var lockedEnd = Capsule(2);
        Assert.NotNull(SweptTackleContact.Find([lockedStart], [lockedEnd], [Capsule(0)], [Capsule(0)], .4f));
        // Carrier leaves the path during the same frame, after the launch direction has locked.
        Assert.Null(SweptTackleContact.Find([lockedStart], [lockedEnd], [Capsule(0)], [Capsule(0, 3)], .4f));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    public void TimeOfImpactIsStableAcrossFrameRatesAndSupportedSpeeds(int fps)
    {
        foreach (float speed in new[] { 4f, 9f, 12f, 30f })
        {
            float? time = null;
            for (int i = 0; i < fps; i++)
            {
                float t = i / (float)fps, dt = 1f / fps;
                var hit = SweptTackleContact.Find([Capsule(-2 + speed * t)], [Capsule(-2 + speed * (t + dt))],
                    [Capsule(0)], [Capsule(0)], dt);
                if (hit.HasValue) { time = t + hit.Value.Time * dt; break; }
            }
            Assert.NotNull(time);
            Assert.InRange(time.Value, 1.6f / speed - .00001f, 1.6f / speed + .00001f);
        }
    }

    [Fact]
    public void ActualNormalAndImpactRegionPreserveGlancingAndFullOutcomes()
    {
        var physical = new PlayerPhysicalAttributes(new(), new());
        var direct = SweptTackleContact.Find([Capsule(-2)], [Capsule(2)],
            [Capsule(0)], [Capsule(0)], .5f)!.Value;
        var glance = SweptTackleContact.Find([Capsule(-2, .3999f)], [Capsule(2, .3999f)],
            [Capsule(0)], [Capsule(0)], .5f)!.Value;
        TackleImpact Impact(SweptContact hit) => TackleImpact.Calculate(physical, physical,
            Vector3.UnitX * 8, Vector3.Zero, hit.Normal, hit.Point.Y, true,
            impactRelativeVelocity: hit.RelativeVelocity, region: hit.CarrierRegion);
        Assert.Equal(PhysicalTackleOutcome.Glancing, Impact(glance).Outcome);
        Assert.Equal(PhysicalTackleOutcome.BroughtDown, Impact(direct).Outcome);
    }

    [Fact]
    public void ConfirmedImpactUsesProvidedBodiesAndPointInRagdollHandoff()
    {
        var d = RagdollContactTests.Create(new(0, 4, -.3f), Vector3.UnitZ * 6);
        var c = RagdollContactTests.Create(new(0, 4, 0), Vector3.Zero);
        var supplied = new RagdollHit(3, 7, new(0, 4.5f, -.1f), Vector3.UnitZ);
        Assert.Equal(supplied, RagdollContact.Resolve(d, c, true, impactContact: supplied));
    }
}
