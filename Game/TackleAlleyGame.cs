using Raylib_cs;
using RaylibGameFramework.Assets;
using RaylibGameFramework.Input;

namespace RaylibTackleAlley.Game;

public sealed class TackleAlleyGame
{
    private readonly FootballField _field;
    private readonly BallCarrier _player;
    private readonly Opponent[] _opponents;
    private readonly ThirdPersonCamera _camera;
    private readonly InputController _input;
    private readonly CarrierPursuitPrediction _pursuitPrediction = new();
    private Opponent? _tackleDefender;

    public bool TacklePendingGroundImpact { get; private set; }
    public bool Touchdown { get; private set; }
    public bool GameOver { get; private set; }
    public bool OutOfBounds { get; private set; }
    public float EndStateElapsed { get; private set; }
    public int SuccessfulRuns { get; private set; }

    public TackleAlleyGame(TackleAlleyConfig config, InputController input, AssetManager assets)
    {
        _input = input;
        _field = new FootballField(config, assets);
        _player = new BallCarrier(config);
        _opponents =
        [
            new Opponent(new(-5.5f, 0, -18f), config),
            new Opponent(new(5.5f, 0, -31f), config),
            new Opponent(new(-4.5f, 0, -46f), config),
            new Opponent(new(4.5f, 0, -61f), config)
        ];
        _camera = new ThirdPersonCamera(config);
        ResetRun();
    }

    public void InitializeVisuals(AssetManager assets)
    {
        _player.InitializeVisual(assets);
        foreach (Opponent opponent in _opponents)
            opponent.InitializeVisual(assets);
    }

    public void ResetRun()
    {
        _tackleDefender = null;
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
            _opponents.Any(o => System.Numerics.Vector3.DistanceSquared(o.Position, _player.Position) < 36f))
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
                _player.RunIntoEndZone(deltaTime, _field.GoalLineZ - _field.EndZoneLength * 0.5f);
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
        bool touching = false;
        foreach (Opponent opponent in _opponents)
        {
            // Test the animated capsules, including the final lunge handoff pose.
            // Existing ragdolls remain excluded from starting another tackle.
            bool controlledAtStart = !opponent.Ragdoll.IsActive && !opponent.IsRecovering;
            opponent.Update(_player.Position, deltaTime, predictedTarget, _player.Velocity);
            if (controlledAtStart && opponent.HasBodyContact(_player))
            {
                if (opponent.State == DefenderState.LungeTackle && LungeTackleOutcome.Confirm(opponent, _player))
                {
                    _tackleDefender = opponent;
                    TacklePendingGroundImpact = true;
                    break;
                }
                touching = true;
                break;
            }
        }
        var cameraInput = new System.Numerics.Vector2(
            Math.Abs(_input.GetValue("MoveRight")) - Math.Abs(_input.GetValue("MoveLeft")),
            Math.Abs(_input.GetValue("MoveBackward")) - Math.Abs(_input.GetValue("MoveForward")));
        _camera.Update(_player.Position, _player.CurrentForwardSpeed, deltaTime, cameraInput);

        if (TacklePendingGroundImpact) return;
        if (touching)
        {
            GameOver = true;
            EndStateElapsed = 0;
            return;
        }

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
