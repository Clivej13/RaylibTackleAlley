using System.Numerics;
using Raylib_cs;
using RaylibGameFramework.Input;

namespace RaylibTackleAlley.Game;

public sealed class BallCarrier
{
    private readonly TackleAlleyConfig _config;
    private const float HeadLeanDistance = 0.6f;
    private const float BodyLeanFraction = 0.5f;
    private const float LeanResponse = 14f;
    private Vector3 _headOffset;
    private Vector3 _bodyOffset;

    public Vector3 Position { get; private set; }
    private float _jukeRemaining;
    private int _jukeDirection;
    private bool _jukeReady = true;
    private const float SpinDuration = 0.45f;
    private const float SpinSpeed = 10f;
    private const float SpinGestureWindow = 0.4f;
    private float _spinGestureRemaining;
    private bool _spinGestureReady = true;
    private bool _spinGestureConsumed;
    private float _spinRemaining;
    private int _spinDirection;

    public int SpeedTier { get; private set; } = 2;
    public float Speed => SpeedTier switch
    {
        1 => _config.PlayerSlowSpeed,
        3 => _config.PlayerSprintSpeed,
        _ => _config.PlayerForwardSpeed
    };

    public BallCarrier(TackleAlleyConfig config)
    {
        _config = config;
        Reset();
    }

    public void Reset()
    {
        Position = Vector3.Zero;
        _headOffset = Vector3.Zero;
        _bodyOffset = Vector3.Zero;
        SpeedTier = 2;
        _jukeRemaining = 0f;
        _jukeDirection = 0;
        _jukeReady = true;
        _spinGestureRemaining = 0f;
        _spinGestureReady = true;
        _spinGestureConsumed = false;
        _spinRemaining = 0f;
        _spinDirection = 0;
    }

    public void Update(InputController input, float deltaTime, FootballField field)
    {
        float lateral = GetDirectionalValue(input.GetValue("MoveLeft"), input.GetValue("MoveRight"));
        deltaTime = Math.Max(0f, deltaTime);
        float longitudinal = GetDirectionalValue(input.GetValue("MoveForward"), input.GetValue("MoveBackward"));
        SpeedTier = input.GetValue("Sprint") > 0.5f ? 3
            : Math.Abs(input.GetValue("MoveBackward")) > 0.35f ? 1 : 2;

        float juke = GetDirectionalValue(input.GetValue("JukeLeft"), input.GetValue("JukeRight"));
        float spinX = GetDirectionalValue(input.GetValue("SpinLeft"), input.GetValue("SpinRight"));
        float spinBack = Math.Abs(input.GetValue("SpinBack"));
        float headZ = GetDirectionalValue(input.GetValue("HeadForward"), input.GetValue("HeadBackward"));
        bool headFake = input.GetValue("Sprint") > 0.5f;
        if (headFake)
        {
            // Cancel moves and consume the gesture until the stick returns to neutral.
            _jukeRemaining = 0f;
            _spinRemaining = 0f;
            _spinGestureRemaining = 0f;
            _spinGestureReady = false;
            _spinGestureConsumed = true;
            _jukeReady = false;
        }
        else
        {
            UpdateSpinGesture(spinX, spinBack, headZ, deltaTime);
        }
        if (!headFake && Math.Abs(juke) < 0.25f && !_spinGestureConsumed)
            _jukeReady = true;
        else if (!headFake && Math.Abs(juke) >= 0.65f && _jukeReady)
        {
            if (_jukeRemaining <= 0f && _spinRemaining <= 0f
                && _spinGestureRemaining <= 0f && !_spinGestureConsumed)
            {
                _jukeDirection = Math.Sign(juke);
                _jukeRemaining = _config.PlayerJukeDuration;
            }
            // Holding the stick must not repeat or queue a juke.
            _jukeReady = false;
        }

        float jukeTime = Math.Min(deltaTime, Math.Max(0f, _jukeRemaining));
        _jukeRemaining = Math.Max(0f, _jukeRemaining - deltaTime);
        float spinTime = Math.Min(deltaTime, _spinRemaining);
        _spinRemaining = Math.Max(0f, _spinRemaining - deltaTime);
        Vector3 movementDirection = _spinRemaining > 0f
            ? Vector3.Zero : new Vector3(lateral, 0f, longitudinal);
        if (_jukeRemaining > 0f)
        {
            _headOffset = new Vector3(_jukeDirection * HeadLeanDistance, 0f, -ForwardLean);
            _bodyOffset = _headOffset * BodyLeanFraction;
        }
        else
        {
            // Head fakes never feed right-stick input into the torso's movement lean.
            Vector3 headDirection = headFake && (Math.Abs(spinX) > 0.25f || Math.Abs(headZ) > 0.25f)
                ? new Vector3(spinX, 0f, headZ) : movementDirection;
            UpdateLean(movementDirection, headDirection, deltaTime);
        }
        Vector3 position = Position;
        // A move owns sideways movement until its animation ends.
        position.X += _jukeDirection * _config.PlayerJukeSpeed * jukeTime
            + _spinDirection * SpinSpeed * spinTime
            + lateral * _config.PlayerLateralSpeed * Math.Max(0f, deltaTime - jukeTime - spinTime);
        position.Z -= Speed * deltaTime;
        Position = field.ClampToOuterBoundary(position, 0.7f);
    }

    public void RunIntoEndZone(float deltaTime, float stopZ)
    {
        _jukeRemaining = Math.Max(0f, _jukeRemaining - Math.Max(0f, deltaTime));
        _spinRemaining = Math.Max(0f, _spinRemaining - Math.Max(0f, deltaTime));
        UpdateLean(Vector3.Zero, Vector3.Zero, Math.Max(0f, deltaTime));
        Position = new Vector3(Position.X, Position.Y,
            Math.Max(stopZ, Position.Z - Speed * Math.Max(0f, deltaTime)));
    }

    private void UpdateSpinGesture(float side, float back, float headZ, float deltaTime)
    {
        _spinGestureRemaining = Math.Max(0f, _spinGestureRemaining - deltaTime);
        if (Math.Abs(side) < 0.25f && back < 0.25f && Math.Abs(headZ) < 0.25f)
        {
            _spinGestureReady = true;
            _spinGestureConsumed = false;
            _spinGestureRemaining = 0f;
            return;
        }

        // Start near straight back, then roll to either side without crossing neutral.
        if (_spinGestureReady && back >= 0.65f && Math.Abs(side) < 0.35f)
        {
            _spinGestureReady = false;
            _spinGestureConsumed = true;
            if (_jukeRemaining <= 0f && _spinRemaining <= 0f)
                _spinGestureRemaining = SpinGestureWindow;
        }

        if (_spinGestureRemaining > 0f && Math.Abs(side) >= 0.65f && back < 0.75f)
        {
            _spinDirection = Math.Sign(side);
            _spinRemaining = SpinDuration;
            _spinGestureRemaining = 0f;
            _jukeReady = false;
        }
    }

    private static float GetDirectionalValue(float negativeDirection, float positiveDirection)
    {
        float value = negativeDirection < 0 || positiveDirection < 0
            ? positiveDirection + negativeDirection
            : positiveDirection - negativeDirection;
        return Math.Clamp(value, -1f, 1f);
    }

    // Tier 0 is reserved for the future stationary movement mode.
    private float ForwardLean => SpeedTier switch
    {
        0 => 0f,
        1 => 0.15f,
        2 => 0.3f,
        3 => 0.45f,
        _ => 0f
    };

    private void UpdateLean(Vector3 movementDirection, Vector3 headDirection, float deltaTime)
    {
        float blend = 1f - MathF.Exp(-LeanResponse * deltaTime);
        _bodyOffset = Vector3.Lerp(_bodyOffset, GetLeanTarget(movementDirection) * BodyLeanFraction, blend);
        _headOffset = Vector3.Lerp(_headOffset, GetLeanTarget(headDirection), blend);
    }

    private Vector3 GetLeanTarget(Vector3 direction)
    {
        // Keep diagonal lean within the same reach as a full cardinal input.
        if (direction.LengthSquared() > 1f)
            direction = Vector3.Normalize(direction);
        return direction * HeadLeanDistance - Vector3.UnitZ * ForwardLean;
    }

    public void Draw()
    {
        float hop = _jukeRemaining > 0f && _config.PlayerJukeDuration > 0f
            ? 0.35f * MathF.Sin(MathF.PI * (1f - _jukeRemaining / _config.PlayerJukeDuration)) : 0f;
        float rotation = _spinRemaining > 0f
            ? _spinDirection * 360f * (1f - _spinRemaining / SpinDuration) : 0f;
        Rlgl.PushMatrix();
        Rlgl.Translatef(Position.X, Position.Y + hop, Position.Z);
        Rlgl.Rotatef(rotation, 0f, 1f, 0f);
        DrawBlock( new Vector3(0f, 0.4f, 0f), new Vector3(1f, 0.8f, 1f), Color.DarkBrown);
        DrawBlock(new Vector3(0f, 1.2f, 0f) + _bodyOffset,
            new Vector3(1.4f, 0.8f, 1.1f), Color.Orange);
        DrawBlock(new Vector3(0f, 1.9f, 0f) + _headOffset,
            new Vector3(0.7f, 0.6f, 0.7f), Color.Gold);
        Rlgl.PopMatrix();
    }

    private static void DrawBlock(Vector3 center, Vector3 size, Color color)
    {
        Raylib.DrawCube(center, size.X, size.Y, size.Z, color);
        Raylib.DrawCubeWires(center, size.X, size.Y, size.Z, Color.Black);
    }
}
