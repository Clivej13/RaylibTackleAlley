using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

// Part 3 replaces the former automatic lunge recovery with persistent physics ownership.
public sealed class DefenderRecoveryTests : IDisposable
{
    private readonly AssetManager _assets;
    public DefenderRecoveryTests()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Lunge ragdoll transition");
        _assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        _assets.RequireAssets(Opponent.AnimationAssetKeys);
        while (!_assets.ProcessNext()) { }
    }
    public void Dispose() { _assets.UnloadAll(); Raylib.CloseWindow(); }
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static AnimationPlayer Animation(Opponent d) => Field<AnimationPlayer>(d, "_animation");
    private static float Duration(Opponent d) => d.State == DefenderState.LungeTackle
        ? .6f : (Animation(d).FrameCount - 1) / Animation(d).FramesPerSecond;

    private Opponent Lunge(float side = 0, float speed = 6.5f, bool visual = true, DefenderProfile? behaviorProfile = null)
    {
        var d = new Opponent(new(5, 0, -7), new() { OpponentJogSpeed = speed }, behaviorProfile: behaviorProfile);
        if (visual) d.InitializeVisual(_assets);
        d.Update(d.Position + new Vector3(0, 0, -30), .01f, false, Vector2.Zero, false);
        // Commit from ready so Left/Right are chosen from the same locked facing.
        d.Update(d.Position + new Vector3(0, 0, -4), 0, true, Vector2.Zero, false);
        d.Update(d.Position + new Vector3(side, 0, -1.8f), 0);
        Assert.Equal(DefenderState.LungeTackle, d.State);
        return d;
    }

    [Theory]
    [InlineData(-.8f, 4f)]
    [InlineData(0f, 6.5f)]
    [InlineData(.8f, 9f)]
    public void FinalLungeFramePreservesPoseAndActualCommittedVelocity(float side, float speed)
    {
        var d = Lunge(side, speed);
        var clip = Animation(d);
        float duration = Duration(d), yaw = d.FacingYawDegrees;
        Vector3 launchPosition = d.Position;
        Vector3 direction = Vector3.Normalize(new Vector3(side, 0, -1.8f));
        d.Update(new(90, 0, 90), duration - .01f, false, Vector2.One, true);
        Assert.False(d.Ragdoll.IsActive);
        Assert.Same(clip, Animation(d));
        d.Update(new(-90, 0, 90), .01f, true, -Vector2.One, true);
        Assert.True(d.Ragdoll.IsActive);
        Assert.Equal(RagdollState.Active, d.Ragdoll.State);
        Assert.Equal(duration, clip.CurrentTime, 5);
        Assert.Equal(yaw, d.FacingYawDegrees);
        Assert.True(Vector3.Distance(new(d.Position.X, 0, d.Position.Z),
            new Vector3(launchPosition.X, 0, launchPosition.Z) + direction * speed * duration) < .0001f);
        Vector3 expectedVelocity = direction * speed;
        Assert.True(d.IsGrounded);
        Assert.All(d.Ragdoll.Bodies, body => Assert.True(Vector3.Distance(expectedVelocity, body.LinearVelocity) < .0001f));
        var bridge = Field<RagdollSkeleton>(d, "_ragdollSkeleton");
        var model = Field<ModelInstance>(d, "_model").Model;
        unsafe {
            for (int i = 0; i < model.Skeleton.BoneCount; i++)
            {
                var animated = RagdollPose.Matrix(clip.Animation.KeyframePoses[(int)clip.CurrentFrame][i]);
                foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                    Assert.True(Vector3.Distance(Vector3.Transform(point, animated),
                        Vector3.Transform(point, bridge.ModelPose[i])) < .0001f);
            }
        }
        Vector3 torso = Vector3.Normalize(d.Ragdoll.Bodies[1].Position - d.Ragdoll.Bodies[0].Position);
        Assert.True(torso.Y < .7f, $"Dive handed off upright: torso {torso}, frame {clip.CurrentFrame}/{clip.FrameCount}");
        AssertSkinnedPoseSurvivesHandoff(d, bridge, model);
        var start = CentreOfMass(d);
        for (int i = 0; i < 12; i++) d.Update(new(100, 0, 100), Ragdoll.FixedStep);
        Assert.True(Vector3.Dot(CentreOfMass(d) - start, direction) > .1f);
    }

    private static unsafe void AssertSkinnedPoseSurvivesHandoff(Opponent d, RagdollSkeleton bridge, Model model)
    {
        var before = new List<Vector3>();
        for (int m = 0; m < model.MeshCount; m++)
        {
            var mesh = model.Meshes[m];
            for (int v = 0; v < mesh.VertexCount; v++)
                before.Add(new(mesh.AnimVertices[v * 3], mesh.AnimVertices[v * 3 + 1], mesh.AnimVertices[v * 3 + 2]));
        }
        bridge.Apply(model, d.Ragdoll);
        int index = 0;
        for (int m = 0; m < model.MeshCount; m++)
        {
            var mesh = model.Meshes[m];
            for (int v = 0; v < mesh.VertexCount; v++)
                Assert.True(Vector3.Distance(before[index++], new(mesh.AnimVertices[v * 3],
                    mesh.AnimVertices[v * 3 + 1], mesh.AnimVertices[v * 3 + 2])) < .0001f,
                    "Rendered mesh changed at ragdoll handoff.");
        }
    }

    [Theory]
    [InlineData(-.8f)]
    [InlineData(0f)]
    [InlineData(.8f)]
    public void MissedDiveRecoversPromptlyAfterLanding(float side)
    {
        var d = Lunge(side);
        bool landed = false, recovering = false;
        float elapsed = 0;
        while (elapsed < 3f)
        {
            bool groundContact = d.Ragdoll.HasMeaningfulGroundContact;
            d.Update(new(100, 0, 100), Ragdoll.FixedStep);
            elapsed += Ragdoll.FixedStep;
            landed |= groundContact || d.Ragdoll.HasMeaningfulGroundContact;
            if (d.IsRecovering)
            {
                Assert.True(landed, "Recovery must wait for torso landing.");
                recovering = true;
            }
            if (recovering && d.State == DefenderState.Locomotion) break;
        }
        Assert.True(recovering);
        Assert.Equal(DefenderState.Locomotion, d.State);
        Assert.True(elapsed < 3f, $"Recovery took {elapsed}s.");
        Vector3 position = d.Position;
        d.Update(new(100, 0, 100), .1f);
        Assert.True(Vector3.Distance(position, d.Position) > 0);
        Assert.True(d.HasEngaged);
        Assert.Equal(OpponentPace.Sprint, d.Pace);
        Assert.Equal(new TackleAlleyConfig().OpponentSprintSpeed, d.TargetSpeed);
    }

    [Theory]
    [InlineData("balanced")]
    [InlineData("aggressive")]
    [InlineData("contain")]
    public void EveryBehaviorProfileFinishesTheSamePhysicalRecoveryBeforePursuit(string id)
    {
        var profiles = DefenderProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "defender-profiles.json"));
        using var d = Lunge(behaviorProfile: profiles.Resolve(id));
        Assert.Equal(DefenderAiState.LungeCommitment, d.AiState);
        d.Update(new(100, 0, 100), .6f);
        Assert.True(d.Ragdoll.IsActive);
        Assert.Equal(DefenderAiState.Recovery, d.AiState);
        bool recovered = false;
        for (int i = 0; i < 400; i++)
        {
            d.Update(new(100, 0, 100), .01f);
            if (d.Ragdoll.IsActive || d.IsRecovering)
                Assert.Equal(DefenderAiState.Recovery, d.AiState);
            else
            {
                Assert.Equal(DefenderAiState.Pursuit, d.AiState);
                recovered = true;
                break;
            }
        }
        Assert.True(recovered);
        Assert.Equal(id, d.BehaviorProfile.Id);
        d.Update(new(100, 0, 100), .1f);
        Assert.Equal(DefenderState.Locomotion, d.State);
    }

    private static Vector3 CentreOfMass(Opponent d) =>
        d.Ragdoll.Bodies.Aggregate(Vector3.Zero, (sum, b) => sum + b.Position * b.Mass) /
        d.Ragdoll.Bodies.Sum(b => b.Mass);

    [Fact]
    public void MissSettlesAndAiCannotSteerOrRestartAnimation()
    {
        var a = Lunge(.8f); var b = Lunge(.8f);
        float duration = Duration(a);
        a.Update(Vector3.Zero, duration); b.Update(Vector3.Zero, duration);
        var clip = Animation(a); float yaw = a.FacingYawDegrees;
        for (int i = 0; i < 2400 && a.Ragdoll.IsActive; i++)
        {
            a.Update(new(200, 0, 100), Ragdoll.FixedStep, true, Vector2.One, true);
            b.Update(new(-200, 0, -100), Ragdoll.FixedStep);
        }
        Assert.Equal(RagdollState.Inactive, a.Ragdoll.State);
        Assert.Equal(a.Ragdoll.Bodies.Select(x => x.Position), b.Ragdoll.Bodies.Select(x => x.Position));
        Assert.Equal(duration, clip.CurrentTime, 5);
        Assert.Equal(DefenderState.Down, a.State);
        Assert.Equal("Down", a.AnimationName);
        Assert.All(a.Ragdoll.Bodies, x => Assert.True(x.Bottom >= -.00001f));
    }

    [Fact]
    public void CrossingBoundaryConsumesOnlyRemainingTimeInPhysics()
    {
        var whole = Lunge(); var split = Lunge();
        float duration = Duration(whole);
        whole.Update(Vector3.Zero, duration + .1f);
        split.Update(Vector3.Zero, duration);
        split.Update(Vector3.Zero, .1f);
        for (int i = 0; i < whole.Ragdoll.Bodies.Count; i++)
            Assert.True(Vector3.Distance(whole.Ragdoll.Bodies[i].Position, split.Ragdoll.Bodies[i].Position) < .0001f);
    }

    [Theory]
    [InlineData(.1f)]
    [InlineData(.6f)]
    [InlineData(2f)]
    public void ResetClearsLungeAndPhysics(float elapsed)
    {
        var d = Lunge();
        for (float t = 0; t < elapsed; t += .01f) d.Update(Vector3.Zero, .01f);
        d.Reset();
        Assert.Equal(DefenderState.Locomotion, d.State);
        Assert.Equal(RagdollState.Inactive, d.Ragdoll.State);
        Assert.Equal("Jog", d.AnimationName);
        Assert.Equal(new Vector3(5, 0, -7), d.Position);
        Assert.Equal(Vector3.Zero, d.Velocity);
        Assert.Equal(0f, Field<float>(d, "_tackleRemaining"));
        Assert.Equal(0f, Animation(d).CurrentTime);
        d.Update(d.Position + new Vector3(0, 0, -30), .1f, false, Vector2.Zero, false);
        Assert.True(Animation(d).CurrentTime > 0);
    }

    [Fact]
    public void SetWrapStaysGroundedAndNeverHandsOff()
    {
        var d = new Opponent(Vector3.Zero, new());
        d.InitializeVisual(_assets);
        d.Update(new(0, 0, 1), 0, true, Vector2.Zero, true);
        float duration = Duration(d);
        d.UpdateLungeAfterOutcome(.2f);
        Assert.Equal(0f, Animation(d).CurrentTime);
        d.Update(Vector3.Zero, duration, true, Vector2.Zero, false);
        Assert.Equal(DefenderState.TackleReady, d.State);
        Assert.False(d.Ragdoll.IsActive);
        Assert.True(d.IsGrounded);
        Assert.Equal(Vector3.Zero, d.Position);
    }

    [Fact]
    public void HeadlessCompletionWaitsForActualPoseAndThenHandsOff()
    {
        var d = Lunge(visual: false);
        d.Update(Vector3.Zero, .6f);
        var position = d.Position; var velocity = d.Velocity;
        d.Update(new(100, 0, 100), 10f);
        Assert.Equal(position, d.Position);
        Assert.Equal(DefenderState.LungeTackle, d.State);
        Assert.False(d.Ragdoll.IsActive);
        d.InitializeVisual(_assets);
        d.Update(Vector3.Zero, 0f);
        Assert.True(d.Ragdoll.IsActive);
        Assert.All(d.Ragdoll.Bodies, body => Assert.Equal(velocity, body.LinearVelocity));
    }

}
