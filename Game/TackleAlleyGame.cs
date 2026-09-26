using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;

namespace RaylibTackleAlley.Game;

public sealed class TackleAlleyGame : IDisposable
{
    private readonly TackleAlleyConfig _config;
    private readonly FootballField _field;
    private BallCarrier _player;
    private AssetManager? _visualAssets;
    public string? SelectedReturnerId { get; private set; }
    public PlayerProfile SelectedProfile => _player.Profile;
    private readonly Opponent[] _opponents;
    private readonly ThirdPersonCamera _camera;
    private readonly InputController _input;
    private readonly CarrierPursuitPrediction _pursuitPrediction;
    private Opponent? _tackleDefender;

    public bool TacklePendingGroundImpact { get; private set; }
    public bool Touchdown { get; private set; }
    public bool GameOver { get; private set; }
    public bool OutOfBounds { get; private set; }
    public float EndStateElapsed { get; private set; }
    private Opponent? _celebratingDefender;
    public bool OutcomeCelebrationComplete => Touchdown ? _player.TauntComplete :
        _celebratingDefender?.TauntComplete ?? true;
    public int SuccessfulRuns { get; private set; }

    public LevelDefinition CurrentLevel { get; }

    // Preserve standalone callers, including tests with intentionally empty practice setups.
    public TackleAlleyGame(TackleAlleyConfig config, InputController input, AssetManager assets)
        : this(config, input, assets, LevelDefinition.FromLegacyConfiguration(config), false) { }

    public TackleAlleyGame(TackleAlleyConfig config, InputController input, AssetManager assets, LevelDefinition level)
        : this(config, input, assets, level, true) { }

    private TackleAlleyGame(TackleAlleyConfig config, InputController input, AssetManager assets,
        LevelDefinition level, bool applyLevel)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (applyLevel)
        {
            level.ValidateGeometry(config);
            // Field and carrier retain their established component APIs; this private
            // projection gives them level geometry without mutating the shared tuning.
            config = config.ForLevel(level);
        }
        config.Validate();
        CurrentLevel = level;
        _config = config;
        _pursuitPrediction = new(config);
        _input = input;
        _field = new FootballField(config, assets);
        _player = new BallCarrier(config);
        _opponents = level.Defenders.Select(spawn => new Opponent(spawn.Position, config, spawn.Profile, spawn.BehaviorProfile)).ToArray();
        _camera = new ThirdPersonCamera(config);
        ResetRun();
    }

    public void InitializeVisuals(AssetManager assets)
    {
        _visualAssets = assets;
        _player.InitializeVisual(assets);
        foreach (Opponent opponent in _opponents)
            opponent.InitializeVisual(assets);
    }

    public void ApplyPlayerUniform(AssetManager assets, string textureKey) =>
        _player.ApplyUniform(assets, textureKey);

    public void ApplyOpponentUniforms(AssetManager assets, string textureKey)
    {
        foreach (Opponent opponent in _opponents)
            opponent.ApplyUniform(assets, textureKey);
    }

    public void SelectReturner(ReturnerCatalog catalog, string id)
    {
        var selected = catalog.Resolve(id);
        // Build a complete replacement before releasing the previous carrier.
        // Highlighting uses a separate lightweight preview, never this path.
        var replacement = new BallCarrier(_config, selected.Profile, selected.Taunt);
        try
        {
            if (_visualAssets is { } assets)
            {
                replacement.InitializeVisual(assets);
                replacement.ApplyUniform(assets, selected.Uniform ?? _config.OffenseUniform);
            }
        }
        catch { replacement.Dispose(); throw; }
        _player.Dispose();
        _player = replacement;
        SelectedReturnerId = selected.Id;
        ResetRun();
    }

    public void ResetRun()
    {
        _tackleDefender = null;
        _celebratingDefender = null;
        _player.Reset();
        _pursuitPrediction.Reset(_player.Position);
        foreach (Opponent opponent in _opponents)
            opponent.Reset();
        _camera.Reset(_player.Position, _player.CurrentForwardSpeed);
        TacklePendingGroundImpact = false;
        Touchdown = false;
        GameOver = false;
        OutOfBounds = false;
        EndStateElapsed = 0;
    }

    public void IgnoreNextMouseDelta() => _player.IgnoreNextMouseDelta();

    public void Update(float deltaTime)
    {
        RagdollDebugControls.Update(_opponents, _player.Position);
        // Tighter capsules need short motion steps to catch fast head-on contact.
        if (!TacklePendingGroundImpact && !GameOver && !Touchdown && deltaTime > Ragdoll.FixedStep &&
            _opponents.Any(o => System.Numerics.Vector3.DistanceSquared(o.Position, _player.Position) < MathF.Pow(_config.ContactSubstepDistance * PlayerPhysicalAttributes.MaximumHeightRatio, 2)))
        {
            float remaining = Math.Min(deltaTime, .25f);
            while (remaining > 0)
            {
                float step = remaining <= Ragdoll.FixedStep + 1e-7f ? remaining : Ragdoll.FixedStep;
                UpdateStep(step);
                remaining = Math.Max(0, remaining - step);
            }
            return;
        }
        UpdateStep(deltaTime);
    }

    private void UpdateStep(float deltaTime)
    {
        if (TacklePendingGroundImpact)
        {
            UpdateOutcomePhysics(deltaTime);
            _camera.Update(_player.Position, 0, deltaTime);
            if (_player.HasTackleGroundImpact)
            {
                TacklePendingGroundImpact = false;
                GameOver = true; EndStateElapsed = 0;
            }
            return;
        }
        if (GameOver) UpdateOutcomePhysics(deltaTime);
        else if (Touchdown)
            foreach (var defender in _opponents)
                defender.UpdateLungeAfterOutcome(deltaTime);
        if (Touchdown || GameOver)
        {
            EndStateElapsed += Math.Max(0, deltaTime);
            if (Touchdown)
            {
                // Keep running after scoring, then stop in the middle of the end zone.
                _player.RunIntoEndZone(deltaTime, _field.GoalLineZ - _field.EndZoneLength * _config.EndZoneStopFraction);
                _camera.Update(_player.Position, _player.CurrentForwardSpeed, deltaTime);
            }
            return;
        }

        var previousCarrierCapsules = ContactCapsule.Capture(_player.ContactPose());
        foreach (var opponent in _opponents) opponent.CaptureTackleStart(previousCarrierCapsules);
        _player.Update(_input, deltaTime, _field);
        if (_field.IsOutOfBounds(_player.Position))
        {
            OutOfBounds = true;
            GameOver = true;
            EndStateElapsed = 0;
            _camera.Update(_player.Position, _player.CurrentForwardSpeed, deltaTime);
            return;
        }
        var predictedTarget = _pursuitPrediction.Observe(_player.Position, _player.SpeedTier, deltaTime,
            _player.Movement.TierSpeed(_player.SpeedTier));
        foreach (Opponent opponent in _opponents)
        {
            // Test the animated capsules, including the final lunge handoff pose.
            // Existing ragdolls remain excluded from starting another tackle.
            bool controlledAtStart = !opponent.Ragdoll.IsActive && !opponent.IsRecovering;
            opponent.Update(_player.Position, deltaTime, predictedTarget, _player.Velocity,
                predictedTarget - _player.Position, _player.Physical, _player.ContactPose(), _player.IsEvading);
            var swept = opponent.SweepTackle(ContactCapsule.Capture(_player.ContactPose()), deltaTime);
            if (controlledAtStart && (swept.HasValue || opponent.HasBodyContact(_player)))
            {
                if (LungeTackleOutcome.Confirm(opponent, _player, swept))
                {
                    _tackleDefender = opponent;
                    _celebratingDefender = opponent;
                    opponent.CelebrateTackle();
                    TacklePendingGroundImpact = true;
                    break;
                }
            }
        }
        var cameraInput = new System.Numerics.Vector2(
            Math.Abs(_input.GetValue("MoveRight")) - Math.Abs(_input.GetValue("MoveLeft")),
            Math.Abs(_input.GetValue("MoveBackward")) - Math.Abs(_input.GetValue("MoveForward")));
        // Movement deadzones discard small X values that still matter to the rear-view
        // cone. Recover raw X only for the standard left-stick mapping and active back
        // input; keep keyboard and rebound controls on their configured action values.
        if (cameraInput.Y > 0 && Raylib.IsGamepadAvailable(0) &&
            _input.GetBinding("MoveBackward", InputDeviceFamily.Gamepad)?.Input == "LeftYPositive" &&
            _input.GetBinding("MoveLeft", InputDeviceFamily.Gamepad)?.Input == "LeftXNegative" &&
            _input.GetBinding("MoveRight", InputDeviceFamily.Gamepad)?.Input == "LeftXPositive")
        {
            float rawY = Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftY);
            float rawX = Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftX);
            if (rawY >= cameraInput.Y && Math.Abs(rawX) >= Math.Abs(cameraInput.X))
                cameraInput.X = rawX;
        }
        _camera.Update(_player.Position, _player.CurrentForwardSpeed, deltaTime, cameraInput);

        if (TacklePendingGroundImpact) return;

        if (_player.Position.Z <= _field.GoalLineZ)
        {
            Touchdown = true;
            SuccessfulRuns++;
        }
    }

    private void UpdateOutcomePhysics(float deltaTime)
    {
        float remaining = Math.Max(0, deltaTime);
        // Bound physics catch-up as Ragdoll.Update does, but retain elapsed recovery time.
        float physicsRemaining = Math.Min(remaining, .25f);
        while (physicsRemaining > 0)
        {
            float step = Math.Min(physicsRemaining, Ragdoll.FixedStep);
            if (_tackleDefender is { } contact)
            {
                RagdollContact.Resolve(contact.Ragdoll, _player.Ragdoll);
                if (contact.WrapRemaining > 0)
                {
                    contact.WrapRemaining = Math.Max(0, contact.WrapRemaining - step);
                    if (!RagdollContact.MaintainWrap(contact.Ragdoll, _player.Ragdoll, step)) contact.WrapRemaining = 0;
                }
            }
            _player.UpdatePhysicsAndRecovery(step);
            foreach (var defender in _opponents) defender.UpdateLungeAfterOutcome(step);
            if (_tackleDefender is { } active)
                RagdollContact.Resolve(active.Ragdoll, _player.Ragdoll);
            physicsRemaining = Math.Max(0, physicsRemaining - step);
            remaining = Math.Max(0, remaining - step);
        }
        // Refresh the carrier render/football pose after split position correction.
        _player.UpdatePhysicsAndRecovery(_player.Ragdoll.IsActive ? 0 : remaining);
        foreach (var defender in _opponents)
            if (!defender.Ragdoll.IsActive) defender.UpdateLungeAfterOutcome(remaining);
    }

    public void Dispose()
    {
        _player.Dispose();
        foreach (var opponent in _opponents) opponent.Dispose();
    }

    private void DrawProfiles()
    {
        if (!_config.ShowPlayerProfiles) return;
        var entries = new[] { ("CARRIER", _player.VisualProfile,
            _player.Ragdoll.IsActive ? _player.Ragdoll.Bodies[0].LinearVelocity.Length() : _player.CurrentForwardSpeed,
            _player.Ragdoll.IsActive || _player.IsRecovering || _player.IsTaunting ? 0f : _player.TargetForwardSpeed) }
            .Concat(_opponents.Select((o, i) => ($"DEFENDER {i + 1}", o.VisualProfile,
                o.Ragdoll.IsActive ? o.Ragdoll.Bodies[0].LinearVelocity.Length() : o.CurrentSpeed, o.TargetSpeed))).ToArray();
        int width = Math.Min(520, Raylib.GetScreenWidth() - 36);
        int x = Raylib.GetScreenWidth() - width - 18, y = Raylib.GetScreenWidth() < 940 ? 100 : 18;
        Raylib.DrawRectangle(x, y, width, 28 + entries.Length * 62, new Color(0, 0, 0, 180));
        Raylib.DrawText("PLAYER PROFILES", x + 12, y + 8, 16, Color.SkyBlue);
        y += 30;
        foreach (var (role, visual, current, target) in entries)
        {
            var p = visual.Profile;
            string title = $"{role}  #{p.JerseyNumber} {p.Name}";
            int fontSize = 16;
            while (fontSize > 8 && Raylib.MeasureText(title, fontSize) > width - 24) fontSize--;
            Raylib.DrawText(title, x + 12, y, fontSize, Color.White);
            Raylib.DrawText($"{p.Height:0.00} m   {p.Weight:0} kg   {visual.BuildDescription} ({p.Build:+0.00;-0.00;0.00})",
                x + 12, y + 19, 14, Color.SkyBlue);
            Raylib.DrawText($"SPD {p.Speed}  ACC {p.Acceleration}  AGI {p.Agility}   {current:0.0} / {target:0.0} m/s",
                x + 12, y + 37, 14, Color.White);
            y += 62;
        }
    }

    private static void DrawPhysical(string role, PlayerProfile p, PlayerPhysicalAttributes physical,
        System.Numerics.Vector3 momentum, TackleImpact? tackle, float impulse, int y)
    {
        Raylib.DrawRectangle(18, y - 2, 850, 50, new Color(0, 0, 0, 180));
        Raylib.DrawText($"{role} {p.Name}: {p.Height:0.00}m {p.Weight:0}kg STR {p.Strength} MASS {physical.TotalMass:0.0}kg  P ({momentum.X:0},{momentum.Y:0},{momentum.Z:0}) kg m/s",
            24, y, 14, Color.SkyBlue);
        Raylib.DrawText($"Impact {tackle?.ImpactScore ?? 0:0} Resistance {tackle?.ResistanceScore ?? 0:0}  {tackle?.Outcome.ToString() ?? "No contact"}  Impulse {impulse:0.0} Ns",
            24, y + 22, 14, Color.White);
    }

    public void Draw()
    {
        Raylib.BeginMode3D(_camera.Camera);
        _field.Draw();
        _player.Draw();
        foreach (Opponent opponent in _opponents)
            opponent.Draw();
        foreach (var opponent in _opponents) opponent.DrawTackleAiming();
        _field.DrawOutOfBounds();
        Raylib.EndMode3D();
        RagdollDebugControls.Draw(_opponents);
        DrawProfiles();
        if (_config.DrawTackleAimingDebug) Opponent.DrawTackleAimingLegend();
        for (int i = 0; i < _opponents.Length; i++) _opponents[i].DrawTackleAimingLabel(122 + i * 36);
        if (_config.DrawGameplayDebug || _config.ShowPlayerProfiles)
        {
            var defender = _tackleDefender ?? _opponents.MinBy(o => System.Numerics.Vector3.DistanceSquared(o.Position, _player.Position));
            int y = Raylib.GetScreenHeight() - 112;
            DrawPhysical("CARRIER", _player.Profile, _player.Physical, _player.Momentum,
                _player.LastTackle, _player.Ragdoll.LastContactImpulse, y);
            if (defender is not null) DrawPhysical("DEFENDER", defender.Profile, defender.Physical,
                defender.Momentum, defender.LastTackle, defender.Ragdoll.LastContactImpulse, y + 52);
        }

        Raylib.DrawRectangle(18, 18, 360, 70, new Color(0, 0, 0, 180));
        Raylib.DrawText($"RUNS {SuccessfulRuns}   SPEED {_player.SpeedTier}", 32, 32, 20, Color.White);
        Raylib.DrawText("Stay inside the red sidelines", 32, 58, 16, Color.SkyBlue);
        if (Touchdown || GameOver)
        {
            string title = Touchdown ? "TOUCHDOWN" : OutOfBounds ? "OUT OF BOUNDS" : "GAME OVER";
            Color titleColor = Touchdown ? Color.Gold : Color.Red;
            string prompt = Touchdown ? "Enter / Pause to run again" : "Enter / Pause to try again";
            int w = Raylib.MeasureText(title, 54);
            Raylib.DrawText(title, (Raylib.GetScreenWidth() - w) / 2, Raylib.GetScreenHeight() / 3, 54, titleColor);
            int promptWidth = Raylib.MeasureText(prompt, 20);
            Raylib.DrawText(prompt, (Raylib.GetScreenWidth() - promptWidth) / 2, Raylib.GetScreenHeight() / 3 + 70, 20, Color.White);
        }
    }
}
