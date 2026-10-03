# Shared role movement scaling

TackleAlleyConfig owns ReturnerMovementScaling and DefenderMovementScaling.
Each has SpeedTierInfluence and AccelerationTierInfluence, each containing Jog,
Run and Sprint. Jog is the returner's slow tier. These replace the three flat
ReturnerJogSpeedInfluence / ReturnerRunSpeedInfluence / ReturnerSprintSpeedInfluence
fields; migrate customized config to the nested fields.

| Role | Speed influence: jog / run / sprint | Acceleration influence: jog / run / sprint |
| --- | --- | --- |
| Returner | 0.05 / 0.15 / 0.30 | 0.25 / 0.25 / 0.25 |
| Defender | 0.20 / 0.20 / 0.20 | 0.25 / 0.25 / 0.25 |

Defender defaults intentionally retain their previous uniform Speed mapping;
their tiers can be tuned independently without affecting returners. All player
ratings, AI behavior presets, level definitions and base speeds are unchanged.

This checkout stores raw ratings directly in PlayerProfile (there is no separate
PlayerAttributes type). No profile stores scaling overrides or effective movement.
PlayerMovementAttributes calculates and caches both roles' movement at construction.

For rating r, offset = (r - 50) / (r <= 50 ? 49 : 50).
Multiplier = 1 + offset * influence.
Tier speed = role tier baseline * Speed multiplier.
Tier acceleration = ForwardAcceleration * Acceleration multiplier.
Speed and Acceleration never feed each other's calculation.

BallCarrier uses its requested SpeedTier, including acceleration after penalties.
Opponent pursuit uses its selected Pace; tackle-ready movement uses jog acceleration.
Ready speed retains PlayerSlowSpeed as its baseline, multiplied by defender jog
influence. Contact prediction receives the relevant tier acceleration. AI decisions,
reaction times, pursuit distances and action durations remain separate.

All influences must be finite in [0, 1). Startup checks both roles before window or
asset creation. Results must satisfy finite 0 <= jog < run < sprint, nonnegative
finite ready speed, and nonnegative finite acceleration for every supported rating.
Zero ForwardAcceleration remains supported for existing stationary-ramp scenarios.
Endpoint and midpoint checks cover the piecewise-linear mapping. Null config
blocks and unknown nested fields fail clearly. Standalone player construction
also validates both roles. Recreate players after tuning; reset retains cached values.

At rating 100, returner speed multipliers are 1.05 / 1.15 / 1.30. Over a fixed
distance, sprint leaves about 77% of the neutral travel/reaction time, versus
87% for running. This changes the available travel window without changing dodge
timers or AI reaction settings; controllability still needs hands-on playtesting.

## Shipped effective values

Speeds are m/s; acceleration is m/s² and is currently the same in all three tiers.
Ready applies only to defenders.

| Player | Jog/slow | Run | Sprint | Acceleration, all tiers | Ready |
| --- | --- | --- | --- | --- | --- |
| Marcus Reed | 6.812 | 10.296 | 14.812 | 8.833 | — |
| Eli Brooks | 6.656 | 9.648 | 13.156 | 10.375 | — |
| Jalen Price | 6.682 | 9.756 | 13.432 | 9.500 | — |
| Darius Stone | 6.460 | 8.835 | 11.078 | 7.823 | — |
| Noah Grant | 6.630 | 9.540 | 12.880 | 9.167 | — |
| Marcus Hill | 4.560 | 7.410 | 10.260 | 10.000 | 7.410 |
| Devon Brooks | 4.160 | 6.760 | 9.360 | 8.958 | 6.760 |
| Andre Cole | 3.918 | 6.367 | 8.816 | 7.908 | 6.367 |
| Sam Taylor | 3.673 | 5.969 | 8.265 | 7.483 | 5.969 |

The formulas preserve the previous defaults mathematically. Unifying the old
defender and Acceleration interpolation can change the last float rounding bits
(less than 0.00001 in roster tests); no intentional gameplay balance changes.
Tests that previously made jog >= run to seed approach momentum now set current
speed directly, retaining their scenarios under the stricter validation.

MovementScalingTests covers role separation, tier influence, rating independence,
invalid config, roster preservation and profile shape. MovementAttributesTests
also exercises real carrier and defender travel with different tier acceleration,
including ready movement. Run: dotnet test Tests/Controls.Tests.csproj -c Release.
