using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class TackleReadyTests
{
    [Fact]
    public void AiUsesReadyDistanceHysteresisAndRequiresPreparationBeforeCommit()
    {
        var defender = Defender();
        void At(float distance) => defender.Update(defender.Position + new Vector3(0, 0, -distance), 0);
        At(4.01f);
        Assert.Equal(DefenderState.Locomotion, defender.State);
        At(4);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        Assert.Equal("TackleReadyForward", defender.AnimationName);
        Assert.Equal(4f, defender.CurrentSpeed); // Zero-time entry preserves incoming speed.
        At(5);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        At(5.01f);
        Assert.Equal(DefenderState.Locomotion, defender.State);
        At(2);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        At(1f);
        Assert.Equal(DefenderState.SetWrap, defender.State);
    }

    [Theory]
    [InlineData(0, "SetWrapForward")]
    [InlineData(-1, "SetWrapLeft")]
    [InlineData(1, "SetWrapRight")]
    public void AiCommitsSideAndIgnoresCarrierMovementUntilCompletion(float side, string clip)
    {
        var defender = Defender();
        var start = defender.Position;
        defender.Update(start + new Vector3(0, 0, -3), 0.01f);
        start = defender.Position;
        defender.Update(start + new Vector3(side * 0.4f, 0, -0.8f), 0);
        float facing = defender.FacingYawDegrees;
        Assert.Equal(clip, defender.AnimationName);
        defender.Update(start + new Vector3(-side, 0, 30), 0.2f);
        Assert.Equal(DefenderState.SetWrap, defender.State);
        Assert.Equal(clip, defender.AnimationName);
        Assert.Equal(start, defender.Position);
        Assert.Equal(facing, defender.FacingYawDegrees);
        defender.Update(start + new Vector3(0, 0, 30), 0.2f);
        Assert.Equal(DefenderState.Locomotion, defender.State);
        Assert.Equal("Jog", defender.AnimationName);
        Assert.Equal(start, defender.Position);
    }

    [Fact]
    public void AiReturnsReadyAfterWrapAndSquaresAtCappedGroundedSpeed()
    {
        var defender = Defender();
        defender.Update(defender.Position + new Vector3(0, 0, -3), 0);
        defender.Update(defender.Position + new Vector3(0, 0, -1), 0);
        defender.Update(defender.Position + new Vector3(0, 0, -3), 0.4f);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        var start = defender.Position;
        defender.Update(start + new Vector3(3, 0, 0), 0.1f);
        Assert.Equal(-36f, defender.FacingYawDegrees, 4);
        Assert.Equal("TackleReadyRight", defender.AnimationName);
        Assert.InRange(Vector3.Distance(start, defender.Position), 0.01f, defender.TackleReadySpeed * 0.1f);
        Assert.Equal(0f, defender.Position.Y);
        defender.Update(start + new Vector3(3, 0, 0), 0.2f);
        Assert.Equal(-90f, defender.FacingYawDegrees, 4);
        Assert.Equal("TackleReadyForward", defender.AnimationName);
    }

    [Fact]
    public void AiClosesAtConfiguredSpeedOneBeforeLunging()
    {
        var config = new TackleAlleyConfig { PlayerSlowSpeed = 2f };
        var defender = new Opponent(Vector3.Zero, config);
        defender.Update(new(0, 0, -30), 0.01f);
        Vector3 carrier = defender.Position + new Vector3(0, 0, -4);
        float incoming = defender.CurrentSpeed;
        defender.Update(carrier, 0f);
        Assert.Equal(incoming, defender.CurrentSpeed);
        defender.Update(carrier, 0.1f);
        Assert.Equal(incoming - config.ForwardDeceleration * 0.1f, defender.CurrentSpeed, 4);
        bool reachedSpeedOne = false;
        for (int i = 0; i < 120 && defender.State != DefenderState.LungeTackle; i++)
        {
            float before = Vector3.Distance(defender.Position, carrier);
            defender.Update(carrier, 1f / 60f);
            if (defender.State == DefenderState.LungeTackle)
                Assert.InRange(before, Opponent.WrapCommitDistance, Opponent.LungeReachDistance);
            else
            {
                Assert.True(Vector3.Distance(defender.Position, carrier) < before);
                Assert.Equal("TackleReadyForward", defender.AnimationName);
                reachedSpeedOne |= Math.Abs(defender.CurrentSpeed - config.PlayerSlowSpeed) < 0.001f;
            }
        }
        Assert.True(reachedSpeedOne);
        Assert.Equal(DefenderState.LungeTackle, defender.State);
    }

    [Theory]
    [InlineData(-3, 0, "TackleReadyLeft")]
    [InlineData(3, 0, "TackleReadyRight")]
    [InlineData(0, 3, "TackleReadyBackward")]
    [InlineData(0, -3, "TackleReadyForward")]
    public void AiReadyFollowsInterceptionTargetWhileFacingCarrier(float x, float z, string clip)
    {
        var defender = Defender();
        Vector3 start = defender.Position;
        defender.Update(start + new Vector3(0, 0, -4), 0.1f, start + new Vector3(x, 0, z));
        Assert.Equal(DefenderState.TackleReady, defender.State);
        Assert.Equal(clip, defender.AnimationName);
        Assert.Equal(0f, defender.FacingYawDegrees);
        Assert.True(Vector3.Dot(defender.Position - start, new Vector3(x, 0, z)) > 0f);
    }

    [Fact]
    public void NeutralReadyCoastsThenSelectsStationaryOnlyAfterStopping()
    {
        var defender = Defender();
        Vector3 start = defender.Position;
        defender.Update(new(0, 0, -30), 0.1f, true, Vector2.Zero, false);
        Assert.True(defender.Position.Z < start.Z);
        Assert.True(defender.CurrentSpeed > 0f);
        Assert.Equal("TackleReadyForward", defender.AnimationName);
        defender.Update(new(0, 0, -30), 1f, true, Vector2.Zero, false);
        Assert.Equal(0f, defender.CurrentSpeed);
        Assert.Equal("TackleReady", defender.AnimationName);
    }

    private static Opponent Defender()
    {
        var defender = new Opponent(Vector3.Zero, new());
        defender.Update(new(0, 0, -30), 0.01f); // Face authored -Z.
        return defender;
    }

    [Fact]
    public void TriggerEntersAndExitsReadyAndTackleRequiresReady()
    {
        var defender = Defender();
        defender.Update(new(0, 0, -30), 0, false, Vector2.Zero, true);
        Assert.Equal(DefenderState.Locomotion, defender.State);
        defender.Update(new(0, 0, -30), 0, true, Vector2.Zero, false);
        Assert.Equal(DefenderState.TackleReady, defender.State);
        Assert.Equal("TackleReadyForward", defender.AnimationName); // Incoming travel is still active.
        defender.Update(new(0, 0, -30), 0, false, Vector2.Zero, false);
        Assert.Equal(DefenderState.Locomotion, defender.State);
        Assert.Equal("Jog", defender.AnimationName);
    }

    [Theory]
    [InlineData(0, 0, "TackleReadyForward")]
    [InlineData(0, 1, "TackleReadyForward")]
    [InlineData(0, -1, "TackleReadyBackward")]
    [InlineData(-1, 0, "TackleReadyLeft")]
    [InlineData(1, 0, "TackleReadyRight")]
    [InlineData(-0.8f, 0.3f, "TackleReadyLeft")]
    [InlineData(0.3f, -0.8f, "TackleReadyBackward")]
    [InlineData(1, 1, "TackleReadyForward")]
    public void ReadyUsesDominantLocalAxis(float x, float y, string clip)
    {
        var defender = Defender();
        defender.Update(new(0, 0, -30), 0.1f, true, new(x, y), false);
        Assert.Equal(clip, defender.AnimationName);
        Assert.Equal(0f, defender.FacingYawDegrees);
        Assert.Equal(0f, defender.Position.Y);
    }

    [Fact]
    public void ReadyDeceleratesSprintSpeedAndNormalizesDiagonalTravelAndKeepsSquare()
    {
        var defender = Defender();
        defender.Update(new(0, 0, -3), 0.7f, false, Vector2.Zero, false);
        Assert.Equal(9f, defender.CurrentSpeed);
        var start = defender.Position;
        defender.Update(start + new Vector3(20, 0, 0), 1f, true, Vector2.One, false);
        Assert.Equal(4f, defender.CurrentSpeed, 4);
        Assert.Equal(5f, Vector3.Distance(start, defender.Position), 4);
        Assert.Equal(-90f, defender.FacingYawDegrees, 4);
        defender.Update(new(0, 0, -30), 1f, true, Vector2.Zero, false);
        Assert.Equal(0f, defender.CurrentSpeed);
        Assert.Equal(start.Y, defender.Position.Y);
    }

    [Theory]
    [InlineData(0, "SetWrapForward")]
    [InlineData(-14, "SetWrapForward")]
    [InlineData(14, "SetWrapForward")]
    [InlineData(-16, "SetWrapLeft")]
    [InlineData(16, "SetWrapRight")]
    [InlineData(-45, "SetWrapLeft")]
    [InlineData(45, "SetWrapRight")]
    [InlineData(-90, "SetWrapLeft")]
    [InlineData(90, "SetWrapRight")]
    public void SetWrapSelectsTargetSideRelativeToFacing(float degrees, string clip)
    {
        foreach (float yaw in new[] { 0f, 90f, 180f })
        {
            var defender = new Opponent(Vector3.Zero, new());
            var rotation = Matrix4x4.CreateRotationY(yaw * MathF.PI / 180f);
            defender.Update(Vector3.Transform(new Vector3(0, 0, -30), rotation), 0.01f);
            float radians = degrees * MathF.PI / 180f;
            var target = defender.Position + Vector3.Transform(new Vector3(MathF.Sin(radians), 0, -MathF.Cos(radians)) * 5f, rotation);
            defender.Update(target, 0, true, Vector2.Zero, true);
            Assert.Equal(clip, defender.AnimationName);
            Assert.Equal(DefenderState.SetWrap, defender.State);
        }
    }

    [Theory]
    [InlineData(true, DefenderState.TackleReady, "TackleReady")]
    [InlineData(false, DefenderState.Locomotion, "Jog")]
    public void ActiveTackleIgnoresSteeringAndReselectionThenReturns(bool held, DefenderState state, string clip)
    {
        var defender = Defender();
        var start = defender.Position;
        defender.Update(start + new Vector3(-3, 0, -4), 0, true, Vector2.Zero, true);
        defender.Update(start + new Vector3(3, 0, -4), 0.2f, held, Vector2.One, true);
        Assert.Equal("SetWrapLeft", defender.AnimationName);
        Assert.Equal(DefenderState.SetWrap, defender.State);
        Assert.Equal(start, defender.Position);
        Assert.Equal(0f, defender.FacingYawDegrees);
        defender.Update(start + new Vector3(0, 0, -30), 0.2f, held, Vector2.Zero, true);
        Assert.Equal(state, defender.State);
        Assert.Equal(clip, defender.AnimationName);
        Assert.Equal(start, defender.Position);
        defender.Reset();
        Assert.Equal(DefenderState.Locomotion, defender.State);
        Assert.Equal("Jog", defender.AnimationName);
    }

    [Fact]
    public void CollisionRemainsOmnidirectionalDistanceOnlyInEveryState()
    {
        var defender = Defender();
        foreach (var controls in new[] { (false, false), (true, false), (true, true) })
        {
            defender.Update(defender.Position + new Vector3(0, 0, -30), 0, controls.Item1, Vector2.Zero, controls.Item2);
            foreach (var axis in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY })
            {
                Assert.True(defender.IsTouching(defender.Position + axis * 1.4f));
                Assert.False(defender.IsTouching(defender.Position + axis * 1.401f));
            }
        }
    }

    [Fact]
    public void NativeClipsLoadAndOneShotsCompleteAtTheirFinalFrame()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Tackle state validation");
        var assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        try
        {
            assets.RequireAssets(Opponent.AnimationAssetKeys);
            while (!assets.ProcessNext()) { }
            var defender = Defender();
            defender.InitializeVisual(assets);
            var field = typeof(Opponent).GetField("_animation", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var move in new[] { Vector2.Zero, Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY })
            {
                defender.Update(new(0, 0, -30), 0.05f, true, move, false);
                Assert.True(((AnimationPlayer)field.GetValue(defender)!).CurrentTime > 0);
            }
            foreach (float side in new[] { -3f, 0f, 3f })
            {
                defender.Reset();
                defender.Update(new(0, 0, -30), 0.01f);
                defender.Update(defender.Position + new Vector3(side, 0, -4), 0, true, Vector2.Zero, true);
                var animation = (AnimationPlayer)field.GetValue(defender)!;
                Assert.Equal(0f, animation.CurrentTime);
                float duration = (animation.FrameCount - 1) / animation.FramesPerSecond;
                Assert.InRange(duration, 0.38f, 0.42f);
                defender.Update(new(0, 0, -30), duration / 2, true, Vector2.One, false);
                Assert.Same(animation, field.GetValue(defender));
                Assert.Equal(DefenderState.SetWrap, defender.State);
                defender.Update(new(0, 0, -30), duration / 2, true, Vector2.Zero, false);
                Assert.Equal(DefenderState.TackleReady, defender.State);
                Assert.Equal("TackleReady", defender.AnimationName);
            }
            foreach (float side in new[] { -0.8f, 0f, 0.8f })
            {
                defender.Reset();
                defender.Update(new(0, 0, -30), 0.01f);
                defender.Update(defender.Position + new Vector3(0, 0, -4), 0);
                defender.Update(defender.Position + new Vector3(side, 0, -1.8f), 0);
                Assert.Equal(DefenderState.LungeTackle, defender.State);
                Assert.Equal(side == 0f ? "LungeTackleForward" :
                    side < 0f ? "LungeTackleLeft" : "LungeTackleRight", defender.AnimationName);
                var animation = (AnimationPlayer)field.GetValue(defender)!;
                float duration = (animation.FrameCount - 1) / animation.FramesPerSecond;
                Assert.InRange(duration, 0.58f, 0.62f);
                Assert.Equal(0f, animation.CurrentTime);
                float facing = defender.FacingYawDegrees;
                defender.Update(defender.Position + new Vector3(0, 0, 4), duration / 2);
                Assert.Same(animation, field.GetValue(defender));
                Assert.Equal(DefenderState.LungeTackle, defender.State);
                Assert.Equal(facing, defender.FacingYawDegrees);
                defender.Update(defender.Position + new Vector3(0, 0, 4), duration / 2);
                Assert.Equal(DefenderState.LungeLand, defender.State);
                animation = (AnimationPlayer)field.GetValue(defender)!;
                Assert.Equal(0f, animation.CurrentTime);
                duration = (animation.FrameCount - 1) / animation.FramesPerSecond;
                defender.Update(new(30, 0, 30), duration / 2, true, Vector2.One, true);
                Assert.Equal(DefenderState.LungeLand, defender.State);
                Assert.True(defender.IsGrounded);
                Vector3 landed = defender.Position;
                defender.Update(new(30, 0, 30), duration / 2, true, Vector2.One, true);
                Assert.Equal(DefenderState.Down, defender.State);
                Assert.Equal("Down", defender.AnimationName);
                animation = (AnimationPlayer)field.GetValue(defender)!;
                float loopDuration = animation.FrameCount / animation.FramesPerSecond;
                defender.Update(new(30, 0, 30), loopDuration + 0.1f, true, Vector2.One, true);
                Assert.Equal(DefenderState.Down, defender.State);
                Assert.InRange(animation.CurrentTime, 0.09f, 0.11f);
                Assert.Equal(landed, defender.Position);
                defender.Update(new(30, 0, 30),
                    Opponent.DefenderDownDurationSeconds - loopDuration - 0.1f, true, Vector2.One, true);
                Assert.Equal(DefenderState.GetUp, defender.State);
                animation = (AnimationPlayer)field.GetValue(defender)!;
                Assert.Equal(0f, animation.CurrentTime);
                duration = (animation.FrameCount - 1) / animation.FramesPerSecond;
                defender.Update(new(30, 0, 30), duration - 0.01f, true, Vector2.One, true);
                Assert.Equal(DefenderState.GetUp, defender.State);
                Assert.Equal(landed, defender.Position);
                Assert.Equal(facing, defender.FacingYawDegrees);
                defender.Update(new(30, 0, 30), 0.01f, true, Vector2.One, true);
                Assert.Equal(DefenderState.Locomotion, defender.State);
                Assert.Equal(landed, defender.Position);
                Assert.Equal(0f, ((AnimationPlayer)field.GetValue(defender)!).CurrentTime);
            }
            foreach (float elapsed in new[] { 0.1f, 0.7f, 1.3f, 3.3f })
            {
                defender.Reset();
                defender.Update(new(0, 0, -30), 0.01f);
                defender.Update(defender.Position + new Vector3(0, 0, -4), 0f);
                defender.Update(defender.Position + new Vector3(0, 0, -1.8f), 0f);
                defender.Update(new(30, 0, 30), elapsed, false, Vector2.Zero, false);
                defender.Reset();
                Assert.True(defender.IsGrounded);
                Assert.Equal(0f, defender.VerticalVelocity);
                var animations = (Dictionary<string, AnimationPlayer>)typeof(Opponent)
                    .GetField("_animations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(defender)!;
                Assert.All(animations.Values, clip => Assert.Equal(0f, clip.CurrentTime));
            }
            defender.Update(new(0, 0, -30), 0.01f);
            defender.Update(defender.Position + new Vector3(0, 0, -4), 0f);
            defender.Update(defender.Position + new Vector3(0, 0, -1.8f), 0f);
            defender.Update(new(30, 0, 30), 100f, true, Vector2.One, true);
            Assert.Equal(DefenderState.Locomotion, defender.State);
            Assert.True(defender.IsGrounded);
            Assert.Equal(0f, defender.Position.Y);
        }
        finally { assets.UnloadAll(); Raylib.CloseWindow(); }
    }
}
