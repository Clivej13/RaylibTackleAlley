using System.Numerics;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class RagdollTests
{
    private static Dictionary<string, Matrix4x4> Pose(float height = 0)
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
        return points.ToDictionary(p => p.Key, p => Matrix4x4.CreateTranslation(p.Value + Vector3.UnitY * height));
    }

    private static Ragdoll Create(float height = 0, Vector3 velocity = default)
    {
        var doll = new Ragdoll();
        doll.Activate(Pose(height), Pose(height), velocity);
        return doll;
    }

    [Theory]
    [InlineData("Hips", true)]
    [InlineData("Chest", true)]
    [InlineData("LowerArm.L", true)]
    [InlineData("LowerArm.R", true)]
    [InlineData("LowerLeg.L", true)]
    [InlineData("LowerLeg.R", true)]
    [InlineData("Head", false)]
    [InlineData("UpperArm.L", false)]
    public void OnlyDownBodyRegionsCountAsGroundContact(string bone, bool expected)
    {
        var doll = Create(5);
        var body = doll.Bodies.Single(b => b.Bone == bone);
        // Place this capsule horizontally on the floor, isolated from other parts.
        typeof(RagdollBody).GetProperty(nameof(RagdollBody.Orientation))!.SetValue(body,
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
        float bottom = body.Bottom;
        typeof(RagdollBody).GetProperty(nameof(RagdollBody.Position))!.SetValue(body,
            body.Position - Vector3.UnitY * bottom);
        Assert.Equal(expected, doll.IsDownOnGround());
    }

    [Theory]
    [InlineData("LowerLeg.L")]
    [InlineData("LowerLeg.R")]
    [InlineData("LowerArm.L")]
    [InlineData("LowerArm.R")]
    public void FootOrHandEndAloneDoesNotCount(string bone)
    {
        var doll = Create(5);
        var body = doll.Bodies.Single(b => b.Bone == bone);
        typeof(RagdollBody).GetProperty(nameof(RagdollBody.Position))!.SetValue(body,
            body.Position - Vector3.UnitY * body.Bottom);
        Assert.False(doll.IsDownOnGround());
    }

    [Fact]
    public void ActivationCopiesExistingPoseAndVelocityWithoutProjection()
    {
        var pose = Pose(3);
        var world = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(4, 0, -2);
        foreach (string key in pose.Keys.ToArray()) pose[key] *= world;
        var velocity = new Vector3(3, 2, -4);
        var doll = new Ragdoll();
        doll.Activate(pose, Pose(), velocity);
        Assert.Equal(11, doll.Bodies.Count); Assert.Equal(10, doll.Joints.Count);
        Assert.Equal((pose["Hips"].Translation + pose["Chest"].Translation) / 2, doll.Bodies[0].Position);
        Assert.All(doll.Bodies, b => {
            Assert.Equal(velocity, b.LinearVelocity);
            Assert.Equal(Vector3.Zero, b.AngularVelocity);
            Assert.True(Math.Abs(Quaternion.Dot(b.Orientation, Quaternion.CreateFromAxisAngle(Vector3.UnitY, .7f))) > .99999f);
        });
        Vector3 saved = doll.Bodies[0].Position;
        pose["Hips"] = Matrix4x4.Identity;
        Assert.Equal(saved, doll.Bodies[0].Position);
        Assert.All(doll.Joints, j => Assert.InRange(doll.JointSeparation(j), 0, .00001f));
    }

    [Fact]
    public void GravityAndFixedStepAccumulator()
    {
        var a = Create(10); var b = Create(10);
        float y = a.Bodies[0].Position.Y;
        a.Update(Ragdoll.FixedStep / 2);
        Assert.Equal(y, a.Bodies[0].Position.Y);
        a.Update(Ragdoll.FixedStep / 2); b.Update(Ragdoll.FixedStep);
        Assert.True(a.Bodies[0].Position.Y < y);
        Assert.True(a.Bodies[0].LinearVelocity.Y < 0);
        Assert.Equal(a.Bodies[0].Position, b.Bodies[0].Position);
        for (int i = 0; i < 60; i++) a.Update(1f / 60);
        for (int i = 0; i < 120; i++) b.Update(1f / 120);
        Assert.Equal(a.Bodies.Select(x => x.Position), b.Bodies.Select(x => x.Position));
    }

    [Fact]
    public void ImpulseUsesMassAndPointProducesBoundedAngularVelocity()
    {
        var doll = Create(3, new(1, 2, 3));
        var body = doll.Bodies[1];
        doll.ApplyImpulse(new(1, new(25, 50, -75), body.Position + Vector3.UnitX));
        Assert.Equal(new Vector3(2, 4, 0), body.LinearVelocity);
        Assert.InRange(body.AngularVelocity.Length(), .01f, new TackleAlleyConfig().RagdollMaximumAngularSpeed + .0001f);
        Assert.Equal(new Vector3(1, 2, 3), doll.Bodies[0].LinearVelocity);
        var activated = new Ragdoll();
        activated.Activate(Pose(3), Pose(3), new(1, 2, 3), new(1, new(25, 50, -75)));
        Assert.Equal(body.LinearVelocity, activated.Bodies[1].LinearVelocity);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(4f)]
    public void GroundAndJointLimitsRemainStableAndEventuallySettle(float height)
    {
        var doll = Create(height, new(2, 0, -3));
        doll.ApplyImpulse(new(1, new(80, 60, -90), doll.Bodies[1].Position + Vector3.UnitX * .2f));
        bool settling = false;
        for (int step = 0; step < 3600; step++)
        {
            doll.Update(Ragdoll.FixedStep);
            settling |= doll.State == RagdollState.Settling;
            Assert.All(doll.Bodies, b => {
                Assert.True(float.IsFinite(b.Position.LengthSquared()));
                Assert.True(b.Bottom >= -.00001f, $"Ground penetration: {b.Bottom}");
                Assert.InRange(b.AngularVelocity.Length(), 0, new TackleAlleyConfig().RagdollMaximumAngularSpeed + .001f);
            });
            Assert.All(doll.Joints, j => {
                Vector3 a = doll.JointAngles(j);
                Assert.InRange(a.X, j.MinimumAngles.X - .001f, j.MaximumAngles.X + .001f);
                Assert.InRange(a.Y, j.MinimumAngles.Y - .001f, j.MaximumAngles.Y + .001f);
                Assert.InRange(a.Z, j.MinimumAngles.Z - .001f, j.MaximumAngles.Z + .001f);
                Assert.True(doll.JointSeparation(j) < .08f, $"Detached {j.Child}: {doll.JointSeparation(j)}");
            });
        }
        Assert.True(settling);
        Assert.Equal(RagdollState.Settled, doll.State);
        var positions = doll.Bodies.Select(b => b.Position).ToArray();
        doll.Update(1);
        Assert.Equal(positions, doll.Bodies.Select(b => b.Position));
        doll.ApplyImpulse(new(0, new(0, 50, 0)));
        Assert.Equal(RagdollState.Active, doll.State);
    }

    [Fact]
    public void DeactivationAndReactivationClearMotionTimersAndPose()
    {
        var doll = Create(2);
        doll.Update(.2f);
        doll.Deactivate();
        Assert.Equal(RagdollState.Inactive, doll.State);
        var positions = doll.Bodies.Select(b => b.Position).ToArray();
        doll.Update(.2f);
        Assert.Equal(positions, doll.Bodies.Select(b => b.Position));
        Assert.All(doll.Bodies, b => Assert.Equal(Vector3.Zero, b.LinearVelocity + b.AngularVelocity));
        doll.Activate(Pose(5), Pose(5), Vector3.One);
        Assert.Equal(RagdollState.Active, doll.State);
        Assert.All(doll.Bodies, b => Assert.Equal(Vector3.One, b.LinearVelocity));
        doll.Update(Ragdoll.FixedStep / 2);
        Assert.Equal(6.2f, doll.Bodies[0].Position.Y, 5);
    }

    [Fact]
    public void DebugSelectionAndImpulseFollowNearestEligibleDefenderFacing()
    {
        var far = new Opponent(new(20, 0, 0), new());
        var near = new Opponent(new(2, 0, 0), new());
        Assert.Same(near, RagdollDebugControls.Nearest([far, near], Vector3.Zero));
        near.Ragdoll.Activate(Pose(), Pose(), Vector3.Zero);
        Assert.Same(far, RagdollDebugControls.Nearest([far, near], Vector3.Zero));
        Assert.Equal(new Vector3(0, 120, -180), RagdollDebugControls.TestImpulse(0));
        Assert.True(Vector3.Distance(new(0, 120, 180), RagdollDebugControls.TestImpulse(180)) < .001f);
        var state = near.State;
        near.Update(Vector3.Zero, .1f, true, Vector2.One, true);
        Assert.Equal(state, near.State);
        near.Reset();
        Assert.Equal(RagdollState.Inactive, near.Ragdoll.State);
    }
}
