using System.Reflection;
using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Configuration;
using RaylibGameFramework.Input;
using RaylibGameFramework.Menus;
using RaylibTackleAlley.Application;
using RaylibTackleAlley.Game;
using Xunit;

public sealed class ReturnerMenuTests : IDisposable
{
    private readonly GameApplication _app;
    private readonly ReturnerCatalog _catalog;
    private readonly MenuManager _menu;
    private readonly InputController _input;
    private readonly TackleAlleyGame _game;
    private readonly AssetManager _assets;
    private readonly IDisposable _preview;

    public ReturnerMenuTests()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(640, 480, "Returner selection validation");
        Raylib.SetExitKey(KeyboardKey.Null);
        _catalog = ReturnerCatalog.Load(Path.Combine(AppContext.BaseDirectory, "returners.json"));
        var menus = MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json"));
        Assert.Equal(MenuLayout.ListWithDetail, menus.Menus["Returners"].Layout);
        Assert.Single(menus.Menus["Returners"].Items); // Only Back is authored in JSON.
        _app = new(new GameConfig(), InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")),
            menus, AssetConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "assets.json")), new(), _catalog,
            LevelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "levels.json"), new()).Resolve("level-1"));
        _menu = Field<MenuManager>(_app, "_mainMenu");
        _input = Field<InputController>(_app, "_input");
        _game = Field<TackleAlleyGame>(_app, "_game");
        _assets = Field<AssetManager>(_app, "_assets");
        _preview = Field<IDisposable>(_app, "_returnerPreview");
        Assert.Equal(_catalog.Returners.Count + 1, menus.Menus["Returners"].Items.Count);
        _input.Update();
        Frame();
    }

    public void Dispose()
    {
        _preview.Dispose();
        _game.Dispose();
        _assets.UnloadAll();
        Raylib.CloseWindow();
    }

    private static T Field<T>(object value, string name) =>
        (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Invoke(object value, string name, params object[] args) =>
        value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private string State => Field<object>(_app, "_state").ToString()!;
    private static unsafe void Event(uint type, int a, int b = 0)
    {
        var e = new AutomationEvent { Type = type };
        e.Params[0] = a; e.Params[1] = b;
        Raylib.PlayAutomationEvent(e);
    }
    private static void Frame() { Raylib.BeginDrawing(); Raylib.EndDrawing(); }
    private void Press(KeyboardKey key)
    {
        Event(2, (int)key); _input.Update(); Invoke(_app, "Update", 0f); Frame();
        Event(1, (int)key); _input.Update(); Invoke(_app, "Update", 0f); Frame();
    }

    [Fact]
    public void StartBackSelectRestartAndReselectUseRealMenuActions()
    {
        Press(KeyboardKey.Enter);
        Assert.Equal("Returners", _menu.CurrentMenuName);
        Assert.Equal("MainMenu", State);
        Assert.Null(_game.SelectedReturnerId);
        Press(KeyboardKey.Escape);
        Assert.Equal("Main", _menu.CurrentMenuName);
        Press(KeyboardKey.Enter);
        Press(KeyboardKey.Down);
        var selected = _catalog.Returners[1];
        Assert.Equal(selected.Id, _menu.SelectedItem!.Value.GetString());

        Event(2, (int)KeyboardKey.Enter); _input.Update();
        var action = _menu.Update();
        Assert.NotNull(action);
        Assert.Equal("SelectReturner", action.Function);
        Assert.Equal(selected.Id, Assert.IsType<System.Text.Json.JsonElement>(action.Value).GetString());
        Invoke(_app, "HandleMenu", action);
        Frame();
        Event(1, (int)KeyboardKey.Enter); _input.Update(); Frame();
        Assert.Equal("Playing", State);
        Assert.Equal(selected.Profile, _game.SelectedProfile);
        Assert.Equal(selected.Id, _game.SelectedReturnerId);
        var carrier = Field<BallCarrier>(_game, "_player");
        float startZ = carrier.Position.Z;
        _game.Update(.1f);
        Assert.Equal(carrier.Movement.RunningSpeed * .1f, startZ - carrier.Position.Z, 4);

        Press(KeyboardKey.Escape);
        Assert.Equal("Paused", State);
        Press(KeyboardKey.Down); Press(KeyboardKey.Enter); // Restart.
        Assert.Equal("Playing", State);
        Assert.Equal(selected.Id, _game.SelectedReturnerId);

        Press(KeyboardKey.Escape);
        Press(KeyboardKey.Down); Press(KeyboardKey.Down); Press(KeyboardKey.Enter); // Main Menu.
        Assert.Equal("MainMenu", State);
        Assert.Equal("Main", _menu.CurrentMenuName);
        Press(KeyboardKey.Enter);
        Assert.Equal("Returners", _menu.CurrentMenuName);
        Press(KeyboardKey.Enter);
        Assert.Equal("Playing", State);
        Assert.Equal(_catalog.Returners[0].Id, _game.SelectedReturnerId);
    }

    [Fact]
    public void GamepadCanHighlightSelectAndBackOut()
    {
        void Button(GamepadButton button)
        {
            Event(9, 0);
            Event(12, 0, (int)button); _input.Update(); Invoke(_app, "Update", 0f); Frame();
            Event(11, 0, (int)button); _input.Update(); Invoke(_app, "Update", 0f); Frame();
        }
        Button(GamepadButton.RightFaceDown);
        Assert.Equal("Returners", _menu.CurrentMenuName);
        Button(GamepadButton.RightFaceRight);
        Assert.Equal("Main", _menu.CurrentMenuName);
        Button(GamepadButton.RightFaceDown);
        Button(GamepadButton.LeftFaceDown);
        Assert.Equal(_catalog.Returners[1].Id, _menu.SelectedItem!.Value.GetString());
        Button(GamepadButton.RightFaceDown);
        Assert.Equal("Playing", State);
        Assert.Equal(_catalog.Returners[1].Id, _game.SelectedReturnerId);
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(1280, 720)]
    public void HighlightDrawsIntoDetailBoundsReusingOneModel(int width, int height)
    {
        Raylib.SetWindowSize(width, height);
        Frame();
        _assets.RequireAssets("FootballPlayer", "OffenseUniform", "FootballPlayerTauntBicepFlexAnimations");
        foreach (var entry in _catalog.Returners)
        {
            _assets.RequireAsset(entry.Uniform ?? "OffenseUniform");
            _assets.RequireAsset(entry.Taunt ?? "FootballPlayerTauntBicepFlexAnimations");
        }
        while (!_assets.ProcessNext()) { }
        var carrier = Field<BallCarrier>(_game, "_player");
        Press(KeyboardKey.Enter);
        ModelInstance? model = null;
        for (int i = 0; i < _catalog.Returners.Count; i++)
        {
            Raylib.BeginDrawing();
            _menu.Draw();
            Invoke(_app, "DrawReturnerDetail");
            Raylib.EndDrawing();
            var bounds = _menu.DetailPanelBounds!.Value;
            Assert.True(bounds.Width > 0 && bounds.Height > 0);
            Assert.InRange(bounds.X + bounds.Width, 1, width);
            Assert.InRange(bounds.Y + bounds.Height, 1, height);
            var current = Field<ModelInstance>(_preview, "_model");
            if (model is not null) Assert.Same(model, current);
            model = current;
            Assert.Equal(_catalog.Returners[i].Id, Field<string>(_preview, "_id"));
            var animation = Field<RaylibGameFramework.ThreeD.AnimationPlayer>(_preview, "_animation");
            Assert.Equal(0f, animation.CurrentFrame);
            unsafe
            {
                var clip = animation.Animation;
                Assert.Equal(_catalog.Returners[i].Taunt!.Replace("FootballPlayer", "").Replace("Animations", ""),
                    new string(clip.Name));
            }
            Invoke(_preview, "AdvanceAnimation", .5f);
            Assert.InRange(animation.CurrentFrame, 29.9f, 30.1f);
            float duration = animation.FrameCount / animation.FramesPerSecond;
            Invoke(_preview, "AdvanceAnimation", duration * 3);
            Assert.InRange(animation.CurrentFrame, 29.9f, 30.1f);
            Invoke(_preview, "AdvanceAnimation", float.NaN);
            Assert.True(float.IsFinite(animation.CurrentFrame));
            AssertUniformArtwork(Field<Texture2D>(_preview, "_numberedUniform"), _catalog.Returners[i]);
            Assert.Equal(_catalog.Returners[i].Profile, Field<PlayerVisualProfile>(_preview, "_visual").Profile);
            Assert.Same(carrier, Field<BallCarrier>(_game, "_player"));
            Assert.Null(_game.SelectedReturnerId);
            var target = Field<RenderTexture2D>(_preview, "_target");
            Assert.InRange(target.Texture.Width, 32, (int)bounds.Width);
            Assert.InRange(target.Texture.Height, 32, (int)bounds.Height);
            Image image = Raylib.LoadImageFromTexture(target.Texture);
            try
            {
                int rendered = 0;
                var background = new Color(17, 29, 42, 255);
                for (int y = 0; y < image.Height; y += 3)
                    for (int x = 0; x < image.Width; x += 3)
                        if (!Raylib.GetImageColor(image, x, y).Equals(background)) rendered++;
                Assert.True(rendered > 25, "The 3D preview must contain visible player pixels.");
            }
            finally { Raylib.UnloadImage(image); }
            Press(KeyboardKey.Down);
        }
        Assert.Equal("Back", _menu.SelectedItem!.Function);
        Raylib.BeginDrawing(); _menu.Draw(); Invoke(_app, "DrawReturnerDetail"); Raylib.EndDrawing();
        Assert.Null(Field<string?>(_preview, "_id"));
        Press(KeyboardKey.Up);
        Raylib.BeginDrawing(); _menu.Draw(); Invoke(_app, "DrawReturnerDetail"); Raylib.EndDrawing();
        Assert.Equal(0f, Field<RaylibGameFramework.ThreeD.AnimationPlayer>(_preview, "_animation").CurrentFrame);
        Assert.Same(model, Field<ModelInstance>(_preview, "_model"));
        Press(KeyboardKey.Down);
        Press(KeyboardKey.Enter);
        Assert.Equal("Main", _menu.CurrentMenuName);
        _preview.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = model!.Model; });
    }

    [Fact]
    public void SelectingWithLoadedVisualsReleasesOldCarrierAndAppliesNumberedUniform()
    {
        _assets.RequireAssets(Opponent.AnimationAssetKeys);
        _assets.RequireAssets("FootballPlayer", "Football", "OffenseUniform",
            "FootballPlayerCarryJogAnimations", "FootballPlayerCarryRunAnimations", "FootballPlayerCarrySprintAnimations",
            "FootballPlayerCutLeftAnimations", "FootballPlayerCutRightAnimations",
            "FootballPlayerJukeLeftAnimations", "FootballPlayerJukeRightAnimations",
            "FootballPlayerSpinLeftAnimations", "FootballPlayerSpinRightAnimations");
        foreach (var entry in _catalog.Returners)
        {
            _assets.RequireAsset(entry.Uniform ?? "OffenseUniform");
            _assets.RequireAsset(entry.Taunt ?? "FootballPlayerTauntBicepFlexAnimations");
        }
        while (!_assets.ProcessNext()) { }
        _game.InitializeVisuals(_assets);
        foreach (var entry in _catalog.Returners)
        {
            var previous = Field<ModelInstance>(Field<BallCarrier>(_game, "_player"), "_model");
            _game.SelectReturner(_catalog, entry.Id);
            Assert.Throws<ObjectDisposedException>(() => { _ = previous.Model; });
            var carrier = Field<BallCarrier>(_game, "_player");
            Assert.Equal(entry.Profile, carrier.Profile);
            Assert.True(Raylib.IsTextureValid(Field<Texture2D>(carrier, "_numberedUniform")));
            AssertUniformArtwork(Field<Texture2D>(carrier, "_numberedUniform"), entry);
            carrier.RunIntoEndZone(0, float.MaxValue);
            Assert.Equal(entry.Taunt!.Replace("FootballPlayer", "").Replace("Animations", ""), carrier.AnimationName);
            var taunt = Field<RaylibGameFramework.ThreeD.AnimationPlayer>(carrier, "_animation");
            Assert.InRange(taunt.FrameCount / taunt.FramesPerSecond, 2.5f, 2.7f);
            for (int frame = 0; frame < 150; frame++)
            {
                carrier.RunIntoEndZone(1f / 60f, float.MaxValue);
                var ball = Field<System.Numerics.Matrix4x4>(carrier, "_footballWorldTransform");
                Assert.True(float.IsFinite(ball.M41) && float.IsFinite(ball.M42) && float.IsFinite(ball.M43));
            }
            Assert.False(carrier.TauntComplete);
            carrier.RunIntoEndZone(.2f, float.MaxValue);
            Assert.True(carrier.TauntComplete);
            Assert.Equal(taunt.FrameCount - 1f, taunt.CurrentFrame, 4);
            carrier.RunIntoEndZone(1f, float.MaxValue);
            Assert.Equal(taunt.FrameCount - 1f, taunt.CurrentFrame, 4);
            Raylib.BeginDrawing(); carrier.Draw(); Raylib.EndDrawing();
            carrier.Reset();
            Assert.False(carrier.IsTaunting);
            Assert.Equal("CarryRun", carrier.AnimationName);
        }
    }

    [Fact]
    public void MissingExportsStillRenderAndSelectEveryReturnerWithSharedFallbacks()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets.json")))!;
        var optionalKeys = _catalog.Returners.SelectMany(r => new[] { r.Uniform, r.Taunt }).ToHashSet();
        foreach (var asset in json["Assets"]!.AsArray())
            if (optionalKeys.Contains(asset!["Key"]!.GetValue<string>()))
                asset["Path"] = "Assets/not-exported/" + Path.GetFileName(asset["Path"]!.GetValue<string>());
        var config = System.Text.Json.JsonSerializer.Deserialize<AssetConfig>(json.ToJsonString())!;
        var app = new GameApplication(new GameConfig(),
            InputConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "input.json")),
            MenuConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "menu.json")),
            config, new(), _catalog);
        var assets = Field<AssetManager>(app, "_assets");
        var game = Field<TackleAlleyGame>(app, "_game");
        var preview = Field<IDisposable>(app, "_returnerPreview");
        var resolved = Field<ReturnerCatalog>(app, "_returners");
        try
        {
            assets.RequireAssets(Opponent.AnimationAssetKeys);
            assets.RequireAssets("FootballPlayer", "Football", "OffenseUniform",
                "FootballPlayerCarryJogAnimations", "FootballPlayerCarryRunAnimations", "FootballPlayerCarrySprintAnimations",
                "FootballPlayerCutLeftAnimations", "FootballPlayerCutRightAnimations",
                "FootballPlayerJukeLeftAnimations", "FootballPlayerJukeRightAnimations",
                "FootballPlayerSpinLeftAnimations", "FootballPlayerSpinRightAnimations");
            foreach (var entry in resolved.Returners)
                assets.RequireAssets(entry.Uniform!, entry.Taunt!);
            while (!assets.ProcessNext()) { }
            game.InitializeVisuals(assets);
            foreach (var entry in resolved.Returners)
            {
                Raylib.BeginDrawing();
                preview.GetType().GetMethod("Draw")!.Invoke(preview,
                    new object[] { entry, new Rectangle(0, 0, 200, 200) });
                Raylib.EndDrawing();
                game.SelectReturner(resolved, entry.Id);
                var carrier = Field<BallCarrier>(game, "_player");
                Assert.Equal(entry.Profile, carrier.Profile);
                carrier.RunIntoEndZone(0, float.MaxValue);
                Assert.Equal("TauntBicepFlex", carrier.AnimationName);
                Image menu = Raylib.LoadImageFromTexture(Field<Texture2D>(preview, "_numberedUniform"));
                Image play = Raylib.LoadImageFromTexture(Field<Texture2D>(carrier, "_numberedUniform"));
                try
                {
                    for (int y = 0; y < menu.Height; y += 31)
                        for (int x = 0; x < menu.Width; x += 31)
                            Assert.Equal(Raylib.GetImageColor(menu, x, y), Raylib.GetImageColor(play, x, y));
                }
                finally { Raylib.UnloadImage(menu); Raylib.UnloadImage(play); }
            }
        }
        finally { preview.Dispose(); game.Dispose(); assets.UnloadAll(); }
    }

    private (Rectangle List, Rectangle Detail) CardLayout()
    {
        var view = Field<object>(_app, "_returnerSelection");
        return ((Rectangle, Rectangle))view.GetType().GetMethod("Layout",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
    }

    private void LoadPreviewAssets()
    {
        _assets.RequireAsset("FootballPlayer");
        foreach (var entry in _catalog.Returners)
            _assets.RequireAssets(entry.Uniform!, entry.Taunt!);
        while (!_assets.ProcessNext()) { }
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(1280, 720)]
    public void PlayerCardsRenderFullTauntsAndExportReviewFrames(int width, int height)
    {
        Raylib.SetWindowSize(width, height); Frame();
        LoadPreviewAssets();
        Press(KeyboardKey.Enter);
        var layout = CardLayout();
        Assert.True(layout.List.Width < _menu.DetailPanelBounds!.Value.X - 48);
        Assert.True(layout.Detail.Width > _menu.DetailPanelBounds.Value.Width);
        Assert.InRange(layout.Detail.X + layout.Detail.Width, 1, width - 16);
        Assert.InRange(layout.Detail.Y + layout.Detail.Height, 1, height - 40);
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/returner-select"));
        Directory.CreateDirectory(directory);
        foreach (var entry in _catalog.Returners)
        {
            for (int pose = 0; pose < 6; pose++)
            {
                Raylib.BeginDrawing();
                Invoke(_app, "DrawReturnerDetail");
                if (pose == 1)
                {
                    Image screen = Raylib.LoadImageFromScreen();
                    try { Assert.True(Raylib.ExportImage(screen, Path.Combine(directory, $"{entry.Id}-{width}x{height}.png"))); }
                    finally { Raylib.UnloadImage(screen); }
                }
                Raylib.EndDrawing();
                var target = Field<RenderTexture2D>(_preview, "_target");
                Assert.True(target.Texture.Height >= 220, "Reserve a substantial full-height preview at minimum resolution.");
                Image image = Raylib.LoadImageFromTexture(target.Texture);
                try
                {
                    int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1, pixels = 0;
                    Color accent = (Color)_preview.GetType().GetMethod("Accent")!.Invoke(_preview, [entry])!;
                    Color floor = new((int)accent.R / 5, (int)accent.G / 5, (int)accent.B / 5, 255);
                    for (int y = 0; y < image.Height; y++)
                        for (int x = 0; x < image.Width; x++)
                        {
                            Color c = Raylib.GetImageColor(image, x, y);
                            if (c.Equals(new Color(17, 29, 42, 255)) ||
                                c.Equals(new Color(9, 17, 25, 255)) || c.Equals(floor)) continue;
                            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                            pixels++;
                        }
                    Assert.True(pixels > 500, $"{entry.Id}: player must be visible.");
                    Assert.True(minX > 1 && minY > 1 && maxX < image.Width - 2 && maxY < image.Height - 2,
                        $"{entry.Id} pose {pose}: full body must clear all preview edges ({minX},{minY})-({maxX},{maxY}).");
                    Assert.True(maxY - minY > image.Height * .58f,
                        $"{entry.Id}: player should occupy most of the preview height: {maxY-minY}/{image.Height}; envelope {Field<BoundingBox>(_preview, "_tauntBounds")}; model {Field<BoundingBox>(_preview, "_bounds")}.");
                }
                finally { Raylib.UnloadImage(image); }
                Invoke(_preview, "AdvanceAnimation", .4f);
            }
            Press(KeyboardKey.Down);
        }
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(1280, 720)]
    public void ResizedCardsKeepNativeMouseHitTestingAndScrolling(int width, int height)
    {
        Raylib.SetWindowSize(width, height); Frame();
        Press(KeyboardKey.Enter);
        // Scroll to the final player with the native keyboard navigation.
        for (int i = 0; i < _catalog.Returners.Count - 1; i++) Press(KeyboardKey.Down);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var definition = typeof(MenuManager).GetProperty("CurrentMenu", flags)!.GetValue(_menu);
        var native = ((Rectangle, Rectangle?))typeof(MenuManager).GetProperty("CurrentLayoutBounds", flags)!.GetValue(_menu)!;
        var row = (Rectangle)typeof(MenuManager).GetMethod("GetItemBounds", flags)!
            .Invoke(_menu, [definition, _menu.SelectedIndex])!;
        Rectangle list = CardLayout().List;
        int x = (int)(list.X + list.Width / 2);
        int y = (int)(list.Y + (row.Y + row.Height / 2 - native.Item1.Y) * list.Height / native.Item1.Height);
        Event(7, x, y); _input.Update(); Invoke(_app, "Update", 0f); Frame();
        Assert.Equal(_catalog.Returners[^1].Id, _menu.SelectedItem!.Value.GetString());
        Assert.InRange(Raylib.GetMousePosition().X, x - 1, x + 1); // Transform was restored.
        // Click the space reclaimed for the detail card: it must not activate a native row.
        Event(7, (int)(list.X + list.Width + 24), y);
        Event(6, 0); _input.Update(); Invoke(_app, "Update", 0f); Frame();
        Event(5, 0); _input.Update(); Invoke(_app, "Update", 0f); Frame();
        Assert.Equal("MainMenu", State);
        Assert.Null(_game.SelectedReturnerId);
        Event(7, x, y); _input.Update(); Invoke(_app, "Update", 0f); Frame();
        Event(6, 0); _input.Update(); Invoke(_app, "Update", 0f); Frame();
        Event(5, 0); _input.Update(); Frame();
        Assert.Equal("Playing", State);
        Assert.Equal(_catalog.Returners[^1].Id, _game.SelectedReturnerId);
    }

    private void AssertUniformArtwork(Texture2D texture, ReturnerDefinition entry)
    {
        Image actual = Raylib.LoadImageFromTexture(texture);
        Image expected = Raylib.LoadImageFromTexture(_assets.GetTexture(entry.Uniform ?? "OffenseUniform"));
        try
        {
            PlayerUniform.PaintNumber(ref expected, entry.Profile.JerseyNumber);
            // Sample the whole atlas, including number patches, sleeve bands and pants.
            for (int y = 0; y < expected.Height; y += 7)
                for (int x = 0; x < expected.Width; x += 7)
                    Assert.Equal(Raylib.GetImageColor(expected, x, y), Raylib.GetImageColor(actual, x, y));
        }
        finally { Raylib.UnloadImage(actual); Raylib.UnloadImage(expected); }
    }
}
