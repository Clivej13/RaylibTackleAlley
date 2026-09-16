using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class RagdollSkeletonTests
{
    private static T Field<T>(Opponent d, string name) =>
        (T)typeof(Opponent).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(d)!;
    private static void Set(Opponent d, string name, object value) =>
        typeof(Opponent).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(d, value);

    [Theory]
    [InlineData("Jog", .137f, 37f)]
    [InlineData("Run", .21f, -113f)]
    [InlineData("Sprint", .31f, 180f)]
    [InlineData("LungeTackleForward", .25f, 73f)]
    [InlineData("Down", .1f, -47f)]
    public unsafe void NativeMeshPreservesActivationFollowsBodiesAndResets(string clipName, float time, float yaw)
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(320, 320, "Ragdoll skeleton");
        var assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        var target = Raylib.LoadRenderTexture(320, 320);
        try
        {
            assets.RequireAssets(Opponent.AnimationAssetKeys);
            while (!assets.ProcessNext()) { }
            var defender = new Opponent(new(7, 0, -11), new());
            defender.InitializeVisual(assets);
            defender.Update(new(9, 0, -40), .1f, false, Vector2.Zero, false);
            Set(defender, "_yawDegrees", yaw);
            var animation = Field<Dictionary<string, AnimationPlayer>>(defender, "_animations")[clipName];
            animation.SeekTime(time);
            Set(defender, "_animation", animation);
            var other = new Opponent(new(-5, 0, 8), new());
            other.InitializeVisual(assets);
            var model = Field<ModelInstance>(defender, "_model").Model;
            var otherModel = Field<ModelInstance>(other, "_model").Model;
            var unchangedOther = Vertices(otherModel);
            var originalModelTransform = model.Transform;
            var skeleton = model.Skeleton;
            var names = new string[skeleton.BoneCount];
            var initial = new Matrix4x4[skeleton.BoneCount];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = new string(skeleton.Bones[i].Name);
                Assert.True(animation.TryGetBoneTransform(names[i], out initial[i]));
            }
            var originalVertices = Vertices(model);
            var camera = new Camera3D {
                Position = defender.Position + new Vector3(3, 2, 4),
                Target = defender.Position + Vector3.UnitY, Up = Vector3.UnitY,
                FovY = 45, Projection = CameraProjection.Perspective
            };
            byte[] Capture()
            {
                Raylib.BeginTextureMode(target); Raylib.ClearBackground(Color.Magenta);
                Raylib.BeginMode3D(camera); defender.Draw(); Raylib.EndMode3D(); Raylib.EndTextureMode();
                var image = Raylib.LoadImageFromTexture(target.Texture);
                var pixels = Raylib.LoadImageColors(image);
                try {
                    var bytes = new byte[320 * 320 * 4];
                    Marshal.Copy((IntPtr)pixels, bytes, 0, bytes.Length); return bytes;
                } finally { Raylib.UnloadImageColors(pixels); Raylib.UnloadImage(image); }
            }
            byte[] beforeImage = Capture();
            Assert.True(defender.ActivateRagdoll());
            var bridge = Field<RagdollSkeleton>(defender, "_ragdollSkeleton");
            Assert.Equal(12, bridge.BodyIndices.Count(b => b >= 0)); // 11 bodies plus Root alias
            for (int i = 0; i < names.Length; i++)
            {
                Close(initial[i], bridge.ModelPose[i]);
                int body = bridge.BodyIndices[i];
                if (body >= 0) Assert.Equal(names[i] == "Root" ? "Hips" : names[i], defender.Ragdoll.Bodies[body].Bone);
            }
            var offsets = bridge.ModelPose.Select((pose, i) => {
                int body = bridge.BodyIndices[i];
                if (body < 0) return Matrix4x4.Identity;
                Matrix4x4.Invert(RagdollSkeleton.BodyWorld(defender.Ragdoll.Bodies[body]), out var inverse);
                return pose * bridge.ModelWorld * inverse;
            }).ToArray();
            byte[] activatedImage = Capture();
            var activatedVertices = Vertices(model);
            Assert.Equal(originalVertices.Length, activatedVertices.Length);
            for (int v = 0; v < originalVertices.Length; v++)
                Assert.True(Vector3.Distance(originalVertices[v], activatedVertices[v]) < .0002f, $"Activation vertex {v}");
            int changedPixels = 0, visiblePixels = 0;
            for (int p = 0; p < beforeImage.Length; p += 4)
            {
                if (beforeImage[p] != 255 || beforeImage[p + 1] != 0 || beforeImage[p + 2] != 255) visiblePixels++;
                if (beforeImage[p] != activatedImage[p] || beforeImage[p+1] != activatedImage[p+1] ||
                    beforeImage[p+2] != activatedImage[p+2]) changedPixels++;
            }
            Assert.True(visiblePixels > 500, $"Empty model render: {visiblePixels}");
            Assert.True(changedPixels < visiblePixels * .015f, $"Activation changed {changedPixels}/{visiblePixels} pixels");

            int head = Array.IndexOf(names, "Head");
            var headLocal = BoneLocalVertices(model, head, initial[head]);
            var rigidVertices = Enumerable.Range(0, names.Length).Where(i => bridge.BodyIndices[i] >= 0 && names[i] != "Root")
                .ToDictionary(i => i, i => BoneLocalVertices(model, i, initial[i]));
            Assert.All(rigidVertices, pair => Assert.NotEmpty(pair.Value));
            Assert.True(headLocal.Length > 100); // includes helmet, visor, facemask and chinstrap
            defender.Ragdoll.ApplyImpulse(new(1, RagdollDebugControls.TestImpulse(yaw)));
            for (int step = 0; step < 1800; step++)
            {
                // Inspect the settled physics pose before the gameplay recovery handoff.
                defender.Ragdoll.Update(1f / 120);
                if (step % 60 != 0 && step != 1799) continue;
                bridge.Apply(model, defender.Ragdoll);
                for (int i = 0; i < names.Length; i++)
                {
                    int body = bridge.BodyIndices[i], parent = skeleton.Bones[i].Parent;
                    if (body >= 0)
                    {
                        Matrix4x4.Invert(RagdollSkeleton.BodyWorld(defender.Ragdoll.Bodies[body]), out var inverse);
                        Close(offsets[i], bridge.ModelPose[i] * bridge.ModelWorld * inverse, .0005f);
                    }
                    else if (parent >= 0)
                    {
                        Matrix4x4.Invert(initial[parent], out var inverseStartParent);
                        Matrix4x4.Invert(bridge.ModelPose[parent], out var inverseParent);
                        Close(initial[i] * inverseStartParent, bridge.ModelPose[i] * inverseParent, .0005f);
                    }
                    Assert.True(Matrix4x4.Decompose(bridge.ModelPose[i], out var scale, out _, out _));
                    Matrix4x4.Decompose(initial[i], out var initialScale, out _, out _);
                    Assert.True(Vector3.Distance(scale, initialScale) < .0005f, $"Bone scale changed: {names[i]}");
                }
                // Check actual skinned helmet vertices, not only the pose matrices.
                var headNow = BoneLocalVertices(model, head, bridge.ModelPose[head]);
                for (int v = 0; v < headLocal.Length; v++)
                    Assert.True(Vector3.Distance(headLocal[v], headNow[v]) < .0005f, $"Head vertex drift: {v}");
                foreach (var (bone, localVertices) in rigidVertices)
                {
                    var now = BoneLocalVertices(model, bone, bridge.ModelPose[bone]);
                    for (int v = 0; v < localVertices.Length; v++)
                        Assert.True(Vector3.Distance(localVertices[v], now[v]) < .0005f, $"Rigid vertex drift: {names[bone]} / {v}");
                }
                // No mesh vertex escapes the local body envelope.
                foreach (var vertex in Vertices(model))
                {
                    var worldVertex = Vector3.Transform(vertex, bridge.ModelWorld);
                    Assert.True(float.IsFinite(worldVertex.LengthSquared()));
                    Assert.True(defender.Ragdoll.Bodies.Min(b => Vector3.Distance(b.Position, worldVertex)) < 1f);
                }
            }
            Assert.Equal(RagdollState.Settled, defender.Ragdoll.State);
            Assert.Equal(time, animation.CurrentTime);
            Assert.Equal(originalModelTransform, model.Transform);
            Assert.Equal(unchangedOther, Vertices(otherModel));
            Assert.True(Vector3.Distance(initial[head].Translation, bridge.ModelPose[head].Translation) > .2f);
            Capture(); // normal model at its final physics pose, through the real Draw path

            defender.Reset();
            Assert.Null(typeof(Opponent).GetField("_ragdollSkeleton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(defender));
            var resetVertices = Vertices(model);
            var current = Field<AnimationPlayer>(defender, "_animation");
            Raylib.UpdateModelAnimation(model, current.Animation, current.CurrentFrame);
            Assert.Equal(resetVertices, Vertices(model)); // Reset already restored the exact animated pose
            defender.Update(new(7, 0, -40), .1f, false, Vector2.Zero, false);
            Assert.True(current.CurrentTime > 0);

            // Activation at cached frame zero followed by immediate reset must also restore ownership.
            defender.Reset();
            var zero = Vertices(model);
            Assert.True(defender.ActivateRagdoll());
            defender.Update(Vector3.Zero, .2f); Capture();
            defender.Reset();
            Assert.Equal(zero, Vertices(model));
            Assert.True(defender.ActivateRagdoll());
            defender.Update(Vector3.Zero, .2f); Capture();
            defender.Ragdoll.Deactivate(); Capture();
            Assert.Equal(zero, Vertices(model)); // Direct deactivation re-applies the frozen animation frame.
        }
        finally { Raylib.UnloadRenderTexture(target); assets.UnloadAll(); Raylib.CloseWindow(); }
    }

    private static unsafe Vector3[] Vertices(Model model)
    {
        var values = new List<Vector3>();
        for (int m = 0; m < model.MeshCount; m++)
        {
            var mesh = model.Meshes[m];
            if (mesh.AnimVertices == null) continue;
            for (int v = 0; v < mesh.VertexCount; v++)
                values.Add(new(mesh.AnimVertices[v*3], mesh.AnimVertices[v*3+1], mesh.AnimVertices[v*3+2]));
        }
        return values.ToArray();
    }

    private static unsafe Vector3[] BoneLocalVertices(Model model, int head, Matrix4x4 pose)
    {
        Matrix4x4.Invert(pose, out var inverse);
        var values = new List<Vector3>();
        for (int m = 0; m < model.MeshCount; m++)
        {
            var mesh = model.Meshes[m];
            if (mesh.AnimVertices == null || mesh.BoneIndices == null || mesh.BoneWeights == null) continue;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                float weight = 0;
                for (int s = 0; s < 4; s++)
                    if (mesh.BoneIndices[v*4+s] == head) weight += mesh.BoneWeights[v*4+s];
                if (weight < .99999f) continue;
                values.Add(Vector3.Transform(new(mesh.AnimVertices[v*3], mesh.AnimVertices[v*3+1], mesh.AnimVertices[v*3+2]), inverse));
            }
        }
        return values.ToArray();
    }

    private static void Close(Matrix4x4 expected, Matrix4x4 actual, float tolerance = .0001f)
    {
        foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            Assert.True(Vector3.Distance(Vector3.Transform(point, expected), Vector3.Transform(point, actual)) < tolerance,
                $"Transform mismatch: {expected} vs {actual}");
    }
}
