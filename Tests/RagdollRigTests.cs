using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class RagdollRigTests
{
    [Theory]
    [InlineData("Jog", .137f, false)]
    [InlineData("Run", .21f, true)]
    [InlineData("Sprint", .31f, false)]
    [InlineData("LungeTackleForward", .25f, true)]
    [InlineData("Down", .1f, false)]
    public void CurrentAnimatedRigIsCapturedWithoutAdvancingAndRemainsIndependent(string clipName, float poseTime, bool impulse)
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Ragdoll rig validation");
        var assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        try
        {
            assets.RequireAssets(Opponent.AnimationAssetKeys);
            while (!assets.ProcessNext()) { }
            var defender = new Opponent(new(4, 0, -3), new());
            defender.InitializeVisual(assets);
            var other = new Opponent(new(10, 0, 0), new());
            other.InitializeVisual(assets);
            defender.Update(new(4, 0, -30), .137f, false, Vector2.Zero, false);
            var animations = (Dictionary<string, AnimationPlayer>)typeof(Opponent).GetField("_animations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(defender)!;
            var animation = animations[clipName];
            animation.SeekTime(poseTime);
            typeof(Opponent).GetField("_animation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(defender, animation);
            float time = animation.CurrentTime;
            Assert.True(animation.TryGetBoneTransform("Hips", out var hips));
            Assert.True(animation.TryGetBoneTransform("Chest", out var chest));
            float scale = (float)typeof(Opponent).GetField("_visualScale", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(defender)!;
            float offset = (float)typeof(Opponent).GetField("_groundOffset", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(defender)!;
            var instance = (ModelInstance)typeof(Opponent).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(defender)!;
            var world = instance.Model.Transform * Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateRotationY(defender.FacingYawDegrees * MathF.PI / 180) *
                Matrix4x4.CreateTranslation(defender.Position + Vector3.UnitY * offset);
            Vector3 velocity = defender.Velocity;
            Assert.True(defender.ActivateRagdoll());
            Assert.False(defender.ActivateRagdoll());
            Assert.Equal(time, animation.CurrentTime);
            Assert.True(Vector3.Distance(Vector3.Transform((hips.Translation + chest.Translation) / 2, world),
                defender.Ragdoll.Bodies[0].Position) < .00001f);
            Matrix4x4.Decompose(hips * world, out _, out var rotation, out _);
            Assert.True(Math.Abs(Quaternion.Dot(rotation, defender.Ragdoll.Bodies[0].Orientation)) > .99999f);
            Assert.All(defender.Ragdoll.Bodies, b => Assert.Equal(velocity, b.LinearVelocity));
            Assert.All(defender.Ragdoll.Joints, j => Assert.InRange(defender.Ragdoll.JointSeparation(j), 0, .00001f));
            if (impulse) defender.Ragdoll.ApplyImpulse(new(1, RagdollDebugControls.TestImpulse(defender.FacingYawDegrees)));
            var state = defender.State;
            Vector3 otherPosition = other.Position;
            for (int i = 0; i < 3600; i++)
            {
                // Foundation test keeps physics ownership; recovery is covered separately.
                defender.Ragdoll.Update(Ragdoll.FixedStep);
                Assert.All(defender.Ragdoll.Bodies, b => Assert.True(b.Bottom >= -.00001f));
                Assert.All(defender.Ragdoll.Joints, j => {
                    var a = defender.Ragdoll.JointAngles(j);
                    Assert.InRange(a.X, j.MinimumAngles.X - .001f, j.MaximumAngles.X + .001f);
                    Assert.InRange(a.Y, j.MinimumAngles.Y - .001f, j.MaximumAngles.Y + .001f);
                    Assert.InRange(a.Z, j.MinimumAngles.Z - .001f, j.MaximumAngles.Z + .001f);
                    Assert.True(defender.Ragdoll.JointSeparation(j) < .04f, $"Joint {j.Child}: {defender.Ragdoll.JointSeparation(j)}");
                });
            }
            Assert.Equal(RagdollState.Settled, defender.Ragdoll.State);
            Assert.Equal(time, animation.CurrentTime);
            Assert.Equal(state, defender.State);
            Assert.Equal(otherPosition, other.Position);
            Raylib.BeginDrawing(); defender.Draw(); Raylib.EndDrawing();
            defender.Reset();
            Assert.Equal(RagdollState.Inactive, defender.Ragdoll.State);
            defender.Update(new(4, 0, -30), .1f);
            Assert.True(((AnimationPlayer)typeof(Opponent).GetField("_animation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(defender)!).CurrentTime > 0);
            Raylib.BeginDrawing(); defender.Draw(); Raylib.EndDrawing();
        }
        finally { assets.UnloadAll(); Raylib.CloseWindow(); }
    }
}
