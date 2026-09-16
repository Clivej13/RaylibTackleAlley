using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class ActiveRagdollTests
{
    private static Ragdoll Create(float height = 10) => RagdollContactTests.Create(new(0, height, 0), Vector3.Zero);
    private static Func<float, IReadOnlyList<Quaternion>> Gait(Ragdoll doll)
    {
        var rest = doll.Joints.Select(j => Quaternion.Normalize(
            Quaternion.Inverse(doll.Bodies[j.Parent].Orientation) * doll.Bodies[j.Child].Orientation)).ToArray();
        return time => doll.Joints.Select((j, i) =>
        {
            float wave = MathF.Sin(time * 14 + (j.Child >= 9 ? MathF.PI : 0));
            float angle = j.Child is 7 or 9 ? .65f * wave : j.Child is 8 or 10 ? .5f + .45f * wave : 0;
            return Quaternion.Normalize(rest[i] * Quaternion.CreateFromAxisAngle(Vector3.UnitX, angle));
        }).ToArray();
    }

    [Theory]
    [InlineData(7, 8, 1, 9)]
    [InlineData(10, 9, 1, 7)]
    [InlineData(3, 4, 1, 5)]
    [InlineData(6, 5, 1, 3)]
    public void HitLimbYieldsWhileOtherLimbsAndTorsoKeepStrength(int hit, int partner, int torso, int other)
    {
        var doll = Create();
        doll.StartActiveDrive(Gait(doll), hit);
        Assert.Equal(hit, doll.LastHitBody);
        Assert.Equal(Ragdoll.HitLimbMotorStrength, doll.MotorStrength(hit));
        Assert.Equal(Ragdoll.HitLimbMotorStrength, doll.MotorStrength(partner));
        Assert.Equal(1, doll.MotorStrength(torso));
        Assert.Equal(1, doll.MotorStrength(other));
    }

    [Fact]
    public void TorsoHitLeavesLegsAnimatedDuringFallWithoutOverwritingMomentum()
    {
        var doll = Create();
        doll.StartActiveDrive(Gait(doll), 1);
        Assert.Equal(Ragdoll.HitTorsoMotorStrength, doll.MotorStrength(1));
        Assert.Equal(1, doll.MotorStrength(7));
        var before = doll.Bodies.Select(b => b.Position).ToArray();
        doll.Update(0);
        Assert.Equal(before, doll.Bodies.Select(b => b.Position));
        doll.ApplyImpulse(new(0, new(90, 0, 0)));
        float maxLegMotion = 0;
        for (int i = 0; i < 60; i++)
        {
            doll.Update(Ragdoll.FixedStep);
            maxLegMotion = Math.Max(maxLegMotion, Math.Abs(doll.JointAngles(doll.Joints.Single(j => j.Child == 7)).X));
        }
        Assert.True(maxLegMotion > .05f, $"Leg drive was not visible: {maxLegMotion}");
        Assert.True(doll.Bodies[0].Position.Y < before[0].Y - .5f); // no animation levitation
        Assert.True(doll.Bodies.Sum(b => b.LinearVelocity.X * b.Mass) > 1);
        Assert.True(doll.IsActivelyDriven);
    }

    [Fact]
    public void StruckLegPhysicallyFollowsGaitLessThanUnstruckLeg()
    {
        var strong = Create(); var weak = Create();
        strong.StartActiveDrive(Gait(strong), 1);
        weak.StartActiveDrive(Gait(weak), 7);
        float strongMotion = 0, weakMotion = 0;
        for (int i = 0; i < 50; i++)
        {
            strong.Update(Ragdoll.FixedStep); weak.Update(Ragdoll.FixedStep);
            strongMotion += Math.Abs(strong.JointAngles(strong.Joints.Single(j => j.Child == 7)).X);
            weakMotion += Math.Abs(weak.JointAngles(weak.Joints.Single(j => j.Child == 7)).X);
        }
        Assert.True(strongMotion > weakMotion * 1.25f, $"Strong {strongMotion}, struck {weakMotion}");
    }

    [Fact]
    public void DriveUsesFixedStepsFadesAndClearsOnReset()
    {
        var a = Create(); var b = Create();
        a.StartActiveDrive(Gait(a)); b.StartActiveDrive(Gait(b));
        for (int i = 0; i < 30; i++) a.Update(1f / 60);
        for (int i = 0; i < 60; i++) b.Update(Ragdoll.FixedStep);
        Assert.Equal(a.Bodies.Select(x => x.Position), b.Bodies.Select(x => x.Position));
        float peak = a.ActiveDriveWeight;
        for (int i = 0; i < 65; i++) a.Update(Ragdoll.FixedStep);
        Assert.True(a.ActiveDriveWeight < peak);
        for (int i = 0; i < 30; i++) a.Update(Ragdoll.FixedStep);
        Assert.False(a.IsActivelyDriven);
        b.Deactivate();
        Assert.False(b.IsActivelyDriven);
        Assert.Null(b.LastHitBody);
    }

    [Fact]
    public void GroundContactEndsDriveAndRecoveryCanSettle()
    {
        var doll = Create(0);
        doll.StartActiveDrive(Gait(doll), 1);
        doll.ApplyImpulse(new(1, new(40, -40, 70), doll.Bodies[1].Position + Vector3.UnitX * .2f));
        bool driven = false;
        for (int i = 0; i < 3600; i++)
        {
            doll.Update(Ragdoll.FixedStep);
            driven |= doll.ActiveDriveWeight > 0;
            Assert.All(doll.Bodies, body =>
            {
                Assert.True(float.IsFinite(body.Position.LengthSquared()));
                Assert.True(body.Bottom >= -.0001f);
                Assert.InRange(body.AngularVelocity.Length(), 0, Ragdoll.MaximumAngularSpeed + .001f);
            });
            if (doll.HasMeaningfulGroundContact) Assert.False(doll.IsActivelyDriven);
            Assert.All(doll.Joints, j => Assert.InRange(doll.JointSeparation(j), 0, .08f));
        }
        Assert.True(driven);
        Assert.True(doll.HasMeaningfulGroundContact);
        Assert.Equal(RagdollState.Settled, doll.State);
    }

    [Fact]
    public void ArmOnlyCollisionTransfersMomentumLocallyAndWeakensThatArm()
    {
        var a = RagdollContactTests.Create(new(-1.2f, 10, 0), new(5, 0, 0));
        var b = Create();
        b.StartActiveDrive(Gait(b));
        float incoming = a.Bodies.Sum(x => x.Mass * x.LinearVelocity.X);
        var hit = RagdollContact.Resolve(a, b);
        Assert.NotNull(hit);
        Assert.InRange(hit.Value.DefenderBody, 3, 6);
        Assert.InRange(hit.Value.CarrierBody, 3, 6);
        Assert.Equal(Vector3.Zero, b.Bodies[0].LinearVelocity);
        Assert.Equal(Vector3.Zero, b.Bodies[1].LinearVelocity);
        Assert.Contains(b.Bodies.Skip(3).Take(4), x => x.LinearVelocity.Length() > .01f);
        Assert.InRange(b.LastHitBody!.Value, 3, 6);
        Assert.Equal(1, b.MotorStrength(7));
        Assert.True(Math.Abs(a.Bodies.Sum(x => x.Mass * x.LinearVelocity.X) +
            b.Bodies.Sum(x => x.Mass * x.LinearVelocity.X) - incoming) < .001f);
    }

    [Fact]
    public void LowTackleSelectsLegContactInsteadOfInventingTorsoHit()
    {
        var a = RagdollContactTests.Create(new(0, 9, -.2f), new(0, 0, 6));
        var b = Create();
        var hit = RagdollContact.Resolve(a, b, true);
        Assert.NotNull(hit);
        Assert.InRange(hit.Value.CarrierBody, 7, 10);
        b.StartActiveDrive(Gait(b), hit.Value.CarrierBody);
        Assert.Equal(1, b.MotorStrength(1));
        Assert.Equal(Ragdoll.HitLimbMotorStrength, b.MotorStrength(hit.Value.CarrierBody));
        Assert.True(b.Bodies[hit.Value.CarrierBody].AngularVelocity.Length() > .01f);
    }

    [Fact]
    public void SecondaryImpactWeakensOnlyHitRegionAndDoesNotRestartDrive()
    {
        var doll = Create();
        doll.StartActiveDrive(Gait(doll), 7);
        for (int i = 0; i < 110; i++) doll.Update(Ragdoll.FixedStep);
        doll.ReactToContact(3, .1f);
        Assert.Equal(1, doll.MotorStrength(3)); // ignore resting contact jitter
        doll.ReactToContact(3, 4);
        Assert.Equal(Ragdoll.HitLimbMotorStrength, doll.MotorStrength(3));
        Assert.Equal(1, doll.MotorStrength(9));
        for (int i = 0; i < 50; i++) doll.Update(Ragdoll.FixedStep);
        Assert.False(doll.IsActivelyDriven);
    }
}
