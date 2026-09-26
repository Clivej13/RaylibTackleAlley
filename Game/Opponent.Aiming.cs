using System.Numerics;
using Raylib_cs;

namespace RaylibTackleAlley.Game;

public sealed partial class Opponent
{
    private readonly PlayerPhysicalAttributes _defaultCarrierPhysical;
    private readonly TackleMotionObservation _tackleObservation = new();
    private readonly List<ContactCandidate> _aimCandidates = [];
    private TackleTarget _aimTarget;
    private Vector3 _aimCarrierPosition, _aimCarrierVelocity;
    private float _lungeElapsed, _committedDuration;
    public ContactSolution CurrentContactSolution { get; private set; }
    public ContactSolution InitialContactSolution { get; private set; }
    public Vector3 InitialLaunchDirection { get; private set; }
    public Vector3 CorrectedDirection => _movementDirection;
    public float LaunchSpeed { get; private set; }
    public float PredictionConfidence => _tackleObservation.Confidence;
    public bool DirectionLocked => !Ragdoll.IsActive && !IsRecovering && State == DefenderState.LungeTackle &&
        _lungeElapsed >= _committedDuration * BehaviorProfile.CorrectionWindowFraction;
    public SweptContact? LastSweptContact { get; internal set; }
    private ContactCapsule[] _previousTackleCapsules = [], _currentTackleCapsules = [];
    private ContactCapsule[] _previousCarrierCapsules = [], _currentCarrierCapsules = [];

    private void ResetAiming()
    {
        _tackleObservation.Reset();
        CurrentContactSolution = InitialContactSolution = default;
        InitialLaunchDirection = Vector3.Zero;
        LaunchSpeed = _lungeElapsed = _committedDuration = 0;
        LastSweptContact = null;
        _aimTarget = default; _aimCarrierPosition = _aimCarrierVelocity = Vector3.Zero;
        _previousTackleCapsules = _currentTackleCapsules = [];
        _previousCarrierCapsules = _currentCarrierCapsules = [];
        _aimCandidates.Clear();
    }

    private float SupportedLungeDuration => _animations.Where(pair => pair.Key.StartsWith("LungeTackle"))
        .Select(pair => Math.Min(AuthoredLungeDuration, (pair.Value.FrameCount - 1) / pair.Value.FramesPerSecond))
        .DefaultIfEmpty(AuthoredLungeDuration).Min();

    private ContactSolution SolveContact(float duration, float elapsed, Vector3 facing) =>
        TackleAiming.Solve(_position, Velocity, facing, _aimCarrierPosition, _aimCarrierVelocity,
            _tackleObservation.Acceleration, Physical, _aimTarget, Movement.AccelerationRate,
            Math.Max(CurrentSpeed, Movement.JogSpeed), duration, PredictionConfidence, _config, elapsed,
            _config.DrawTackleAimingDebug ? _aimCandidates : null, CurrentSpeed);

    private void AdvanceAimedLunge(float dt)
    {
        // Small bounded integration steps keep travel along a changing direction stable at 30/60/120 Hz.
        while (dt > 0)
        {
            float step = Math.Min(dt, 1f / 240);
            if (!DirectionLocked && InitialContactSolution.Reachable)
            {
                var revised = SolveContact(Math.Max(0, _committedDuration - _lungeElapsed),
                    _lungeElapsed, InitialLaunchDirection);
                if (revised.Reachable)
                {
                    CurrentContactSolution = revised;
                    _movementDirection = TackleAiming.Correct(InitialLaunchDirection, _movementDirection,
                        revised.LaunchDirection, _lungeElapsed, step, _committedDuration, Profile.Agility, _aimingConfig);
                }
            }
            _position += _movementDirection * CurrentSpeed * step;
            _lungeElapsed += step;
            dt = Math.Max(0, dt - step);
        }
    }

    internal void CaptureTackleStart(ContactCapsule[] carrier)
    {
        _previousTackleCapsules = ContactCapsule.Capture(RefreshContactPose());
        _previousCarrierCapsules = carrier;
    }

    internal SweptContact? SweepTackle(ContactCapsule[] carrier, float dt)
    {
        _currentTackleCapsules = ContactCapsule.Capture(RefreshContactPose());
        _currentCarrierCapsules = carrier;
        if (State is not (DefenderState.LungeTackle or DefenderState.SetWrap)) return null;
        var contact = SweptTackleContact.Find(_previousTackleCapsules, _currentTackleCapsules,
            _previousCarrierCapsules, _currentCarrierCapsules, dt);
        if (contact.HasValue) LastSweptContact = contact;
        return contact;
    }

    public void DrawTackleAiming()
    {
        if (!_config.DrawTackleAimingDebug) return;
        DrawDecisionDebug();
        Vector3 origin = _position + Vector3.UnitY * Physical.TackleContactHeight;
        Raylib.DrawLine3D(_aimTarget.Point, _aimTarget.Point + _aimCarrierVelocity * .2f, Color.SkyBlue);
        Vector3 previous = _aimTarget.Point;
        for (int i = 1; i <= 16; i++)
        {
            float t = i * _config.LungeMaximumPredictionSeconds / 16;
            Vector3 next = _aimTarget.Point + _aimCarrierVelocity * t +
                .5f * _tackleObservation.Acceleration * PredictionConfidence * t * t;
            Raylib.DrawLine3D(previous, next, Color.SkyBlue); previous = next;
        }
        foreach (var candidate in _aimCandidates)
            Raylib.DrawSphere(candidate.Point, .025f, candidate.Reachable ? Color.Yellow : Color.Red);
        Raylib.DrawSphereWires(_aimTarget.Point, _aimTarget.Radius, 6, 8, Color.Gold);
        if (CurrentContactSolution.Reachable)
            Raylib.DrawSphere(CurrentContactSolution.ContactPoint, .06f, Color.Green);
        Raylib.DrawLine3D(origin, origin + InitialLaunchDirection * 2, Color.Yellow);
        Raylib.DrawLine3D(origin, origin + CorrectedDirection * 2, Color.Lime);
        foreach (float sign in new[] { -1f, 1f })
            Raylib.DrawLine3D(origin, origin + Vector3.Transform(InitialLaunchDirection,
                Matrix4x4.CreateRotationY(sign * BehaviorProfile.MaximumCorrectionDegrees * MathF.PI / 180)) * 2, Color.Orange);
        foreach (var capsule in _currentTackleCapsules)
        {
            Raylib.DrawCapsuleWires(capsule.Start, capsule.End, capsule.Radius, 4, 6, Color.Lime);
            var old = _previousTackleCapsules.FirstOrDefault(c => c.Body == capsule.Body);
            Raylib.DrawLine3D(old.Start, capsule.Start, Color.Orange);
            Raylib.DrawLine3D(old.End, capsule.End, Color.Orange);
            Raylib.DrawCapsuleWires(old.Start, old.End, old.Radius, 4, 6, Color.Orange);
        }
        foreach (var capsule in _currentCarrierCapsules)
            Raylib.DrawCapsuleWires(capsule.Start, capsule.End, capsule.Radius, 4, 6, Color.SkyBlue);
        if (LastSweptContact is { } hit)
        {
            Raylib.DrawSphere(hit.Point, .08f, Color.Magenta);
            Raylib.DrawLine3D(hit.Point, hit.Point + hit.Normal, Color.Magenta);
        }
    }

}
