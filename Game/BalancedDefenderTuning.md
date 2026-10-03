# Balanced defender approach tuning

> Updated after playtesting feedback: the patient approach described below is now
> reserved as `offball-linebacker` for future off-ball run defense. No current level
> uses that preset. Tackle Alley `balanced` omits ApproachTuning and restores
> PursuitPredictionStrength=1, BreakdownDistance=4, BreakdownExitDistance=5 and
> LungePreference=1. It retains the shorter correction window/rate/cap
> (0.2 / 60 / 8), so reachable dives commit without waiting for a projected wrap.
> The historical candidate measurements below describe the preserved linebacker
> behavior, not the active Tackle Alley preset. Its regression coverage is now
> OffballLinebackerFlowTests; TackleAlleyCommitmentTests checks level assignments
> and #52's commitment and post-lock lateral escape at 30/60/120 Hz.
> Player ratings, movement scaling, aggressive/contain, physical reach, swept contact
> and recovery are unchanged. No run-game mode or new level is added.


This is a deterministic simulation-backed candidate, not a completed hands-on
balance sign-off. The implementation environment has repository validation tools
but no desktop control or live gameplay viewing capability. Hidden-window native
rendering probes exercise debug drawing; they do not establish animation feel,
input fairness, or tackle success in a real run.

## Scope and ownership

Only the shipped balanced preset enables ApproachTuning. This optional record
refines existing DefenderDecision stages and is selected by data, never profile ID.
Aggressive and contain keep their exact authored values and the old decision rules.
Standalone Opponent constructors still use legacy config-derived Balanced.
Movement scaling, player ratings, reach dimensions, correction solver, swept
capsule contact, ragdoll handoff and recovery code are unchanged by this tuning pass.
No progression or new gameplay system is introduced.

## Changes and rationale

- Pursuit keeps interception lead outside the preparation cone. Previously that
  cone turned side pursuit into direct chasing. Lead is bounded by current
  separation and at most 0.35 seconds of observed carrier travel. Existing shared
  pursuit prediction still determines the direction; stale opposing lead is dropped.
  Existing observed-motion confidence suppresses lead during cuts and rebuilds it
  as motion settles. Target-tier movement scaling remains authoritative.
- Preparation uses actual observed velocity rather than the prediction vector.
  Prediction suppression no longer makes the ready cone disappear. A stationary
  carrier can be approached from any side, but facing/reach gates still apply.
- Stopping space includes carrier closing travel during defender reaction and
  braking. Entry is max(base entry, required stopping space), capped by the existing
  8 m approach range. Exit retains the authored 1 m gap, also capped at 8 m.
  If entry was already too late, the defender still brakes instead of refusing ready
  movement and continuing at sprint pace. Existing nonzero ready speed is retained.
- Ready movement closes on the physical carrier, not a lead point beyond the body.
  A grounded wrap forecast can defer a dive only if there is time to slow, square up,
  and cover the gap within 0.30 seconds without excessive lateral escape. It does
  not authorize contact: the existing TryWrap body, height, angle, separation and
  lateral-speed checks must still pass. An already-close aligned wrap needs no
  extra frame in ready stance.
- Balanced adds a stricter 25-degree facing gate to close wraps. When already in
  range but slightly misaligned, it turns while the body is expected to stay in reach.
  Late fast approaches or escaping/crossing carriers can still select a physically
  reachable lunge. Bad launch angles remain rejected.
- Early lunge correction ends at 20% of the existing 0.6-second dive (0.12 seconds).
  With the existing linear fade, 60 degrees/sec provides about 3.6 degrees total
  correction at neutral Agility, additionally capped at 8 degrees from launch.
  After that, the existing direction lock is absolute.

## Config delta: defender-profiles.json, balanced only

| Field | Before | Candidate |
| --- | --- | --- |
| PursuitPredictionStrength | 1 | 0.85 |
| BreakdownDistance | 4 m | 4.5 m |
| BreakdownExitDistance | 5 m | 5.5 m |
| LungePreference | 1 | 0.65 |
| CorrectionWindowFraction | 0.4 | 0.2 |
| CorrectionRateDegrees | 90 deg/s | 60 deg/s |
| MaximumCorrectionDegrees | 18 deg | 8 deg |

New optional ApproachTuning fields:
MaximumPredictionSeconds = 0.35 (valid 0–1 seconds);
ReadyHalfAngleDegrees = 60 (valid 0–90 degrees);
WrapLookAheadSeconds = 0.30 (valid 0–0.6 seconds);
SquareUpHalfAngleDegrees = 25 (valid 0–45 degrees).
Nested values must be explicit and finite. Unknown fields fail catalog loading.

ReactionTime remains 0.15 seconds, ApproachDistance 8 m, TackleCommitDistance
4.5 m, PursuitAggression 1, ContainBias 0, and WrapPreference 1.
No change to config.json or either role's movement scaling in this tuning pass.
Defender Speed influences remain 0.20/0.20/0.20 and Acceleration influences
0.25/0.25/0.25. Tackle reach is never enlarged for faster returners.

## Deterministic evidence

BalancedDefenderFlowTests runs low-Speed Darius Stone and high-Speed Marcus Reed
at each shipped slow/run/sprint pace against all four current defender bodies.
Each encounter starts head-on 12 m apart and advances at 60 Hz. It uses the shared
movement values, pursuit predictor, actual Opponent update/decision path, and
physical body aiming. It records tackle selection, not a swept-contact takedown:
full contact/recovery mechanics remain covered by the existing native-asset suite.

Original preset: 22 lunges and 2 wraps. Candidate: 24 wraps, each after one
preparation entry without toggling back into pursuit. First recorded ready distances
span 4.367–5.043 m; minimum ready speeds span 5.969–7.410 m/s, preserving useful
momentum. Selected wrap distances span 0.598–1.000 m with existing body reach.

The sprint travel window between equal-distance points remains shorter for Marcus
than Darius: speed ratio gives about 75% of Darius's time at sprint, versus about
86% at run and 95% at jog. No reaction delay or reach compensation was added.

Additional regression scenarios cover bounded side interception, stale lead after
reversal, repeated cuts at 30/60/120 Hz, prediction suppression without preparation
flicker, close-angle square-up, grounded-wrap deferral, escaping carriers, poor
launch angles, emergency dives, correction lock, debug metrics, preset identity
independence and unchanged aggressive/contain values.

Recorded approach matrix (distances sampled after each update):
```text
darius-stone tier 3 vs Marcus Hill: speed=11.078, old breakdown=3.617 choice=LungeTackle, new breakdown=4.948 readyMin=7.410 choice=SetWrap at=0.734m
darius-stone tier 3 vs Devon Brooks: speed=11.078, old breakdown= choice=LungeTackle, new breakdown=4.876 readyMin=6.760 choice=SetWrap at=0.803m
darius-stone tier 3 vs Andre Cole: speed=11.078, old breakdown= choice=LungeTackle, new breakdown=4.762 readyMin=6.367 choice=SetWrap at=0.782m
darius-stone tier 3 vs Sam Taylor: speed=11.078, old breakdown= choice=LungeTackle, new breakdown=4.607 readyMin=5.969 choice=SetWrap at=0.707m
darius-stone tier 1 vs Marcus Hill: speed=6.460, old breakdown= choice=LungeTackle, new breakdown=5.043 readyMin=7.410 choice=SetWrap at=0.669m
darius-stone tier 1 vs Devon Brooks: speed=6.460, old breakdown= choice=LungeTackle, new breakdown=4.899 readyMin=6.760 choice=SetWrap at=0.945m
darius-stone tier 1 vs Andre Cole: speed=6.460, old breakdown= choice=LungeTackle, new breakdown=4.898 readyMin=6.367 choice=SetWrap at=0.851m
darius-stone tier 1 vs Sam Taylor: speed=6.460, old breakdown= choice=LungeTackle, new breakdown=4.663 readyMin=5.969 choice=SetWrap at=0.732m
marcus-reed tier 1 vs Marcus Hill: speed=6.812, old breakdown= choice=LungeTackle, new breakdown=4.861 readyMin=7.410 choice=SetWrap at=0.612m
marcus-reed tier 1 vs Devon Brooks: speed=6.812, old breakdown= choice=LungeTackle, new breakdown=4.932 readyMin=6.760 choice=SetWrap at=0.872m
marcus-reed tier 1 vs Andre Cole: speed=6.812, old breakdown= choice=LungeTackle, new breakdown=4.698 readyMin=6.367 choice=SetWrap at=0.980m
marcus-reed tier 1 vs Sam Taylor: speed=6.812, old breakdown= choice=LungeTackle, new breakdown=4.665 readyMin=5.969 choice=SetWrap at=0.836m
marcus-reed tier 2 vs Marcus Hill: speed=10.296, old breakdown=3.655 choice=LungeTackle, new breakdown=4.945 readyMin=7.410 choice=SetWrap at=0.607m
marcus-reed tier 2 vs Devon Brooks: speed=10.296, old breakdown= choice=LungeTackle, new breakdown=4.898 readyMin=6.760 choice=SetWrap at=0.712m
marcus-reed tier 2 vs Andre Cole: speed=10.296, old breakdown= choice=LungeTackle, new breakdown=4.806 readyMin=6.367 choice=SetWrap at=1.000m
marcus-reed tier 2 vs Sam Taylor: speed=10.296, old breakdown= choice=LungeTackle, new breakdown=4.669 readyMin=5.969 choice=SetWrap at=0.672m
marcus-reed tier 3 vs Marcus Hill: speed=14.812, old breakdown=3.842 choice=SetWrap, new breakdown=4.610 readyMin=7.410 choice=SetWrap at=0.657m
marcus-reed tier 3 vs Devon Brooks: speed=14.812, old breakdown= choice=LungeTackle, new breakdown=4.444 readyMin=6.760 choice=SetWrap at=0.598m
marcus-reed tier 3 vs Andre Cole: speed=14.812, old breakdown= choice=LungeTackle, new breakdown=4.603 readyMin=6.367 choice=SetWrap at=0.826m
marcus-reed tier 3 vs Sam Taylor: speed=14.812, old breakdown=3.651 choice=SetWrap, new breakdown=4.367 readyMin=5.969 choice=SetWrap at=0.654m
darius-stone tier 2 vs Marcus Hill: speed=8.835, old breakdown=3.748 choice=LungeTackle, new breakdown=4.963 readyMin=7.410 choice=SetWrap at=0.690m
darius-stone tier 2 vs Devon Brooks: speed=8.835, old breakdown= choice=LungeTackle, new breakdown=4.963 readyMin=6.760 choice=SetWrap at=0.858m
darius-stone tier 2 vs Andre Cole: speed=8.835, old breakdown= choice=LungeTackle, new breakdown=4.915 readyMin=6.367 choice=SetWrap at=0.925m
darius-stone tier 2 vs Sam Taylor: speed=8.835, old breakdown= choice=LungeTackle, new breakdown=4.813 readyMin=5.969 choice=SetWrap at=0.669m
```

## Subjective playtest still required

Use DrawTackleAimingDebug = true temporarily and restore it afterward. In the current
single level, try Darius and Marcus at slow/run/sprint, then Eli/Jalen/Noah:

1. Straight and diagonal approaches from either sideline: judge interception angle,
   ready entry distance, momentum and the visual square-up.
2. Early cut, late juke, spin, stop/restart and repeated left/right reversals: judge
   whether suppressed prediction feels responsive without target wobble or chatter.
3. Run then sprint near a defender, and sprint then slow: verify reduced dodge time
   feels earned by travel speed, while normal running stays controllable.
4. Close aligned contact, bad-angle approach and late escape: verify wrap preference
   looks grounded, emergency dives remain useful, and poor commitments visibly miss.
5. Watch after the 0.12-second lock: the dive must not bend toward a new dodge.
6. Check real swept contact, tackle outcome, ragdoll landing/recovery and multiple
   defenders converging. Selection statistics above are not tackle success rates.
7. Check all four HUD rows at 640x480 and 1280x720 alongside the roster overlay.

The debug state includes distance, signed closing speed, estimated time to wrap
range, pursuit target, live body aim target, wrap/dive reach, original commitment
reason, remaining correction time and angular budget. The time estimate assumes
constant relative velocity and is not the physical solver's contact time.
