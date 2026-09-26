using System.Numerics;
using System.Text.Json;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class PhysicalAttributesTests
{
    private static PlayerPhysicalAttributes Attributes(float weight = 110, int strength = 50, float height = 2,
        TackleAlleyConfig? config = null) => new(new() { Weight = weight, Strength = strength, Height = height }, config ?? new());

    [Theory]
    [InlineData(1, .8f, .9f)]
    [InlineData(50, 1f, 1f)]
    [InlineData(100, 1.2f, 1.1f)]
    public void StrengthUsesSharedNeutralConversion(int rating, float force, float balance)
    {
        var a = Attributes(strength: rating);
        Assert.Equal(force, a.StrengthMultiplier, 6);
        Assert.Equal(force, a.TackleForceMultiplier, 6);
        Assert.Equal(force, a.TackleResistanceMultiplier, 6);
        Assert.Equal(force, a.ActiveMotorMultiplier, 6);
        Assert.Equal(balance, a.BalanceSupportMultiplier, 6);
        if (rating == 50) Assert.Equal(1f, a.RecoveryRate);
    }

    [Theory]
    [InlineData(70, 1.65f, 1)]
    [InlineData(160, 2.05f, 100)]
    [InlineData(70, 2.05f, 100)]
    [InlineData(160, 1.65f, 1)]
    [InlineData(110, 2f, 50)]
    public void PhysicalMassesDimensionsAndGroundRemainValid(float weight, float height, int strength)
    {
        var a = Attributes(weight, strength, height);
        Assert.Equal(weight, a.BodyPartMasses.Sum(), 4);
        Assert.Equal(18f / 25, a.BodyPartMasses[0] / a.BodyPartMasses[1], 5);
        Assert.InRange(a.RadiusScale, PlayerPhysicalAttributes.MinimumRadiusScale, PlayerPhysicalAttributes.MaximumRadiusScale);
        Assert.Equal(height / 2, a.HeightRatio);
        Assert.Equal(.24f * a.RadiusScale, a.TorsoRadius);
        var doll = RagdollContactTests.Create(Vector3.Zero, new(0, 0, -3), physical: a);
        Assert.Equal(weight, doll.Bodies.Sum(b => b.Mass), 4);
        Assert.Equal(.15f * a.RadiusScale, doll.Bodies[0].Radius, 5);
        Assert.Equal(a.TorsoRadius, RagdollContact.Radius(doll.Bodies[1]), 5);
        Assert.Equal(1.2f * a.HeightRatio, doll.Bodies[0].Position.Y, 5);
        Assert.Equal(.45f * a.HeightRatio, Vector3.Distance(
            doll.Bodies[7].Position, doll.Bodies[8].Position), 4);
        doll.StartActiveDrive(_ => doll.Joints.Select(j => j.ReferenceRotation).ToArray());
        for (int i = 0; i < 240; i++)
        {
            doll.Update(Ragdoll.FixedStep);
            Assert.All(doll.Bodies, b => {
                Assert.True(float.IsFinite(b.Position.LengthSquared()));
                Assert.True(b.Bottom >= -.0001f);
                Assert.InRange(b.AngularVelocity.Length(), 0, 12.001f);
            });
            Assert.All(doll.Joints, j => Assert.InRange(doll.JointSeparation(j), 0, .1f));
        }
        Assert.InRange(a.RecoveryRate, .85f, 1.15f);
    }

    [Fact]
    public void MomentumCombinesMassAndSpeed()
    {
        var light = Attributes(80); var heavy = Attributes(160);
        Assert.Equal(heavy.Momentum(new(0, 0, 4)), light.Momentum(new(0, 0, 8)));
        Assert.True(heavy.Momentum(Vector3.UnitX).Length() > light.Momentum(Vector3.UnitX).Length());
    }

    [Fact]
    public void DirectionRelativeSpeedAndStrengthDriveSeverity()
    {
        var a = Attributes();
        TackleImpact Hit(Vector3 d, Vector3 c, Vector3 n, PlayerPhysicalAttributes? dp = null,
            PlayerPhysicalAttributes? cp = null) => TackleImpact.Calculate(dp ?? a, cp ?? a, d, c, n, 1, true);
        var head = Hit(new(0, 0, 6), new(0, 0, -4), Vector3.UnitZ);
        var side = Hit(new(0, 0, 6), new(0, 0, -4), Vector3.UnitX);
        Assert.Equal(10, head.ClosingSpeed);
        Assert.Equal(0, side.ImpactScore);
        Assert.NotEqual(head.Outcome, side.Outcome);
        Assert.Equal(head, Hit(new(40, 0, 6), new(-20, 0, -4), Vector3.UnitZ));
        Assert.Equal(0, Hit(Vector3.UnitZ, Vector3.UnitZ * 2, Vector3.UnitZ).ImpactScore);
        var strong = Hit(new(0, 0, 6), new(0, 0, -4), Vector3.UnitZ, Attributes(strength:100));
        Assert.True(strong.ImpactScore > head.ImpactScore);
        Assert.True(strong.Transfer > head.Transfer);
        Assert.True(Hit(new(0, 0, 6), new(0, 0, -4), Vector3.UnitZ, cp:Attributes(strength:100)).ResistanceScore > head.ResistanceScore);
    }

    [Fact]
    public void WrapUsesReachWeightAndStrengthAndAllowsWeakContactToBreak()
    {
        var small = Attributes(70, 1, 1.65f); var large = Attributes(160, 100, 2.05f);
        TackleImpact Wrap(PlayerPhysicalAttributes d, PlayerPhysicalAttributes c, float distance) =>
            TackleImpact.Calculate(d, c, Vector3.UnitZ * 2, -Vector3.UnitZ * 5, Vector3.UnitZ, 1, false, 1, distance);
        Assert.Equal(0, Wrap(small, large, .95f).ImpactScore);
        Assert.True(Wrap(large, small, .95f).ImpactScore > 0);
        Assert.True(Wrap(large, small, .5f).Severity > Wrap(small, large, .5f).Severity);
        Assert.Equal(PhysicalTackleOutcome.Glancing, Wrap(small, large, .5f).Outcome);
        var stationary = TackleImpact.Calculate(Attributes(), Attributes(), Vector3.Zero, Vector3.Zero, Vector3.UnitZ, 1, false);
        Assert.Equal(PhysicalTackleOutcome.ControlledWrap, stationary.Outcome);
        var d = RagdollContactTests.Create(new(0, 3, -.4f), Vector3.Zero, physical: small);
        var c = RagdollContactTests.Create(new(0, 3, 0), new(0, 0, -6), physical: large);
        Vector3 before = d.Momentum + c.Momentum;
        Assert.True(RagdollContact.MaintainWrap(d, c, Ragdoll.FixedStep));
        Assert.True(c.Momentum.Z < 0); // Carrier drags lighter defender.
        Assert.True(d.Momentum.Z < 0);
        Assert.True((before - d.Momentum - c.Momentum).Length() < .001f);
        var far = RagdollContactTests.Create(new(10, 3, 0), Vector3.Zero, physical: large);
        Assert.False(RagdollContact.MaintainWrap(d, far, Ragdoll.FixedStep));
    }

    [Fact]
    public void DiveUsesAirborneMomentumAndImpactRegion()
    {
        var a = Attributes();
        TackleImpact Dive(float height, Vector3 velocity) =>
            TackleImpact.Calculate(a, a, velocity, Vector3.Zero, Vector3.Normalize(new(0,-1,1)), height, true);
        var low = Dive(.5f, new(0,-3,6));
        var torso = Dive(1.1f, new(0,-3,6));
        var high = Dive(1.8f, new(0,-3,6));
        Assert.True(low.AngularScale > torso.AngularScale);
        Assert.True(high.AngularScale > torso.AngularScale);
        Assert.True(low.ImpactScore > Dive(.5f, new(0,0,6)).ImpactScore);
        Assert.Equal(0, Dive(3, new(0,-3,6)).ImpactScore);
    }

    [Fact]
    public void MassNormalizedDriveProducesSameAccelerationAcrossWeightRange()
    {
        var light = RagdollContactTests.Create(new(0,10,0), Vector3.Zero, physical:Attributes(70));
        var heavy = RagdollContactTests.Create(new(0,10,0), Vector3.Zero, physical:Attributes(160));
        foreach (var doll in new[] {light,heavy})
        {
            var targets = doll.Joints.Select(j => j.ReferenceRotation *
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, j.Child == 7 ? .3f : 0)).ToArray();
            doll.StartActiveDrive(_ => targets);
            doll.ApplyImpulse(new(1, Vector3.UnitX * doll.Bodies[1].Mass));
        }
        for (int i=0; i<30; i++) { light.Update(Ragdoll.FixedStep); heavy.Update(Ragdoll.FixedStep); }
        Assert.True(Vector3.Distance(light.Bodies[0].Position, heavy.Bodies[0].Position) < .005f);
        Assert.True(Vector3.Distance(light.Bodies[7].AngularVelocity, heavy.Bodies[7].AngularVelocity) < .05f);
    }

    [Theory]
    [InlineData(70,160,1)]
    [InlineData(160,70,100)]
    [InlineData(160,160,100)]
    public void ContactImpulseAndEnergyStayBounded(float dm, float cm, int strength)
    {
        var d = RagdollContactTests.Create(new(0,10,-.4f), new(0,0,100), physical:Attributes(dm,strength));
        var c = RagdollContactTests.Create(new(0,10,0), new(0,0,-10), physical:Attributes(cm,101-strength));
        var before = d.Momentum + c.Momentum;
        float Energy(Ragdoll r) => r.Bodies.Sum(b => .5f*b.Mass*b.LinearVelocity.LengthSquared());
        float energy = Energy(d)+Energy(c);
        var impact = TackleImpact.Calculate(d.Physical!,c.Physical!,new(0,0,100),new(0,0,-10),Vector3.UnitZ,1,true);
        RagdollContact.Resolve(d,c,true,impact);
        Assert.True((d.Momentum+c.Momentum-before).Length()<.02f);
        Assert.True(Energy(d)+Energy(c)<=energy+.02f);
        Assert.InRange(c.LastContactImpulse,0,1200*Math.Min(d.Physical!.ImpulseLimitScale,c.Physical!.ImpulseLimitScale)+.01f);
        RagdollContact.Resolve(d,c);
        Assert.All(c.Bodies,b=>Assert.InRange(b.AngularVelocity.Length(),0,12.001f));
    }

    [Fact]
    public void NeutralStrengthReproducesBaselineActiveDrive()
    {
        var baseline = RagdollContactTests.Create(new(0,10,0), Vector3.Zero);
        var rated = RagdollContactTests.Create(new(0,10,0), Vector3.Zero, physical:Attributes(87));
        foreach (var doll in new[] { baseline, rated })
        {
            var targets = doll.Joints.Select(j => j.ReferenceRotation *
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, j.Child == 7 ? .3f : 0)).ToArray();
            doll.StartActiveDrive(_ => targets, 3);
        }
        for (int i=0; i<30; i++) { baseline.Update(Ragdoll.FixedStep); rated.Update(Ragdoll.FixedStep); }
        for (int i=0; i<11; i++)
        {
            Assert.Equal(baseline.MotorStrength(i),rated.MotorStrength(i));
            Assert.True(Vector3.Distance(baseline.Bodies[i].Position,rated.Bodies[i].Position)<.001f);
        }
    }

    [Fact]
    public void EveryPlayerKeepsOwnPhysicalAttributesAcrossReset()
    {
        var config = new TackleAlleyConfig { BallCarrierProfile = new() {Weight=75,Strength=20} };
        using var carrier = new BallCarrier(config);
        using var small = new Opponent(Vector3.Zero,config,new() {Height=1.65f,Weight=70,Strength=1});
        using var large = new Opponent(Vector3.One,config,new() {Height=2.05f,Weight=160,Strength=100});
        var cached = carrier.Physical;
        config.BallCarrierProfile = new();
        carrier.Reset(); small.Reset(); large.Reset();
        Assert.Same(cached,carrier.Physical);
        Assert.Same(cached,carrier.Ragdoll.Physical);
        Assert.Equal(75,carrier.Physical.TotalMass);
        Assert.Equal(70,small.Physical.TotalMass);
        Assert.Equal(160,large.Physical.TotalMass);
        Assert.True(large.Physical.WrapReach>small.Physical.WrapReach);
        Assert.True(large.Physical.ActiveMotorMultiplier>small.Physical.ActiveMotorMultiplier);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void InvalidStrengthIsRejected(int strength) =>
        Assert.Throws<ArgumentException>(() => Attributes(strength:strength));

    [Fact]
    public void InvalidDerivedValuesAndNonIntegerRatingsAreRejected()
    {
        Assert.Equal(50,JsonSerializer.Deserialize<PlayerProfile>("{}")!.Strength);
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<PlayerProfile>("{\"Strength\":50.5}"));
        foreach (float value in new[]{float.NaN,float.PositiveInfinity,0,-1})
        {
            Assert.Throws<ArgumentException>(()=>Attributes(config:new(){RagdollChestMass=value}));
            Assert.Throws<ArgumentException>(()=>Attributes(config:new(){ContactTorsoRadius=value}));
        }
        Assert.Throws<ArgumentException>(()=>Attributes(config:new(){RagdollChestMass=float.MaxValue,RagdollPelvisMass=float.MaxValue}));
    }
}
