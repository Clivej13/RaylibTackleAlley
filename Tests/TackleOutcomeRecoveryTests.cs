using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class TackleOutcomeRecoveryTests : IDisposable
{
    private readonly AssetManager _assets;
    private readonly InputController _input;
    private readonly TackleAlleyConfig _config = new() { RagdollDownDuration = .8f, RagdollDownBlendDuration = .5f };
    private readonly TackleAlleyGame _game;
    private readonly BallCarrier _carrier;
    private readonly Opponent _defender;
    private static T Field<T>(object o, string n) => (T)o.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;
    private static void Position(BallCarrier c, Vector3 position) => typeof(BallCarrier).GetProperty(nameof(BallCarrier.Position))!.SetValue(c, position);
    public TackleOutcomeRecoveryTests()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Two-character tackle");
        _assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        _assets.RequireAssets(Opponent.AnimationAssetKeys);
        _assets.RequireAssets("Football", "FootballPlayerCarryJogAnimations", "FootballPlayerCarryRunAnimations",
            "FootballPlayerCarrySprintAnimations", "FootballPlayerCutLeftAnimations", "FootballPlayerCutRightAnimations",
            "FootballPlayerJukeLeftAnimations", "FootballPlayerJukeRightAnimations", "FootballPlayerSpinLeftAnimations", "FootballPlayerSpinRightAnimations");
        while (!_assets.ProcessNext()) { }
        _input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        _game = new(_config, _input, _assets);
        _game.InitializeVisuals(_assets);
        _carrier = Field<BallCarrier>(_game, "_player");
        var defenders = Field<Opponent[]>(_game, "_opponents");
        _defender = new(new(4, 0, -8), _config);
        _defender.InitializeVisual(_assets); defenders[0] = _defender;
        for (int i = 1; i < defenders.Length; i++) defenders[i] = new(new(1000 + i, 0, 0), _config);
    }
    public void Dispose() { _assets.UnloadAll(); Raylib.CloseWindow(); }
    private void Launch(float side = .5f)
    {
        _defender.Update(_defender.Position + new Vector3(0, 0, -30), .1f, false, Vector2.Zero, false);
        _defender.Update(_defender.Position + new Vector3(0, 0, -4), 0, true, Vector2.Zero, false);
        _defender.Update(_defender.Position + new Vector3(side, 0, -1.8f), 0);
        Assert.Equal(DefenderState.LungeTackle, _defender.State);
        _defender.Update(Vector3.Zero, .15f);
        // Advance actual carrier motion before contact, then place it inside the existing distance test.
        _carrier.Update(_input, .1f, new FootballField(_config, _assets));
        Position(_carrier, _defender.Position + new Vector3(0, 0, -.3f));
    }

    [Fact]
    public void ContactPreservesPosesAndVelocitiesThenAddsEqualOppositeCommittedImpulse()
    {
        Launch();
        var dc = Field<AnimationPlayer>(_defender, "_animation");
        var cc = Field<AnimationPlayer>(_carrier, "_animation");
        var dp = RagdollPose.Snapshot(Field<ModelInstance>(_defender, "_model").Model, dc);
        var cp = RagdollPose.Snapshot(Field<ModelInstance>(_carrier, "_model").Model, cc);
        Vector3 dv = _defender.Velocity, cv = _carrier.Velocity, j = LungeTackleOutcome.Impulse(dv);
        _game.Update(0);
        Assert.True(_game.TacklePendingGroundImpact);
        Assert.False(_game.GameOver);
        Assert.True(_defender.Ragdoll.IsActive); Assert.True(_carrier.Ragdoll.IsActive);
        Assert.True(j.X > 0 && j.Z < 0); Assert.Equal(0, j.Y);
        Vector3 totalChange = Vector3.Zero;
        for (int i = 0; i < 11; i++)
        {
            float fraction = i == 1 ? .6f : i == 0 ? .4f : 0;
            var d = _defender.Ragdoll.Bodies[i]; var c = _carrier.Ragdoll.Bodies[i];
            Assert.True(Vector3.Distance(dv - j * fraction / d.Mass, d.LinearVelocity) < .0001f);
            Assert.True(Vector3.Distance(cv + j * fraction / c.Mass, c.LinearVelocity) < .0001f);
            totalChange += (d.LinearVelocity - dv) * d.Mass + (c.LinearVelocity - cv) * c.Mass;
        }
        Assert.True(totalChange.Length() < .001f);
        CheckPose(dp, Field<RagdollSkeleton>(_defender, "_ragdollSkeleton").ModelPose);
        CheckPose(cp, Field<RagdollSkeleton>(_carrier, "_ragdollSkeleton").ModelPose);
        Assert.False(LungeTackleOutcome.Confirm(_defender, _carrier)); // no duplicate impulse
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundImpactEndsConfirmedTackleOnceAndResetClearsPending(bool boundary)
    {
        Launch();
        if (boundary)
        {
            var lunge = Field<AnimationPlayer>(_defender, "_animation");
            _defender.Update(Vector3.Zero, (lunge.FrameCount - 1) / lunge.FramesPerSecond - lunge.CurrentTime - .01f);
            Position(_carrier, _defender.Position);
        }
        _game.Update(boundary ? .01f : 0);
        Assert.True(_game.TacklePendingGroundImpact); Assert.False(_game.GameOver);
        Assert.Equal(0, _game.EndStateElapsed);
        for (int i = 0; i < 2400 && !_game.GameOver; i++)
        {
            _game.Update(Ragdoll.FixedStep);
            Assert.Equal(_carrier.HasTackleGroundImpact, _game.GameOver);
        }
        Assert.True(_game.GameOver); Assert.False(_game.TacklePendingGroundImpact);
        Assert.True(_carrier.Ragdoll.IsActive); // impact, not settled/recovery
        Assert.NotEqual(RagdollState.Settled, _carrier.Ragdoll.State);
        _game.Update(.1f); Assert.Equal(.1f, _game.EndStateElapsed, 5);
        _game.Update(.1f); Assert.Equal(.2f, _game.EndStateElapsed, 5);
        _game.ResetRun();
        Assert.False(_game.GameOver); Assert.False(_game.TacklePendingGroundImpact);
        Assert.False(_carrier.HasTackleGroundImpact);
        Assert.False(_carrier.Ragdoll.IsActive); Assert.False(_defender.Ragdoll.IsActive);
        Launch(); _game.Update(0);
        Assert.True(_game.TacklePendingGroundImpact);
        _game.ResetRun();
        Assert.False(_game.TacklePendingGroundImpact); Assert.False(_game.GameOver);
    }

    [Fact]
    public void ControlsStopAndFootballTracksPhysicsHand()
    {
        Launch(); _game.Update(0);
        var animation = Field<AnimationPlayer>(_carrier, "_animation"); float time = animation.CurrentTime;
        int speedTier = _carrier.SpeedTier;
        // Non-neutral input must be ignored before it can change actions or steering.
        Key(KeyboardKey.LeftShift, true); Key(KeyboardKey.A, true);
        try
        {
            _input.Update();
            _carrier.Update(_input, .1f, new FootballField(_config, _assets));
        }
        finally { Key(KeyboardKey.LeftShift, false); Key(KeyboardKey.A, false); }
        Assert.Equal(time, animation.CurrentTime); Assert.Equal(speedTier, _carrier.SpeedTier);
        Assert.Equal(0, Field<float>(_carrier, "_jukeRemaining"));
        var bridge = Field<RagdollSkeleton>(_carrier, "_ragdollSkeleton");
        var model = Field<ModelInstance>(_carrier, "_model").Model;
        unsafe {
            int hand = Enumerable.Range(0, model.Skeleton.BoneCount).Single(i => new string(model.Skeleton.Bones[i].Name) == "Hand.R");
            var grip = (Matrix4x4)typeof(BallCarrier).GetField("FootballGripLocal", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            CheckPose([grip * bridge.ModelPose[hand] * bridge.ModelWorld], [Field<Matrix4x4>(_carrier, "_footballWorldTransform")]);
        }
        Raylib.BeginDrawing(); _carrier.Draw(); _defender.Draw(); Raylib.EndDrawing();
    }

    [Fact]
    public void MissLeavesCarrierMovingAndNeverConfirmsTackle()
    {
        Launch(); Position(_carrier, new(-10, 0, 0));
        var lunge = Field<AnimationPlayer>(_defender, "_animation");
        _defender.Update(Vector3.Zero, (lunge.FrameCount - 1) / lunge.FramesPerSecond - lunge.CurrentTime);
        Assert.True(_defender.Ragdoll.IsActive);
        Vector3 start = _carrier.Position;
        for (int i = 0; i < 120; i++) _game.Update(Ragdoll.FixedStep);
        Assert.True(_carrier.Position.Z < start.Z);
        Assert.False(_carrier.Ragdoll.IsActive); Assert.False(_game.GameOver); Assert.False(_game.TacklePendingGroundImpact);
    }

    [Fact]
    public void BothCharactersRecoverFromSettledPoseThroughDownAndGetUp()
    {
        Launch(); _game.Update(0);
        bool carrierDown = false, defenderDown = false, carrierGetUp = false, defenderGetUp = false;
        Vector3? downPosition = null;
        for (int i = 0; i < 4000; i++)
        {
            RagdollSkeleton? old = _defender.Ragdoll.IsActive ? Field<RagdollSkeleton>(_defender, "_ragdollSkeleton") : null;
            _game.Update(Ragdoll.FixedStep);
            if (_defender.State == DefenderState.Down && !defenderDown)
            {
                defenderDown = true; downPosition = _defender.Position;
                var recovery = Field<RagdollRecovery>(_defender, "_recovery");
                Assert.NotNull(old);
                CheckPose(old!.ModelPose.Select(p => p * old.ModelWorld).ToArray(),
                    recovery.ModelPose.Select(p => p * recovery.World).ToArray());
                Assert.False(_defender.Ragdoll.IsActive);
            }
            if (_defender.IsRecovering) Assert.Equal(downPosition, _defender.Position);
            carrierDown |= _carrier.AnimationName == "Down";
            carrierGetUp |= _carrier.AnimationName == "GetUp";
            defenderGetUp |= _defender.State == DefenderState.GetUp;
            if (carrierDown && defenderDown && carrierGetUp && defenderGetUp &&
                !_carrier.IsRecovering && !_defender.IsRecovering) break;
        }
        Assert.True(carrierDown && defenderDown && carrierGetUp && defenderGetUp);
        Assert.Equal(DefenderState.Locomotion, _defender.State);
        Assert.False(_carrier.IsRecovering); Assert.StartsWith("Carry", _carrier.AnimationName);
        Assert.False(_carrier.Ragdoll.IsActive);
        _game.ResetRun(); Assert.Equal(Vector3.Zero, _carrier.Position);
    }

    [Fact]
    public void FeetTouchingGroundDoNotCountAsTackledGroundImpact()
    {
        _carrier.Update(_input, .1f, new FootballField(_config, _assets));
        Assert.True(_carrier.ActivateRagdoll());
        _carrier.Ragdoll.Update(Ragdoll.FixedStep);
        Assert.False(_carrier.Ragdoll.HasMeaningfulGroundContact);
        Assert.False(_game.GameOver);
    }

    [Fact]
    public void SetWrapRetainsImmediateOutcomeWithoutRagdolls()
    {
        _defender.Update(_defender.Position + Vector3.UnitZ, 0, true, Vector2.Zero, true);
        Position(_carrier, _defender.Position);
        _game.Update(0);
        Assert.True(_game.GameOver); Assert.False(_game.TacklePendingGroundImpact);
        Assert.False(_carrier.Ragdoll.IsActive); Assert.False(_defender.Ragdoll.IsActive);
        Assert.Equal(DefenderState.SetWrap, _defender.State);
    }

    private static unsafe void Key(KeyboardKey key, bool down)
    {
        var e = new AutomationEvent { Type = down ? 2u : 1u };
        e.Params[0] = (int)key; Raylib.PlayAutomationEvent(e);
    }

    [Fact]
    public void DownDurationIsConfigurableAndRecoveryCannotSteer()
    {
        Launch(); _game.Update(0);
        for (int i = 0; i < 4000 && !_defender.IsRecovering; i++)
            _defender.UpdateLungeAfterOutcome(Ragdoll.FixedStep);
        Assert.Equal(DefenderState.Down, _defender.State);
        Vector3 root = _defender.Position; float yaw = _defender.FacingYawDegrees;
        _defender.Update(new(-100, 0, 100), _config.RagdollDownDuration - .01f, true, Vector2.One, true);
        Assert.Equal(DefenderState.Down, _defender.State);
        Raylib.BeginDrawing(); _defender.Draw(); Raylib.EndDrawing();
        AssertGroundedRecovery(_defender);
        _defender.Update(new(100, 0, -100), .01f, false, -Vector2.One, true);
        Assert.Equal(DefenderState.GetUp, _defender.State);
        var clip = Field<AnimationPlayer>(_defender, "_animation");
        float duration = (clip.FrameCount - 1) / clip.FramesPerSecond;
        _defender.Update(Vector3.Zero, duration - .01f, true, Vector2.One, true);
        Assert.Equal(DefenderState.GetUp, _defender.State);
        Assert.Equal(root, _defender.Position); Assert.Equal(yaw, _defender.FacingYawDegrees);
        _defender.Update(Vector3.Zero, .01f, false, Vector2.One, true);
        Assert.Equal(DefenderState.Locomotion, _defender.State);
        Assert.Equal(root, _defender.Position);
        Assert.Equal(0, _defender.Velocity.Length());
    }

    private static unsafe void AssertGroundedRecovery(Opponent defender)
    {
        var model = Field<ModelInstance>(defender, "_model").Model;
        var world = Field<RagdollRecovery>(defender, "_recovery").World;
        float bottom = float.PositiveInfinity;
        for (int m = 0; m < model.MeshCount; m++)
        {
            var mesh = model.Meshes[m];
            if (mesh.AnimVertices == null) continue;
            for (int v = 0; v < mesh.VertexCount; v++)
                bottom = Math.Min(bottom, Vector3.Transform(new(mesh.AnimVertices[v*3],
                    mesh.AnimVertices[v*3+1], mesh.AnimVertices[v*3+2]), world).Y);
        }
        Assert.InRange(bottom, -.05f, .1f);
    }

    private static void CheckPose(IReadOnlyList<Matrix4x4> a, IReadOnlyList<Matrix4x4> b)
    {
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            foreach (var p in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                Assert.True(Vector3.Distance(Vector3.Transform(p, a[i]), Vector3.Transform(p, b[i])) < .0005f, $"Bone {i} moved during handoff");
    }
}
