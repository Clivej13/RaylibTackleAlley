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
        _player.Reset();
        foreach (Opponent opponent in _opponents)
            opponent.Reset();
        _camera.Reset(_player.Position, _player.CurrentForwardSpeed);
        Touchdown = false;
        GameOver = false;
        OutOfBounds = false;
        EndStateElapsed = 0;
    }

    public void IgnoreNextMouseDelta() => _player.IgnoreNextMouseDelta();

    public void Update(float deltaTime)
    {
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
        foreach (Opponent opponent in _opponents)
            opponent.Update(_player.Position, deltaTime);
        _camera.Update(_player.Position, _player.CurrentForwardSpeed, deltaTime);

        foreach (Opponent opponent in _opponents)
        {
            if (opponent.IsTouching(_player.Position))
            {
                GameOver = true;
                EndStateElapsed = 0;
                return;
            }
        }

        if (_player.Position.Z <= _field.GoalLineZ)
        {
            Touchdown = true;
            SuccessfulRuns++;
        }
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
