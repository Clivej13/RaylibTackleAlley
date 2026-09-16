using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class RagdollContactTests
{
    internal static Ragdoll Create(Vector3 position, Vector3 velocity)
    {
        var points = new Dictionary<string, Vector3> {
            ["Hips"] = new(0, 1, 0), ["Chest"] = new(0, 1.4f, 0),
            ["Neck"] = new(0, 1.7f, 0), ["Head"] = new(0, 1.8f, 0)
        };
        foreach (var (side, x) in new[] { ("L", .25f), ("R", -.25f) })
        {
            points["UpperArm." + side] = new(x, 1.6f, 0);
            points["LowerArm." + side] = new(x * 2, 1.45f, 0);
            points["Hand." + side] = new(x * 3, 1.3f, 0);
            points["UpperLeg." + side] = new(x * .5f, 1, 0);
            points["LowerLeg." + side] = new(x * .5f, .55f, .05f);
            points["Foot." + side] = new(x * .5f, .1f, 0);
        }
        var pose = points.ToDictionary(p => p.Key, p => Matrix4x4.CreateTranslation(p.Value + position));
        var doll = new Ragdoll();
        doll.Activate(pose, pose, velocity);
        return doll;
    }

    private static Vector3 Momentum(Ragdoll doll) =>
        doll.Bodies.Aggregate(Vector3.Zero, (p, b) => p + b.Mass * b.LinearVelocity);
    private static float Energy(Ragdoll doll) => doll.Bodies.Sum(b => .5f * b.Mass * b.LinearVelocity.LengthSquared());

    [Fact]
    public void HeadOnPushesCarrierBackwardAndBothCharactersReceiveOppositeMomentum()
    {
        var defender = Create(new(0, 3, -.65f), new(0, 0, 6));
        var carrier = Create(new(0, 3, 0), new(0, 0, -2));
        Vector3 beforeD = Momentum(defender), beforeC = Momentum(carrier);
        RagdollContact.Resolve(defender, carrier);
        Vector3 push = Momentum(carrier) - beforeC;
        Assert.True(push.Z > 0);
        Assert.True(Math.Abs(push.X) + Math.Abs(push.Y) < push.Z * .05f);
        Assert.True((Momentum(defender) - beforeD + push).Length() < .001f);
        Assert.True(Energy(defender) + Energy(carrier) < 2000);
        Assert.All(carrier.Bodies.Skip(2), b => Assert.Equal(new Vector3(0, 0, -2), b.LinearVelocity));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void SideAndAngledHitsFollowContactGeometry(float side)
    {
        // Same forward velocity for both sides: the normal must choose the lateral reaction.
        var defender = Create(new(side * .4f, 3, -.5f), new(0, 0, 6));
        var carrier = Create(new(0, 3, 0), Vector3.Zero);
        RagdollContact.Resolve(defender, carrier);
        Vector3 push = Momentum(carrier);
        Assert.True(push.X * side < -1);
        Assert.True(push.Z > 1);
        Assert.True(Math.Abs(push.X) > push.Z * .25f);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void PureSideHitPushesSideways(float side)
    {
        var defender = Create(new(side * .65f, 3, 0), new(-side * 6, 0, 0));
        var carrier = Create(new(0, 3, 0), Vector3.Zero);
        RagdollContact.Resolve(defender, carrier);
        Assert.True(Momentum(carrier).X * side < -1);
        Assert.True(Math.Abs(Momentum(carrier).Z) < .001f);
    }

    [Fact]
    public void ClosingSpeedControlsImpulseAndSeparatingContactDoesNotPull()
    {
        float Push(float speed)
        {
            var defender = Create(new(0, 3, -.65f), new(0, 0, speed));
            var carrier = Create(new(0, 3, 0), Vector3.Zero);
            RagdollContact.Resolve(defender, carrier);
            return Momentum(carrier).Z;
        }
        Assert.True(Push(6) > Push(2) * 2);
        Assert.Equal(0, Push(-6));
        Assert.Equal(0, Push(0));
        Assert.InRange(Push(1000), 0, RagdollContact.MaximumImpulse + .001f);
    }

    [Fact]
    public void ShoulderContactProducesMirroredAngularResponse()
    {
        Vector3 Spin(float side)
        {
            var defender = Create(new(side * .4f, 3, -.5f), new(0, 0, 6));
            var carrier = Create(new(0, 3, 0), Vector3.Zero);
            RagdollContact.Resolve(defender, carrier);
            Assert.True(carrier.Bodies[0].AngularVelocity.Length() > .01f);
            return carrier.Bodies[1].AngularVelocity;
        }
        Vector3 left = Spin(-1), right = Spin(1);
        Assert.True(Math.Abs(left.Y) > .01f);
        Assert.True(left.Y * right.Y < 0);
        Assert.All(new[] { left, right }, w => Assert.InRange(w.Length(), 0, Ragdoll.MaximumAngularSpeed + .001f));
    }

    [Fact]
    public void OverlapCorrectionPreservesJointsAndDoesNotGenerateVelocity()
    {
        var defender = Create(new(0, 3, 0), Vector3.Zero);
        var carrier = Create(new(0, 3, 0), Vector3.Zero);
        RagdollContact.Resolve(defender, carrier);
        AssertClear(defender, carrier);
        foreach (var doll in new[] { defender, carrier })
        {
            Assert.Equal(Vector3.Zero, Momentum(doll));
            Assert.All(doll.Joints, j => Assert.InRange(doll.JointSeparation(j), 0, .00001f));
        }
    }

    [Fact]
    public void MovingPairStaysSeparatedGroundedAndEventuallySettles()
    {
        var defender = Create(new(0, 1, -.4f), new(0, 0, 10));
        var carrier = Create(new(0, 1, 0), new(0, 0, -3));
        float initialEnergy = Energy(defender) + Energy(carrier);
        RagdollContact.Resolve(defender, carrier, true);
        Assert.True(Energy(defender) + Energy(carrier) <= initialEnergy + .01f);
        for (int i = 0; i < 3600; i++)
        {
            RagdollContact.Resolve(defender, carrier);
            defender.Update(Ragdoll.FixedStep); carrier.Update(Ragdoll.FixedStep);
            RagdollContact.Resolve(defender, carrier);
            AssertClear(defender, carrier);
            foreach (var doll in new[] { defender, carrier })
                Assert.All(doll.Bodies, b => {
                    Assert.True(float.IsFinite(b.Position.LengthSquared()));
                    Assert.True(b.Bottom >= -.0001f);
                    Assert.InRange(b.LinearVelocity.Length(), 0, 25);
                    Assert.InRange(b.AngularVelocity.Length(), 0, Ragdoll.MaximumAngularSpeed + .001f);
                });
        }
        Assert.True(carrier.HasMeaningfulGroundContact);
        Assert.Equal(RagdollState.Settled, carrier.State);
        Assert.Equal(RagdollState.Settled, defender.State);
    }

    [Fact]
    public void ConfirmedDistanceContactUsesNearestGeometryOnlyOnce()
    {
        var defender = Create(new(-2, 3, 0), new(6, 0, 0));
        var carrier = Create(new(0, 3, 0), Vector3.Zero);
        RagdollContact.Resolve(defender, carrier);
        Assert.Equal(Vector3.Zero, Momentum(carrier));
        RagdollContact.Resolve(defender, carrier, true);
        Assert.True(Momentum(carrier).X > 0);
        var momentum = Momentum(carrier);
        RagdollContact.Resolve(defender, carrier);
        Assert.Equal(momentum, Momentum(carrier));
        carrier.Deactivate();
        RagdollContact.Resolve(defender, carrier);
        Assert.Equal(RagdollState.Inactive, carrier.State);
    }

    [Theory]
    [InlineData(10f, -4f, 3f)] // Defender wins: carrier actually reverses.
    [InlineData(4f, -10f, -3f)] // Carrier wins: defender is carried backwards.
    [InlineData(6f, -6f, 0f)] // Equal opposing momentum cancels.
    [InlineData(10f, 4f, 7f)] // Rear tackle preserves their shared forward momentum.
    public void ConfirmedTackleGivesBothCharactersResultantMomentum(float dv, float cv, float expected)
    {
        var defender = Create(new(0, 10, -.65f), new(0, 0, dv));
        var carrier = Create(new(0, 10, 0), new(0, 0, cv));
        Vector3 before = Momentum(defender) + Momentum(carrier);
        RagdollContact.Resolve(defender, carrier, true);
        foreach (var doll in new[] { defender, carrier })
            Assert.All(doll.Bodies, body => Assert.Equal(expected, body.LinearVelocity.Z, 4));
        Assert.True((Momentum(defender) + Momentum(carrier) - before).Length() < .001f);
        if (expected == 0) return;
        Vector3 startD = Centre(defender), startC = Centre(carrier);
        for (int i = 0; i < 30; i++)
        {
            RagdollContact.Resolve(defender, carrier);
            defender.Update(Ragdoll.FixedStep); carrier.Update(Ragdoll.FixedStep);
            RagdollContact.Resolve(defender, carrier);
        }
        // Assert actual travel after joint solving, not merely a chest impulse or lean.
        Assert.True((Centre(defender).Z - startD.Z) * expected > .1f);
        Assert.True((Centre(carrier).Z - startC.Z) * expected > .1f);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void ConfirmedSideHitCombinesLateralAndForwardMomentum(float side)
    {
        var defender = Create(new(side * .65f, 10, 0), new(-side * 8, 0, 0));
        var carrier = Create(new(0, 10, 0), new(0, 0, -6));
        RagdollContact.Resolve(defender, carrier, true);
        foreach (var doll in new[] { defender, carrier })
            Assert.All(doll.Bodies, body =>
                Assert.True(Vector3.Distance(new(-side * 4, 0, -3), body.LinearVelocity) < .001f));
        Assert.True(Math.Abs(carrier.Bodies[1].AngularVelocity.Y) > .01f);
    }

    [Fact]
    public void ConfirmedTackleIncludesAllBodyMomentumAndBoundsTotalEnergy()
    {
        var defender = Create(new(-.4f, 10, -.5f), new(0, 0, 10));
        var carrier = Create(new(0, 10, 0), new(0, 0, -4));
        defender.ApplyImpulse(new(4, new(12, 0, 0)));
        Vector3 expectedMomentum = Momentum(defender) + Momentum(carrier);
        float beforeEnergy = TotalEnergy(defender) + TotalEnergy(carrier);
        RagdollContact.Resolve(defender, carrier, true);
        Assert.True((Momentum(defender) + Momentum(carrier) - expectedMomentum).Length() < .001f);
        Assert.True(Vector3.Distance(Momentum(defender) / defender.Bodies.Sum(b => b.Mass),
            Momentum(carrier) / carrier.Bodies.Sum(b => b.Mass)) < .001f);
        Assert.True(TotalEnergy(defender) + TotalEnergy(carrier) <= beforeEnergy + .001f);
    }

    [Fact]
    public void ConfirmedImpulseIsCappedAndSeparatingBodiesAreNotCaptured()
    {
        var defender = Create(new(0, 10, -.65f), new(0, 0, 1000));
        var carrier = Create(new(0, 10, 0), Vector3.Zero);
        RagdollContact.Resolve(defender, carrier, true);
        Assert.InRange(Momentum(carrier).Length(), 0, RagdollContact.MaximumTackleImpulse + .01f);
        defender = Create(new(0, 10, -.65f), new(0, 0, -4));
        carrier = Create(new(0, 10, 0), new(0, 0, 4));
        Vector3 beforeD = Momentum(defender), beforeC = Momentum(carrier);
        RagdollContact.Resolve(defender, carrier, true);
        Assert.Equal(beforeD, Momentum(defender));
        Assert.Equal(beforeC, Momentum(carrier));
    }

    private static Vector3 Centre(Ragdoll doll) =>
        doll.Bodies.Aggregate(Vector3.Zero, (sum, b) => sum + b.Mass * b.Position) / doll.Bodies.Sum(b => b.Mass);

    private static float TotalEnergy(Ragdoll doll) => Energy(doll) + doll.Bodies.Sum(b =>
        .5f * b.Mass * (b.HalfSegment.LengthSquared() / 3 + .4f * b.Radius * b.Radius) *
        b.AngularVelocity.LengthSquared());

    private static void AssertClear(Ragdoll a, Ragdoll b)
    {
        foreach (int i in new[] { 0, 1, 3, 4, 5, 6, 7, 8, 9, 10 })
        foreach (int j in new[] { 0, 1, 3, 4, 5, 6, 7, 8, 9, 10 })
            Assert.True(RagdollContact.SurfaceGap(a.Bodies[i], b.Bodies[j]) >=
                -RagdollContact.PenetrationSlop - .002f, $"Bodies {i}/{j} interpenetrated");
    }
}
