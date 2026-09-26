using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class BallCarrierAnimationTests
{
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    private static unsafe float[] Vertices(object character)
    {
        var model = Field<ModelInstance>(character, "_model").Model;
        var result = new List<float>();
        for (int m = 0; m < model.MeshCount; m++)
        {
            var mesh = model.Meshes[m];
            if (mesh.AnimVertices == null) continue;
            for (int v = 0; v < mesh.VertexCount * 3; v++)
                result.Add(mesh.AnimVertices[v]);
        }
        Assert.NotEmpty(result);
        return result.ToArray();
    }

    private static unsafe void Key(KeyboardKey key, bool down)
    {
        AutomationEvent e = new() { Type = down ? 2u : 1u };
        e.Params[0] = (int)key;
        Raylib.PlayAutomationEvent(e);
    }

    private static float Phase(AnimationPlayer player) =>
        player.CurrentTime / (player.FrameCount / player.FramesPerSecond);

    [Fact]
    public void PlayerHasIndependentPlaybackPreservesPhaseAndLeavesMovementGameDriven()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Player model integration validation");
        var assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        try
        {
            assets.RequireAssets("Football", "FootballPlayer", "FootballPlayerAnimations",
                "FootballPlayerRunAnimations", "FootballPlayerSprintAnimations",
                "FootballPlayerCarryJogAnimations", "FootballPlayerCarryRunAnimations", "FootballPlayerCarrySprintAnimations",
                "FootballPlayerCutLeftAnimations", "FootballPlayerCutRightAnimations",
                "FootballPlayerJukeLeftAnimations", "FootballPlayerJukeRightAnimations",
                "FootballPlayerSpinLeftAnimations", "FootballPlayerSpinRightAnimations");
            assets.RequireAssets(Opponent.AnimationAssetKeys);
            while (!assets.ProcessNext()) { }
            var config = new TackleAlleyConfig();
            var input = new InputController(InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
            var field = new FootballField(config, assets);
            var player = new BallCarrier(config);
            var baseline = new BallCarrier(config);
            var opponent = new Opponent(Vector3.Zero, config);
            player.InitializeVisual(assets);
            opponent.InitializeVisual(assets);
            var model = Field<ModelInstance>(player, "_model");
            player.InitializeVisual(assets);
            Assert.Same(model, Field<ModelInstance>(player, "_model"));
            Assert.NotSame(model, Field<ModelInstance>(opponent, "_model"));
            Assert.Equal(Field<float>(opponent, "_visualScale"), Field<float>(player, "_visualScale"));
            Assert.Equal(Field<float>(opponent, "_groundOffset"), Field<float>(player, "_groundOffset"));
            var clocks = Field<Dictionary<string, AnimationPlayer>>(player, "_animations");
            var otherClocks = Field<Dictionary<string, AnimationPlayer>>(opponent, "_animations");
            foreach (string name in new[] { "Jog", "Run", "Sprint" })
                Assert.NotSame(clocks["Carry" + name], otherClocks[name]);
            var otherPose = Vertices(opponent);
            var startPose = Vertices(player);
            Matrix4x4 startBall = Field<Matrix4x4>(player, "_footballWorldTransform");
            Matrix4x4 grip = (Matrix4x4)typeof(BallCarrier)
                .GetField("FootballGripLocal", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Assert.True(Matrix4x4.Decompose(grip, out var gripScale, out _, out _));
            Assert.True(Vector3.Distance(Vector3.One, gripScale) < 0.00001f);
            Assert.Equal(Matrix4x4.Identity, assets.GetModel("Football").Transform);

            void CheckAttachment()
            {
                var active = Field<AnimationPlayer>(player, "_animation");
                Assert.True(active.TryGetBoneTransform("Hand.R", out var hand));
                float yaw = (float)typeof(BallCarrier).GetProperty("VisualYawDegrees",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
                var world = model.Model.Transform *
                    Matrix4x4.CreateScale(Field<float>(player, "_visualScale")) *
                    Matrix4x4.CreateRotationY(yaw * MathF.PI / 180f) *
                    Matrix4x4.CreateTranslation(player.Position +
                        new Vector3(0f, Field<float>(player, "_groundOffset"), 0f));
                Assert.Equal(grip * hand * world, Field<Matrix4x4>(player, "_footballWorldTransform"));
            }
            CheckAttachment();
            Assert.Equal("CarryRun", player.AnimationName);
            Assert.Equal(0f, Field<AnimationPlayer>(player, "_animation").CurrentTime);

            void Tick(float dt)
            {
                input.Update();
                player.Update(input, dt, field);
                baseline.Update(input, dt, field);
                Assert.Equal(baseline.Position, player.Position);
                Assert.Equal(baseline.CurrentForwardSpeed, player.CurrentForwardSpeed);
                Assert.Equal(Field<float>(baseline, "_jukeRemaining"), Field<float>(player, "_jukeRemaining"));
                Assert.Equal(Field<float>(baseline, "_spinRemaining"), Field<float>(player, "_spinRemaining"));
                // Before Draw: attachment must already use this update's pose and world state.
                CheckAttachment();
                Raylib.BeginDrawing();
                player.Draw();
                Raylib.EndDrawing();
            }

            var initialHand = Matrix4x4.Identity;
            Assert.True(clocks["CarryRun"].TryGetBoneTransform("Hand.R", out initialHand));
            Tick(0.23f);
            Assert.True(clocks["CarryRun"].TryGetBoneTransform("Hand.R", out var advancedHand));
            Assert.NotEqual(initialHand, advancedHand);
            Assert.NotEqual(startBall, Field<Matrix4x4>(player, "_footballWorldTransform"));
            Assert.Contains(Vertices(player).Zip(startPose), p => MathF.Abs(p.First - p.Second) > 0.001f);
            foreach (var (key, tier, name) in new[] {
                (KeyboardKey.S, 1, "CarryJog"), (KeyboardKey.LeftShift, 3, "CarrySprint"),
                (KeyboardKey.Null, 2, "CarryRun"), (KeyboardKey.LeftShift, 3, "CarrySprint"),
                (KeyboardKey.S, 1, "CarryJog"), (KeyboardKey.Null, 2, "CarryRun") })
            {
                float phase = Phase(Field<AnimationPlayer>(player, "_animation"));
                Key(KeyboardKey.S, false);
                Key(KeyboardKey.LeftShift, false);
                if (key != KeyboardKey.Null) Key(key, true);
                Tick(0f);
                Assert.Equal(tier, player.SpeedTier);
                Assert.Equal(name, player.AnimationName);
                Assert.Same(clocks[name], Field<AnimationPlayer>(player, "_animation"));
                Assert.Equal(phase, Phase(clocks[name]), 5);
                float time = clocks[name].CurrentTime;
                Vector3 before = player.Position;
                float speedBefore = player.CurrentForwardSpeed;
                Tick(0.025f);
                Assert.Equal(time + 0.025f * player.LocomotionPlaybackRate, clocks[name].CurrentTime, 5);
                float travel = before.Z - player.Position.Z;
                Assert.InRange(travel,
                    Math.Min(speedBefore, player.CurrentForwardSpeed) * 0.025f - 0.00001f,
                    Math.Max(speedBefore, player.CurrentForwardSpeed) * 0.025f + 0.00001f);
                Assert.Equal(0f, player.Position.Y);
                Assert.Equal(otherPose, Vertices(opponent));
                Assert.Equal(0f, Field<AnimationPlayer>(opponent, "_animation").CurrentTime);
            }

            // Exercise both real one-shot clips, including attachment on every frame,
            // sprint interruption protection, and the authored return gait phases.
            foreach (var (from, to, cut, exitPhase) in new[] {
                (KeyboardKey.A, KeyboardKey.D, "CutRight", 21f / 24f),
                (KeyboardKey.D, KeyboardKey.A, "CutLeft", 9f / 24f) })
            {
                Key(KeyboardKey.A, false); Key(KeyboardKey.D, false);
                Key(KeyboardKey.LeftShift, false);
                player.Reset(); baseline.Reset();
                Key(KeyboardKey.LeftShift, true);
                Key(from, true); Tick(0.02f);
                Key(KeyboardKey.LeftShift, false);
                Key(from, false); Tick(0.05f);
                Key(to, true); Tick(0f);
                Assert.Equal(cut, player.AnimationName);
                Assert.Same(clocks[cut], Field<AnimationPlayer>(player, "_animation"));
                Assert.Equal(0f, clocks[cut].CurrentTime);
                Tick(0.1f);
                Key(KeyboardKey.LeftShift, true);
                Tick(0.1f);
                Assert.Equal(cut, player.AnimationName);
                Tick(0.1f);
                Assert.Equal(cut, player.AnimationName);
                float exitPlaybackRate = PlayerMovementAttributes.PlaybackRate(player.CurrentForwardSpeed, config.PlayerSprintSpeed);
                Tick(0.08f);
                Assert.Equal("CarrySprint", player.AnimationName);
                Assert.Same(clocks["CarrySprint"], Field<AnimationPlayer>(player, "_animation"));
                float expectedTime = exitPhase * (clocks["CarrySprint"].FrameCount /
                    clocks["CarrySprint"].FramesPerSecond) + (0.38f - 22f / 60f) * exitPlaybackRate;
                Assert.Equal(expectedTime, clocks["CarrySprint"].CurrentTime, 5);
                Assert.Equal(otherPose, Vertices(opponent));
                Key(to, false); Key(KeyboardKey.LeftShift, false);
            }
            player.Reset(); baseline.Reset();

            // Exercise every evade using the real input triggers and clips. The action
            // timer, not the longer authored clip, owns movement and visual completion.
            input.ApplyRebind(new InputRebindResult("RightStickLeft", "Keyboard", "R"));
            input.ApplyRebind(new InputRebindResult("RightStickRight", "Keyboard", "E"));
            input.ApplyRebind(new InputRebindResult("RightStickBack", "Keyboard", "Q"));
            foreach (bool spin in new[] { false, true })
            foreach (bool left in new[] { false, true })
            foreach (bool slowExit in new[] { false, true })
            {
                player.Reset(); baseline.Reset();
                Key(KeyboardKey.S, false);
                Key(KeyboardKey.A, false); Key(KeyboardKey.D, false);
                Key(KeyboardKey.R, false); Key(KeyboardKey.E, false);
                Key(KeyboardKey.Q, false);
                Tick(0f);
                if (spin)
                {
                    Key(KeyboardKey.Q, true); Tick(0f);
                    Key(KeyboardKey.Q, false);
                }
                var side = left ? KeyboardKey.R : KeyboardKey.E;
                // Juke rig labels are opposite screen direction; movement must still follow input.
                string name = spin ? (left ? "SpinLeft" : "SpinRight")
                    : (left ? "JukeRight" : "JukeLeft");
                float duration = spin ? 0.45f : config.PlayerJukeDuration;
                Key(side, true); Tick(0f);
                Assert.Equal(name, player.AnimationName);
                Assert.Same(clocks[name], Field<AnimationPlayer>(player, "_animation"));
                Assert.Equal(0f, clocks[name].CurrentTime);
                Assert.Equal(player.TargetForwardSpeed * (spin ? 0.30f : 0.50f),
                    player.CurrentForwardSpeed, 5);
                float[] entry = Vertices(player);
                Vector3 before = player.Position;
                Key(KeyboardKey.D, true); // Normal steering cannot take action travel.
                Tick(duration * 0.25f);
                Assert.Equal((left ? -1f : 1f) * (spin ? config.PlayerSpinSpeed : config.PlayerJukeSpeed) *
                    duration * 0.25f, player.Position.X - before.X, 5);
                Assert.Equal(before.Z, player.Position.Z);
                Assert.Contains(Vertices(player).Zip(entry), p => MathF.Abs(p.First - p.Second) > 0.001f);
                if (spin)
                {
                    float yaw = (float)typeof(BallCarrier).GetProperty("VisualYawDegrees",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
                    Assert.Equal(-Field<float>(player, "_currentRunYaw"), yaw);
                }
                Key(KeyboardKey.S, true); Tick(duration * 0.25f);
                Assert.Equal(name, player.AnimationName);
                Assert.Same(clocks[name], Field<AnimationPlayer>(player, "_animation"));
                Assert.Equal(0.5f * (clocks[name].FrameCount - 1) / clocks[name].FramesPerSecond,
                    clocks[name].CurrentTime, 5);
                Key(KeyboardKey.S, slowExit);
                Tick(duration * 0.25f);
                Assert.Equal(name, player.AnimationName);
                float remaining = Field<float>(player, spin ? "_spinRemaining" : "_jukeRemaining");
                Tick(remaining + 0.01f);
                string carry = slowExit ? "CarryJog" : "CarryRun";
                Assert.Equal(carry, player.AnimationName);
                Assert.Same(clocks[carry], Field<AnimationPlayer>(player, "_animation"));
                float exitPhase = name is "JukeRight" or "SpinLeft" ? 0.5f : 0f;
                Assert.Equal(exitPhase * clocks[carry].FrameCount / clocks[carry].FramesPerSecond + 0.01f * player.LocomotionPlaybackRate,
                    clocks[carry].CurrentTime, 5);
                float completedTime = clocks[name].CurrentTime;
                Tick(0.02f); // A held gesture cannot loop or restart the clip.
                Assert.Equal(carry, player.AnimationName);
                Assert.Equal(completedTime, clocks[name].CurrentTime);
                Key(side, false); Key(KeyboardKey.S, false); Key(KeyboardKey.D, false);

                // Existing sprint cancellation must select CarrySprint in the same frame.
                player.Reset(); baseline.Reset(); Tick(0f);
                if (spin) { Key(KeyboardKey.Q, true); Tick(0f); Key(KeyboardKey.Q, false); }
                Key(side, true); Tick(duration * 0.25f);
                Key(KeyboardKey.LeftShift, true); Tick(0.01f);
                Assert.Equal("CarrySprint", player.AnimationName);
                Assert.Same(clocks["CarrySprint"], Field<AnimationPlayer>(player, "_animation"));
                Assert.Equal(0f, Field<float>(player, "_jukeRemaining"));
                Assert.Equal(0f, Field<float>(player, "_spinRemaining"));
                Key(side, false); Key(KeyboardKey.LeftShift, false);

                // Reset during an active move must clear its pose as well as its timers.
                player.Reset(); baseline.Reset(); Tick(0f);
                if (spin) { Key(KeyboardKey.Q, true); Tick(0f); Key(KeyboardKey.Q, false); }
                Key(side, true); Tick(duration * 0.25f);
                player.Reset(); baseline.Reset();
                Assert.Same(clocks["CarryRun"], Field<AnimationPlayer>(player, "_animation"));
                Assert.Equal(startPose, Vertices(player));
                CheckAttachment();
                Key(side, false);

                // Touchdown advancement uses the same remaining action time.
                Tick(0f);
                if (spin) { Key(KeyboardKey.Q, true); Tick(0f); Key(KeyboardKey.Q, false); }
                Key(side, true); Tick(0f);
                player.RunIntoEndZone(duration * 0.5f, -100f);
                Assert.Equal(name, player.AnimationName);
                CheckAttachment();
                player.RunIntoEndZone(duration * 0.5f + 0.01f, -100f);
                Assert.Equal("CarryRun", player.AnimationName);
                Assert.Same(clocks["CarryRun"], Field<AnimationPlayer>(player, "_animation"));
                CheckAttachment();
                // The underlying action players are non-looping as well as timer-driven.
                clocks[name].Update(10f);
                Assert.Equal(clocks[name].FrameCount - 1f, clocks[name].CurrentFrame, 4);
                Key(side, false);
            }
            player.Reset(); baseline.Reset();

            // Compare initialized/uninitialized movement for lateral travel, juke,
            // spin gestures, head fake cancellation, and boundary clamping.
            input.ApplyRebind(new InputRebindResult("RightStickRight", "Keyboard", "E"));
            input.ApplyRebind(new InputRebindResult("RightStickBack", "Keyboard", "Q"));
            Key(KeyboardKey.A, true); Tick(0.1f);
            Key(KeyboardKey.A, false);
            Key(KeyboardKey.D, true); Tick(0.1f);
            Key(KeyboardKey.E, true); Tick(0.05f);
            Key(KeyboardKey.E, false); Tick(0.5f);
            Key(KeyboardKey.Q, true); Tick(0.05f);
            Key(KeyboardKey.Q, false); Key(KeyboardKey.E, true); Tick(0.05f);
            Assert.True(Field<float>(player, "_spinRemaining") > 0f);
            Key(KeyboardKey.LeftShift, true); Tick(0.1f);
            Assert.Equal(0f, Field<float>(player, "_spinRemaining"));
            Key(KeyboardKey.LeftShift, false); Tick(5f);
            Assert.Equal(baseline.Position, player.Position);

            player.Reset();
            Assert.Equal(Vector3.Zero, player.Position);
            Assert.Equal("CarryRun", player.AnimationName);
            Assert.Equal(0f, clocks["CarryRun"].CurrentTime);
            Assert.Equal(0f, clocks["CarryRun"].CurrentFrame);
            Assert.Equal(startPose, Vertices(player));
            Assert.Equal(startBall, Field<Matrix4x4>(player, "_footballWorldTransform"));
            CheckAttachment();
            player.RunIntoEndZone(0.1f, -1f);
            Assert.Equal(Math.Max(-1f, -player.Speed * 0.1f), player.Position.Z);
            Assert.Equal(0.1f, clocks["CarryRun"].CurrentTime, 5);
            player.RunIntoEndZone(0.1f, -1f);
            Assert.Equal(-1f, player.Position.Z);
            Assert.Equal(0.2f, clocks["CarryRun"].CurrentTime, 5);
            player.RunIntoEndZone(.2f, -1f);
            Assert.Equal("TauntBicepFlex", player.AnimationName);
            Assert.Equal(0f, player.CurrentForwardSpeed);
            Assert.Equal(-1f, player.Position.Z);
            CheckAttachment();
            Assert.False(player.TauntComplete);
            player.RunIntoEndZone(3f, -1f);
            Assert.True(player.TauntComplete);
            Assert.Equal(clocks["TauntBicepFlex"].FrameCount - 1f, clocks["TauntBicepFlex"].CurrentFrame, 4);
            CheckAttachment();
            player.Reset();
            Assert.False(player.IsTaunting);
            Assert.False(player.TauntComplete);
            Assert.Equal(startPose, Vertices(player));
            CheckAttachment();
            Assert.Equal(Matrix4x4.Identity, assets.GetModel("Football").Transform);
        }
        finally
        {
            assets.UnloadAll();
            Raylib.CloseWindow();
        }
    }
}
