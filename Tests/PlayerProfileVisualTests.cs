using System.Numerics;
using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.ThreeD;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class PlayerProfileVisualTests : IDisposable
{
    private readonly AssetManager _assets;
    public PlayerProfileVisualTests()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Profile compatibility");
        _assets = new AssetManager(AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")));
        _assets.RequireAssets(Opponent.AnimationAssetKeys);
        _assets.RequireAssets("FootballPlayer", "Football", "OffenseUniform", "DefenseUniform",
            "FootballPlayerCarryJogAnimations", "FootballPlayerCarryRunAnimations", "FootballPlayerCarrySprintAnimations",
            "FootballPlayerCutLeftAnimations", "FootballPlayerCutRightAnimations",
            "FootballPlayerJukeLeftAnimations", "FootballPlayerJukeRightAnimations",
            "FootballPlayerSpinLeftAnimations", "FootballPlayerSpinRightAnimations");
        while (!_assets.ProcessNext()) { }
    }
    public void Dispose() { _assets.UnloadAll(); Raylib.CloseWindow(); }
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    [Theory]
    [InlineData(1.65f, 70f, -1f)]
    [InlineData(2.05f, 160f, 1f)]
    [InlineData(1.65f, 160f, 1f)]
    [InlineData(2.05f, 70f, -1f)]
    public unsafe void RealAnimationsAttachmentUniformsAndRagdollRecoverySupportSizedPlayers(float height, float weight, float build)
    {
        var profile = new PlayerProfile { Name = "Sized player", Height = height, Weight = weight, Strength = weight > 100 ? 100 : 1, Build = build, JerseyNumber = 27 };
        VerifySizedPlayer(profile);
    }

    [Theory]
    [InlineData("marcus-reed")]
    [InlineData("eli-brooks")]
    [InlineData("jalen-price")]
    [InlineData("darius-stone")]
    [InlineData("noah-grant")]
    public void ConfiguredReturnersKeepAnimationsContactsAttachmentAndRecoveryAligned(string id)
    {
        var catalog = ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json"));
        VerifySizedPlayer(catalog.Resolve(id).Profile);
    }

    private void VerifySizedPlayer(PlayerProfile profile)
    {
        float height = profile.Height, weight = profile.Weight;
        var config = new TackleAlleyConfig { BallCarrierProfile = profile };
        using var carrier = new BallCarrier(config);
        using var defender = new Opponent(Vector3.Zero, config, profile with { JerseyNumber = 91 });
        carrier.InitializeVisual(_assets); defender.InitializeVisual(_assets);
        carrier.ApplyUniform(_assets, "OffenseUniform");
        defender.ApplyUniform(_assets, "DefenseUniform");
        var model = Field<ModelInstance>(carrier, "_model").Model;
        var scale = Field<Vector3>(carrier, "_modelScale");
        Assert.Equal(scale, Field<Vector3>(defender, "_modelScale"));
        var bounds = Raylib.GetModelBoundingBox(model);
        Assert.Equal(height, (bounds.Max.Y - bounds.Min.Y) * scale.Y, 4);
        Assert.Equal(0, bounds.Min.Y * scale.Y + Field<float>(carrier, "_groundOffset"), 4);
        var grip = (Matrix4x4)typeof(BallCarrier).GetField("FootballGripLocal", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var worldProperty = typeof(BallCarrier).GetProperty("PlayerWorldTransform", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var attach = typeof(BallCarrier).GetMethod("UpdateFootballAttachment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var activeField = typeof(BallCarrier).GetField("_animation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var clip in Field<Dictionary<string, AnimationPlayer>>(carrier, "_animations").Values)
        {
            activeField.SetValue(carrier, clip);
            foreach (float phase in new[] { 0f, .25f, .5f, .9f })
            {
                clip.SeekPhase(phase);
                attach.Invoke(carrier, null);
                Assert.True(clip.TryGetBoneTransform("Hand.R", out var hand));
                var world = (Matrix4x4)worldProperty.GetValue(carrier)!;
                Assert.Equal(grip * hand * world, Field<Matrix4x4>(carrier, "_footballWorldTransform"));
                AssertFinite(model);
                var contact = (Ragdoll)typeof(BallCarrier).GetMethod("ContactPose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(carrier, null)!;
                Assert.Same(carrier.Physical, contact.Physical);
                foreach (var (bodyIndex, startBone, endBone) in new[]
                {
                    (0, "Hips", "Chest"), (1, "Chest", "Neck"),
                    (3, "UpperArm.L", "LowerArm.L"), (4, "LowerArm.L", "Hand.L"),
                    (5, "UpperArm.R", "LowerArm.R"), (6, "LowerArm.R", "Hand.R"),
                    (7, "UpperLeg.L", "LowerLeg.L"), (8, "LowerLeg.L", "Foot.L"),
                    (9, "UpperLeg.R", "LowerLeg.R"), (10, "LowerLeg.R", "Foot.R")
                })
                {
                    Assert.True(clip.TryGetBoneTransform(startBone, out var start));
                    Assert.True(clip.TryGetBoneTransform(endBone, out var end));
                    Vector3 centre = ((start * world).Translation + (end * world).Translation) * .5f;
                    Assert.InRange(Vector3.Distance(centre, contact.Bodies[bodyIndex].Position), 0, .00001f);
                }
            }
        }
        foreach (var clip in Field<Dictionary<string, AnimationPlayer>>(defender, "_animations").Values)
        {
            clip.SeekPhase(.5f);
            defender.HasBodyContact(carrier);
            AssertFinite(Field<ModelInstance>(defender, "_model").Model);
        }
        carrier.Reset(); defender.Reset();
        var uniform = Field<Texture2D>(carrier, "_numberedUniform");
        Assert.True(Raylib.IsTextureValid(uniform));
        Assert.NotEqual(uniform.Id, Field<Texture2D>(defender, "_numberedUniform").Id);
        defender.ApplyUniform(_assets, "OffenseUniform");
        Assert.True(Raylib.IsTextureValid(uniform)); // Other player's swap cannot release ours.
        var before = Vertices(model);
        var contactBefore = ContactCapsule.Capture((Ragdoll)typeof(BallCarrier)
            .GetMethod("ContactPose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(carrier, null)!);
        Assert.True(carrier.ActivateRagdoll());
        var contactAfter = ContactCapsule.Capture(carrier.Ragdoll);
        Assert.Equal(contactBefore.Length, contactAfter.Length);
        foreach (var (a, b) in contactBefore.Zip(contactAfter))
        {
            Assert.InRange(Vector3.Distance(a.Start, b.Start), 0, .00001f);
            Assert.InRange(Vector3.Distance(a.End, b.End), 0, .00001f);
            Assert.Equal(a.Radius, b.Radius, 5);
        }
        var bridge = Field<RagdollSkeleton>(carrier, "_ragdollSkeleton");
        bridge.Apply(model, carrier.Ragdoll);
        AssertClose(before, Vertices(model));
        Assert.True(defender.ActivateRagdoll());
        Assert.Equal(weight, carrier.Ragdoll.Bodies.Sum(b => b.Mass), 4);
        Assert.Equal(weight, defender.Ragdoll.Bodies.Sum(b => b.Mass), 4);
        var physical = carrier.Physical;
        bool carrierRecovered = false, defenderRecovered = false;
        for (int tick = 0; tick < 1800 && !(carrierRecovered && defenderRecovered); tick++)
        {
            carrier.UpdatePhysicsAndRecovery(Ragdoll.FixedStep);
            defender.Update(Vector3.Zero, Ragdoll.FixedStep);
            if (tick % 30 == 0)
            {
                Raylib.BeginDrawing(); carrier.Draw(); defender.Draw(); Raylib.EndDrawing();
                AssertFinite(model);
                AssertFinite(Field<ModelInstance>(defender, "_model").Model);
            }
            carrierRecovered |= !carrier.Ragdoll.IsActive && !carrier.IsRecovering;
            defenderRecovered |= !defender.Ragdoll.IsActive && !defender.IsRecovering;
        }
        Assert.True(carrierRecovered);
        Assert.True(defenderRecovered);
        carrier.Reset(); defender.Reset();
        Assert.Same(physical, carrier.Physical);
        Assert.Equal(profile, carrier.Profile);
        Assert.Equal(91, defender.Profile.JerseyNumber);
        Assert.Equal(scale, Field<Vector3>(carrier, "_modelScale"));
        Assert.Equal(uniform.Id, Field<Texture2D>(carrier, "_numberedUniform").Id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void RatedLocomotionPlaybackMatchesPaceAndActionsKeepTheirDuration(int rating)
    {
        var profile = new PlayerProfile { Speed = rating, Acceleration = rating, Agility = rating };
        var config = new TackleAlleyConfig { BallCarrierProfile = profile, OpponentInitialYawDegrees = 0 };
        using var carrier = new BallCarrier(config);
        using var defender = new Opponent(Vector3.Zero, config, profile);
        carrier.InitializeVisual(_assets); defender.InitializeVisual(_assets);
        var input = new RaylibGameFramework.Input.InputController(
            RaylibGameFramework.Input.InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")));
        var field = new FootballField(config, _assets);
        input.Update();
        carrier.Update(input, .1f, field);
        defender.Update(new(0, 0, -40), .1f, false, Vector2.Zero, false);
        Assert.Equal(.1f * carrier.Movement.RunningSpeedMultiplier, Field<AnimationPlayer>(carrier, "_animation").CurrentTime, 5);
        Assert.Equal(.1f * defender.Movement.RunningSpeedMultiplier, Field<AnimationPlayer>(defender, "_animation").CurrentTime, 5);
        Assert.InRange(carrier.LocomotionPlaybackRate, .35f, 1.5f);
        Assert.InRange(defender.LocomotionPlaybackRate, .35f, 1.5f);

        // Start the existing cut state and verify its authored clock is not speed-scaled.
        typeof(BallCarrier).GetField("_cutName", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(carrier, "CutRight");
        typeof(BallCarrier).GetField("_cutRemaining", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(carrier, config.PlayerCutDuration);
        carrier.Update(input, .1f, field);
        Assert.Equal("CutRight", carrier.AnimationName);
        Assert.Equal(.1f, Field<AnimationPlayer>(carrier, "_animation").CurrentTime, 5);
        Assert.Equal(config.PlayerCutDuration - .1f, Field<float>(carrier, "_cutRemaining"), 5);
    }

    [Fact]
    public void PowerReturnerResistsTheSameContactThatTakesDownTheLightReturner()
    {
        var catalog = ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json"));
        var light = ConfirmContact(catalog.Resolve("marcus-reed").Profile, .5f);
        var power = ConfirmContact(catalog.Resolve("darius-stone").Profile, .5f);
        Assert.True(light.Down);
        Assert.False(power.Down);
        Assert.True(power.Impact.ResistanceScore > light.Impact.ResistanceScore * 2);
        Assert.True(power.Impact.Severity < light.Impact.Severity);

        // Isolate Strength from body weight and movement ratings.
        var weak = ConfirmContact(new() { Strength = 1 }, .2f);
        var strong = ConfirmContact(new() { Strength = 100 }, .2f);
        Assert.True(weak.Down);
        Assert.False(strong.Down);
        Assert.Equal(weak.Impact.ImpactScore, strong.Impact.ImpactScore);
        Assert.True(strong.Impact.ResistanceScore > weak.Impact.ResistanceScore);
    }

    private (bool Down, TackleImpact Impact) ConfirmContact(PlayerProfile profile, float closingSpeed)
    {
        var config = new TackleAlleyConfig { OpponentInitialYawDegrees = -90 };
        using var carrier = new BallCarrier(config, profile);
        using var defender = new Opponent(new(-.3f, 0, 0), config, new());
        carrier.InitializeVisual(_assets);
        defender.InitializeVisual(_assets);
        var point = new Vector3(0, 1, 0);
        var contact = new SweptContact(0, point, point, Vector3.UnitX, 1, 1,
            TackleBodyRegion.Torso, TackleBodyRegion.Torso, Vector3.UnitX * closingSpeed);
        bool down = LungeTackleOutcome.Confirm(defender, carrier, contact);
        Assert.Equal(down, carrier.Ragdoll.IsActive);
        Assert.NotNull(carrier.LastTackle);
        return (down, carrier.LastTackle.Value);
    }

    [Fact]
    public unsafe void AffineSkinningMatchesNativeAtHandoff()
    {
        using var player = new BallCarrier(new());
        player.InitializeVisual(_assets);
        var model = Field<ModelInstance>(player, "_model").Model;
        var clips = Field<Dictionary<string, AnimationPlayer>>(player, "_animations");
        foreach (var clip in clips.Values)
        {
            clip.SeekPhase(.4f);
            var before = Vertices(model);
            AffinePose.Apply(model, RagdollPose.Snapshot(model, clip));
            AssertClose(before, Vertices(model));
        }
    }

    [Fact]
    public void DifferentNumbersChangeBothPanelsWithoutChangingTeamArtwork()
    {
        Image source = Raylib.LoadImageFromTexture(_assets.GetTexture("OffenseUniform"));
        Image first = Raylib.ImageCopy(source), second = Raylib.ImageCopy(source);
        try
        {
            PlayerUniform.PaintNumber(ref first, 7);
            PlayerUniform.PaintNumber(ref second, 99);
            foreach (float center in new[] { .16f, .48f })
            {
                bool different = false;
                for (int y = 250; y < 590; y += 10)
                    for (int x = (int)(center * 2048) - 260; x < center * 2048 + 260; x += 10)
                        different |= !Raylib.GetImageColor(first, x, y).Equals(Raylib.GetImageColor(second, x, y));
                Assert.True(different);
            }
            Assert.Equal(Raylib.GetImageColor(source, 1800, 1000), Raylib.GetImageColor(first, 1800, 1000));
            Assert.Equal(Raylib.GetImageColor(source, 1800, 1000), Raylib.GetImageColor(second, 1800, 1000));
        }
        finally { Raylib.UnloadImage(source); Raylib.UnloadImage(first); Raylib.UnloadImage(second); }
    }

    private static unsafe float[] Vertices(Model model)
    {
        var result = new List<float>();
        for (int m = 0; m < model.MeshCount; m++)
            if (model.Meshes[m].AnimVertices != null)
                result.AddRange(new ReadOnlySpan<float>(model.Meshes[m].AnimVertices, model.Meshes[m].VertexCount * 3).ToArray());
        return result.ToArray();
    }
    private static void AssertFinite(Model model) => Assert.All(Vertices(model), v => Assert.True(float.IsFinite(v)));
    private static void AssertClose(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        Assert.InRange(expected.Zip(actual).Max(p => Math.Abs(p.First - p.Second)), 0, .0005f);
    }
}
