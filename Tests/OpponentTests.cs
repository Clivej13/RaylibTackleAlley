using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class OpponentTests
{
    [Theory]
    [InlineData(30f, OpponentPace.Jog, 4f)]
    [InlineData(20f, OpponentPace.Run, 6.5f)]
    [InlineData(12f, OpponentPace.Run, 6.5f)]
    [InlineData(8f, OpponentPace.Sprint, 9f)]
    [InlineData(3f, OpponentPace.Sprint, 9f)]
    public void DistanceSelectsPaceAndGameDrivenSpeed(float distance, OpponentPace pace, float speed)
    {
        var opponent = new Opponent(Vector3.Zero, new());
        opponent.Update(new(0, 0, -distance), 0.1f);
        Assert.Equal(pace, opponent.Pace);
        Assert.Equal(speed, opponent.MovementSpeed);
        Assert.Equal(-speed * 0.1f, opponent.Position.Z, 5);
        Assert.Equal(0f, opponent.Position.Y);
    }

    [Fact]
    public void SeparationSlowsDownWithSmallHysteresisAndResetRestoresSpawn()
    {
        var opponent = new Opponent(Vector3.Zero, new());
        void At(float distance) => opponent.Update(new(0, 0, -distance), 0f);
        At(8); Assert.Equal(OpponentPace.Sprint, opponent.Pace);
        At(8.5f); Assert.Equal(OpponentPace.Sprint, opponent.Pace);
        At(8.51f); Assert.Equal(OpponentPace.Run, opponent.Pace);
        At(20.5f); Assert.Equal(OpponentPace.Run, opponent.Pace);
        At(20.51f); Assert.Equal(OpponentPace.Jog, opponent.Pace);
        At(20.1f); Assert.Equal(OpponentPace.Jog, opponent.Pace);
        opponent.Update(new(0, 0, -5), 0.1f);
        opponent.Reset();
        Assert.Equal(Vector3.Zero, opponent.Position);
        Assert.Equal(OpponentPace.Jog, opponent.Pace);
    }

    [Theory]
    [InlineData(30f, "Jog")]
    [InlineData(20f, "Run")]
    [InlineData(8f, "Sprint")]
    public void PaceSelectsApprovedAnimation(float distance, string clip)
    {
        var opponent = new Opponent(Vector3.Zero, new());
        opponent.Update(new(0, 0, -distance), 0);
        Assert.Equal(clip, opponent.AnimationName);
    }

    [Fact]
    public void DefaultLocomotionTuningIsUnchanged()
    {
        var config = new TackleAlleyConfig();
        Assert.Equal(4f, config.OpponentJogSpeed);
        Assert.Equal(6.5f, config.OpponentRunSpeed);
        Assert.Equal(9f, config.OpponentSprintSpeed);
        Assert.Equal(20f, config.OpponentRunDistance);
        Assert.Equal(8f, config.OpponentSprintDistance);
        Assert.Equal(0.5f, config.OpponentPaceHysteresis);
    }

    [Fact]
    public void ConfiguredSpeedsAndBandsAreUsed()
    {
        var config = new TackleAlleyConfig {
            OpponentJogSpeed = 1, OpponentRunSpeed = 2, OpponentSprintSpeed = 3,
            OpponentRunDistance = 10, OpponentSprintDistance = 4
        };
        foreach (var (distance, speed) in new[] { (15f, 1f), (7f, 2f), (2f, 3f) })
        {
            var opponent = new Opponent(Vector3.Zero, config);
            opponent.Update(new(distance, 0, 0), 0.1f);
            Assert.Equal(speed * 0.1f, opponent.Position.X, 5);
        }
    }

    [Theory]
    [InlineData(20f, 20f)]
    [InlineData(21f, 20f)]
    [InlineData(float.NaN, 20f)]
    [InlineData(8f, float.PositiveInfinity)]
    public void InvalidBandsAreRejected(float sprint, float run) =>
        Assert.Throws<ArgumentException>(() => new Opponent(Vector3.Zero,
            new() { OpponentSprintDistance = sprint, OpponentRunDistance = run }));

    [Fact]
    public void TackleRadiusIsStillOnePointEightRegardlessOfPace()
    {
        var opponent = new Opponent(Vector3.Zero, new());
        Assert.Equal(1.8f, new TackleAlleyConfig().TackleDistance);
        foreach (float distance in new[] { 30f, 12f, 3f })
        {
            opponent.Update(new(0, 0, -distance), 0);
            Assert.True(opponent.IsTouching(new(1.8f, 0, 0)));
            Assert.False(opponent.IsTouching(new(1.801f, 0, 0)));
        }
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    // Compare actual native deformed vertices, so restarting every update cannot pass.
    private static unsafe float[] Vertices(Opponent opponent)
    {
        var model = Field<ModelInstance>(opponent, "_model").Model;
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

    [Fact]
    public void ApprovedClipsShareModelWithIndependentContinuousPlaybackAndNoWorldDrift()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Opponent animation validation");
        var assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        try
        {
            assets.RequireAssets("FootballPlayerAnimations", "FootballPlayerRunAnimations", "FootballPlayerSprintAnimations");
            while (!assets.ProcessNext()) { }
            var opponents = Enumerable.Range(0, 4).Select(_ => new Opponent(Vector3.Zero, new())).ToArray();
            foreach (var opponent in opponents) opponent.InitializeVisual(assets);
            for (int i = 0; i < opponents.Length; i++)
                opponents[i].Update(new(0, 0, -(i == 0 ? 3 : i == 1 ? 12 : 30)), 0);
            Assert.Equal(new[] { OpponentPace.Sprint, OpponentPace.Run, OpponentPace.Jog, OpponentPace.Jog },
                opponents.Select(o => o.Pace));
            Assert.Equal(new[] { "Sprint", "Run", "Jog", "Jog" }, opponents.Select(o => o.AnimationName));
            Assert.NotSame(Field<ModelInstance>(opponents[0], "_model"), Field<ModelInstance>(opponents[1], "_model"));
            Assert.NotSame(Field<AnimationPlayer>(opponents[0], "_animation"), Field<AnimationPlayer>(opponents[1], "_animation"));

            foreach (float distance in new[] { 30f, 12f, 3f })
            {
                var a = opponents[0]; var b = opponents[1];
                a.Reset(); b.Reset();
                a.Update(new(0, 0, -distance), 0);
                b.Update(new(0, 0, -distance), 0);
                var untouched = Vertices(b);
                for (int i = 0; i < 6; i++) a.Update(a.Position + new Vector3(0, 0, -distance), 0.025f);
                Assert.Equal(untouched, Vertices(b));
                b.Update(new(0, 0, -distance), 0.15f);
                var actual = Vertices(a); var expected = Vertices(b);
                Assert.True(actual.Zip(expected).All(pair => MathF.Abs(pair.First - pair.Second) < 0.0001f));
                Assert.Contains(actual.Zip(untouched), pair => MathF.Abs(pair.First - pair.Second) > 0.001f);
                Assert.Equal(-a.MovementSpeed * 0.15f, a.Position.Z, 4);
                Assert.Equal(0f, a.Position.Y);
                Assert.Equal(0f, Field<float>(a, "_yawDegrees"), 4);
            }
            // Each pace transition selects its own clip and seeks once, without
            // changing another defender's pose or replacing the football model.
            var runner = opponents[2];
            var reference = opponents[3];
            var model = Field<ModelInstance>(runner, "_model");
            foreach (var (distance, name) in new[] {
                (12f, "Run"), (3f, "Sprint"), (8.51f, "Run"), (20.51f, "Jog") })
            {
                var previous = Field<AnimationPlayer>(runner, "_animation");
                var otherPose = Vertices(opponents[1]);
                runner.Update(runner.Position + new Vector3(0, 0, -distance), 0);
                Assert.Equal(name, runner.AnimationName);
                Assert.NotSame(previous, Field<AnimationPlayer>(runner, "_animation"));
                Assert.Same(model, Field<ModelInstance>(runner, "_model"));
                Assert.Equal(otherPose, Vertices(opponents[1]));

                reference.Reset();
                reference.Update(new(0, 0, -distance), 0);
                runner.Update(runner.Position + new Vector3(0, 0, -distance), 0.15f);
                reference.Update(reference.Position + new Vector3(0, 0, -distance), 0.15f);
                Assert.Equal(Vertices(reference), Vertices(runner));
            }
        }
        finally
        {
            assets.UnloadAll();
            Raylib.CloseWindow();
        }
    }
}
