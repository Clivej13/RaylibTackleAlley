using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;

namespace RaylibTackleAlley.Game;

public sealed class TackleAlleyGame
{
    private readonly TackleAlleyConfig _config;
    private readonly FootballField _field;
    private readonly BallCarrier _player;
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

    public TackleAlleyGame(TackleAlleyConfig config, InputController input, AssetManager assets)
    {
        config.Validate();
        _config = config;
        _pursuitPrediction = new(config);
        _input = input;
        _field = new FootballField(config, assets);
        _player = new BallCarrier(config);
        _opponents = config.OpponentSpawns.Select(spawn => new Opponent(spawn.Position, config)).ToArray();
        _camera = new ThirdPersonCamera(config);
        ResetRun();
    }

    public void InitializeVisuals(AssetManager assets)
    {
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
            _opponents.Any(o => System.Numerics.Vector3.DistanceSquared(o.Position, _player.Position) < _config.ContactSubstepDistance * _config.ContactSubstepDistance))
        {
            float remaining = Math.Min(deltaTime, .25f);
            while (remaining > 0)
            {
                float step = Math.Min(remaining, Ragdoll.FixedStep);
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

        _player.Update(_input, deltaTime, _field);
        if (_field.IsOutOfBounds(_player.Position))
        {
            OutOfBounds = true;
            GameOver = true;
            EndStateElapsed = 0;
            _camera.Update(_player.Position, _player.CurrentForwardSpeed, deltaTime);
            return;
        }
        var predictedTarget = _pursuitPrediction.Observe(_player.Position, _player.SpeedTier, deltaTime);
        foreach (Opponent opponent in _opponents)
        {
            // Test the animated capsules, including the final lunge handoff pose.
            // Existing ragdolls remain excluded from starting another tackle.
            bool controlledAtStart = !opponent.Ragdoll.IsActive && !opponent.IsRecovering;
            opponent.Update(_player.Position, deltaTime, predictedTarget, _player.Velocity,
                predictedTarget - _player.Position);
            if (controlledAtStart && opponent.HasBodyContact(_player))
            {
                if (LungeTackleOutcome.Confirm(opponent, _player))
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
                RagdollContact.Resolve(contact.Ragdoll, _player.Ragdoll);
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

    public void Draw()
    {
        Raylib.BeginMode3D(_camera.Camera);
        _field.Draw();
        _player.Draw();
        foreach (Opponent opponent in _opponents)
            opponent.Draw();
        _field.DrawOutOfBounds();
        Raylib.EndMode3D();
        RagdollDebugControls.Draw(_opponents);

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
