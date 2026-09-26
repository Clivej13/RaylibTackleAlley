# Shared defender decision pipeline

Every defender uses the same DefenderDecision functions and Opponent executor.
No profile ID/name switches, AI subclasses, random preset-specific rules or new
progression are involved. All four Level 1 defenders still use Balanced.

## Flow and ownership

1. Observe carrier velocity, acceleration/confidence, physical body target and the
   shared CarrierPursuitPrediction lead.
2. Plan pursuit/approach: scale and cap prediction, apply optional containment,
   check the carrier forward cone, and calculate reaction travel + braking space.
3. Choose an eligible wrap or reachable lunge, subject to the profile commit range
   and deterministic preferences.
4. Execute via the existing ready locomotion, wrap/lunge animation and physics.
   A commitment cannot be replaced by another decision. Only the existing bounded
   early lunge correction may adjust its travel direction.
5. Direction lock, swept capsule contact, ragdoll handoff and recovery retain the
   existing physical ownership. Pursuit resumes only after recovery completes.

Opponent.AiState exposes decision/ownership stages independently of State, which
remains the animation execution contract:

| AI stage | Meaning |
| --- | --- |
| Pursuit | Chase outside the approach range, or resume after recovery. |
| Approach | Enter the approach range or contain at controlled ready pace. |
| Breakdown | Ready intent is active but current speed still exceeds the controlled wrap threshold. |
| WrapCommitment | Execute the selected waist wrap without retargeting. |
| LungeCommitment | Execute a reachable lunge, early correction, then direction lock. |
| Recovery | Ragdoll, landing, down or get-up owns motion. |
| Taunt | Existing outcome celebration. |

Not every encounter visits every stage: insufficient braking space can lead
directly from approach to a reachable lunge. Failure to find a reachable contact
leaves the defender approaching/chasing; distance alone never confirms a tackle.
Ready-cone gating continues to affect preparation, not independently valid contact
opportunities. Low confidence still favors grounded preparation.

The physical aiming solver and swept-contact implementation are unchanged.
Body proportions, movement ratings, collision radii, actual animation support
windows, facing gates and relative motion still determine reachability.

## Data and references

defender-profiles.json has a required Profiles array of DefenderProfile objects.
All properties must be explicit. IDs are unique lowercase hyphenated keys, names
are nonblank, values are finite, unknown fields fail startup, and all ranges and
relationships are validated. Example Balanced profile:

```json
{
  "Id": "balanced",
  "Name": "Balanced",
  "ReactionTime": 0.15,
  "PursuitPredictionStrength": 1,
  "PursuitAggression": 1,
  "ContainBias": 0,
  "ApproachDistance": 8,
  "BreakdownDistance": 4,
  "BreakdownExitDistance": 5,
  "TackleCommitDistance": 4.5,
  "WrapPreference": 1,
  "LungePreference": 1,
  "CorrectionWindowFraction": 0.4,
  "CorrectionRateDegrees": 90,
  "MaximumCorrectionDegrees": 18
}
```

A levels.json defender now requires both references:

```json
{ "X": -5.5, "Z": -18, "Profile": "marcus-hill", "BehaviorProfile": "balanced" }
```

Profile still resolves the existing identity/body PlayerProfile from the
DefenderProfiles registry in levels.json. BehaviorProfile resolves a DefenderProfile
from defender-profiles.json. The similarly named registries remain separate to
preserve the stage-one identity schema. Startup loads both catalogs and validates
every reference before native assets/window initialization.

| Parameter | Units / bounds | Effect |
| --- | --- | --- |
| ReactionTime | seconds, 0–2 | Travel reserved in the braking-space calculation (the previous OpponentBreakdownReactionSeconds). It is not a polling interval or artificial perception delay. |
| PursuitPredictionStrength | multiplier, 0–2 | Scales pursuit lead before the existing distance cap; does not amplify physical tackle prediction. |
| PursuitAggression | multiplier, 0.25–2 | Scales run/sprint engagement distances. Body speed/acceleration and tackle reach are unchanged. |
| ContainBias | 0–1 | In approach range, reduces lead chasing and biases toward the carrier's forward lane when inside the ready cone. |
| ApproachDistance | metres, 0.1–50 | Starts close approach/contain behavior. |
| BreakdownDistance | metres, 0.1–25 | Ready-entry distance, still requiring braking space and the cone. |
| BreakdownExitDistance | metres, 0.1–30 | Ready hysteresis exit. Must be >= BreakdownDistance and <= ApproachDistance. |
| TackleCommitDistance | metres, 0.1–10 | Additional decision range cap, scaled by defender height; never expands physical reach. |
| WrapPreference / LungePreference | weights, 0–1 | Deterministic choice among eligible actions. Zero disables an action; at least one must be nonzero. Ties choose wrap. |
| CorrectionWindowFraction | 0–0.8 | Fraction of committed animation duration allowing early correction. |
| CorrectionRateDegrees | degrees/sec, 0–360 | Existing fading correction angular budget, still bounded by the agility modifier. |
| MaximumCorrectionDegrees | degrees, 0–45 | Maximum deviation from the original launch direction. |

Balanced mirrors the pre-refactor thresholds. Aggressive and Contain are initial
parameter examples, added after the common pipeline; they are not a balance pass.
Aggressive engages earlier and prefers a lunge when both actions are eligible.
Contain enters controlled approach earlier, reduces lead, favors wrap, and limits
commit/correction ranges. To try either, edit one level spawn's BehaviorProfile
and restart; no new levels are authored.

Legacy direct Opponent and level constructors derive Balanced from their supplied
TackleAlleyConfig, preserving custom test/component tuning. Authored startup passes
the explicit registry, which owns the profile knobs. config.json retains general
physics, aim reach/confidence, deceleration and locomotion baselines.

## Diagnostics

Enable DrawTackleAimingDebug in config.json. Both world graphics and compact
two-line per-defender HUD rows use the existing debug switch.

- Cyan: actual planned pursuit target; blue: scaled predicted target before cap.
- White: approach direction. Gold rings: breakdown entry/exit.
- Orange ring: profile commit cap. Green ring: reference physical wrap range.
- Magenta: selected wrap/lunge target retained through commitment.
- Red: locked lunge direction. Existing yellow/lime launch/correction vectors,
  reachable candidates, body targets, swept capsules and time-of-impact markers remain.
- HUD: AI stage and execution state, behavior profile, decision/rejection reason,
  last evaluated wrap/lunge reachability, required stopping distance, contact time,
  prediction confidence, lock status and swept time of impact.

Opponent.DecisionDebug exposes the same data for automated scenarios without
rendering. During commitment, reachability flags describe the last selection;
live correction uses CurrentContactSolution. The selected point is the commitment
point, not a new chase target. Reset/recovery completion clears decision diagnostics.

## Validation and manual tuning

Tests cover stage transitions/hysteresis, cone gating, profile field isolation,
profile-name independence, invalid data/references, real lunge/ragdoll/recovery for
all three profiles, and existing reachable aiming/swept contact regressions.
Hidden-window rendering checks both 640x480 and 1280x720 and exports six diagnostic
captures to artifacts/defender-ai/. These are geometry/HUD probes, not gameplay
screenshots or a substitute for a visual playtest.

Manual tuning still needed:
- Lead cap and containment offset versus fast lateral cuts, rear pursuit and sideline approaches.
- Reaction/braking margins, overshoot and entry/exit hysteresis across actual body speeds.
- Commit timing, close wrap-versus-lunge preferences, misses and correction feel against jukes.
- Multiple defenders converging on the same lane; no squad coordination or avoidance was added.
- Debug readability with all defenders and the profile/physics HUD enabled.

Keep Balanced as the comparison baseline; change one profile parameter at a time.
