using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

public sealed class Opponent
{
    private readonly Vector3 _spawnPosition;
    private readonly TackleAlleyConfig _config;
    private Vector3 _position;
    private bool _chasing;

    public Opponent(Vector3 spawnPosition, TackleAlleyConfig config)
    {
        _spawnPosition = spawnPosition;
        _config = config;
        Reset();
    }

    public void Reset()
    {
        _position = _spawnPosition;
        _chasing = false;
    }

    public void Update(Vector3 playerPosition, float deltaTime)
    {
        float distance = Vector3.Distance(_position, playerPosition);
        if (!_chasing && distance <= _config.OpponentTriggerDistance)
            _chasing = true;

        if (!_chasing)
            return;

        Vector3 direction = playerPosition - _position;
        direction.Y = 0;
        if (direction.LengthSquared() > 0.001f)
            _position += Vector3.Normalize(direction) * _config.OpponentSpeed * Math.Max(0, deltaTime);
    }

    public bool IsTouching(Vector3 playerPosition) =>
        Vector3.Distance(_position, playerPosition) <= _config.TackleDistance;

    public void Draw()
    {
        Color color = _chasing ? Color.Red : Color.Blue;
        Raylib.DrawCube(_position + new Vector3(0, 1f, 0), 1.4f, 2f, 1.4f, color);
        Raylib.DrawCubeWires(_position + new Vector3(0, 1f, 0), 1.4f, 2f, 1.4f, Color.DarkBlue);
    }
}
