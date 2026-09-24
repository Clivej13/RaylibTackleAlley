using System.Numerics;

namespace RaylibTackleAlley.Game;

/// <summary>Observes world displacement only; never reads controller intent or visual facing.</summary>
public sealed class CarrierPursuitPrediction
{
    // Exponential direction filter, with a 0.1 second time constant.
    private readonly TackleAlleyConfig _config;
    public CarrierPursuitPrediction(TackleAlleyConfig? config = null)
    {
        _config = config ?? new();
        _config.ValidatePrediction();
    }
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
        if (velocity.LengthSquared() <= _config.PursuitStationarySpeed * _config.PursuitStationarySpeed)
        {
            _smoothedDirection = Vector3.Zero;
            return position;
        }

        Vector3 direction = Vector3.Normalize(velocity);
        _smoothedDirection = _smoothedDirection == Vector3.Zero ? direction :
            Vector3.Lerp(_smoothedDirection, direction, 1f - MathF.Exp(-_config.PursuitDirectionResponse * deltaTime));
        // Opposite observations can cancel momentarily; use the latest observation then.
        if (_smoothedDirection.LengthSquared() < 0.000001f)
            _smoothedDirection = direction;
        float distance = carrierSpeedTier switch
        {
            1 => _config.PursuitJogPredictionDistance,
            2 => _config.PursuitRunPredictionDistance,
            3 => _config.PursuitSprintPredictionDistance,
            _ => 0f
        };
        float tierSpeed = carrierSpeedTier switch
        {
            1 => _config.PlayerSlowSpeed,
            2 => _config.PlayerForwardSpeed,
            3 => _config.PlayerSprintSpeed,
            _ => 0f
        };
        // A sudden turn reduces confidence immediately, then earns lead back as
        // observed motion stabilizes. Slow movement cannot retain sprint-sized lead.
        float confidence = Math.Max(0, Vector3.Dot(Vector3.Normalize(_smoothedDirection), direction));
        float speedScale = tierSpeed > 0 ? Math.Clamp(velocity.Length() / tierSpeed, 0, 1) : 0;
        return position + direction * (distance * speedScale * confidence * confidence);
    }
}
