using System.Numerics;

namespace RaylibTackleAlley.Game;

/// <summary>Observes world displacement only; never reads controller intent or visual facing.</summary>
public sealed class CarrierPursuitPrediction
{
    public const float JogPredictionDistance = 1f;
    public const float RunPredictionDistance = 2f;
    public const float SprintPredictionDistance = 3f;
    public const float StationarySpeed = 0.05f;
    // Exponential direction filter, with a 0.1 second time constant.
    public const float DirectionResponse = 10f;
    private Vector3 _previousPosition;
    private Vector3 _smoothedDirection;
    private bool _hasSample;

    public void Reset(Vector3 position)
    {
        _previousPosition = position;
        _smoothedDirection = Vector3.Zero;
        _hasSample = true;
    }

    public Vector3 Observe(Vector3 position, int carrierSpeedTier, float deltaTime)
    {
        if (!_hasSample || !float.IsFinite(deltaTime) || deltaTime <= 0f)
        {
            Reset(position);
            return position;
        }

        Vector3 velocity = (position - _previousPosition) / deltaTime;
        velocity.Y = 0f;
        _previousPosition = position;
        if (velocity.LengthSquared() <= StationarySpeed * StationarySpeed)
        {
            _smoothedDirection = Vector3.Zero;
            return position;
        }

        Vector3 direction = Vector3.Normalize(velocity);
        _smoothedDirection = _smoothedDirection == Vector3.Zero ? direction :
            Vector3.Lerp(_smoothedDirection, direction, 1f - MathF.Exp(-DirectionResponse * deltaTime));
        // Opposite observations can cancel momentarily; use the latest observation then.
        if (_smoothedDirection.LengthSquared() < 0.000001f)
            _smoothedDirection = direction;
        float distance = carrierSpeedTier switch
        {
            1 => JogPredictionDistance,
            2 => RunPredictionDistance,
            3 => SprintPredictionDistance,
            _ => 0f
        };
        return position + Vector3.Normalize(_smoothedDirection) * distance;
    }
}
