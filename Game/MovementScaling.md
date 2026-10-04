# Shared movement scaling

Every returner and defender uses the same TackleAlleyConfig.MovementScaling object,
the same base tier speeds and the same PlayerMovementAttributes calculation.
There are no per-player or per-team scaling overrides. Profiles contain raw ratings;
equal Speed and Acceleration ratings produce equal movement values in either role.

Options > Movement is available from the main menu and Pause:
- Player Speed: 50–150%, in 5% steps; default 100%.
- Player Acceleration: 25–200%, in 5% steps; default 100%.
- Left Stick Sensitivity: 25–100%, in 5% steps; shipped default 70%.

Speed and acceleration sliders apply to every active player immediately, without
resetting positions or the run. Current velocity transitions using the existing
acceleration/deceleration rules when simulation resumes. PlayerMovementAttributes
is rebuilt for the carrier and all defenders; selected returners and future runs
inherit the same settings. Committed tackle trajectories remain committed.
The two scales save as MovementSpeedScale and MovementAccelerationScale in user settings.

## Calculation

MovementScaling owns SpeedScale and AccelerationScale, plus the shared authored
SpeedTierInfluence and AccelerationTierInfluence:
- Speed rating influence, jog / run / sprint: 0.05 / 0.15 / 0.30.
- Acceleration rating influence, jog / run / sprint: 0.25 / 0.25 / 0.25.

For rating r, offset = (r - 50) / (r <= 50 ? 49 : 50).
Rating multiplier = 1 + offset * influence.
Tier speed = base tier speed * SpeedScale * Speed rating multiplier.
Tier acceleration = ForwardAcceleration * AccelerationScale * Acceleration rating multiplier.

The shared base speeds are PlayerSlowSpeed / PlayerForwardSpeed / PlayerSprintSpeed;
shipped values are 6.5 / 9 / 11.5 m/s. Despite the historical property names, these
now apply to every player. Baseline animation speeds remain unscaled so playback
can follow actual travel speed. Speed and Acceleration ratings remain independent.
Ready movement uses the jog speed and jog acceleration. AI pace selection, turning,
reaction times, evades, and committed tackle behavior are separate.

At default 100% speed, #24 Marcus Hill (Speed 85) sprints at 13.915 m/s.
Darius Stone (44) reaches 11.078; Noah Grant (70) 12.880; Eli Brooks (74) 13.156;
Jalen Price (78) 13.432; Marcus Reed (98) 14.812. The ordering remains the same
at every speed slider value. Pursuit paths and preparation still affect catches.

## Configuration and validation

The old ReturnerMovementScaling / DefenderMovementScaling blocks and
OpponentJogSpeed / OpponentRunSpeed / OpponentSprintSpeed keys have been removed.
Migrate custom tuning into MovementScaling and the three shared base speed fields.
Old JSON keys have no effect; omitted new settings use defaults.
Existing user settings without the two new scale fields retain authored defaults.

Influences must be finite in [0, 1); scales must be within their menu ranges.
Startup checks both roles through the same validation: finite ordered
0 <= jog < run < sprint and nonnegative finite acceleration at every rating.
Zero ForwardAcceleration remains supported. Invalid saved scale values retain
authored defaults and report a settings error.

Tests cover equal ratings across roles, scale bounds, rating order, independent
acceleration, actual full-sprint travel, native main/pause submenu navigation,
live updates of all active players, restart/returner changes, and persistence.
Run: dotnet test Tests/Controls.Tests.csproj -c Release.
