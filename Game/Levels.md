# Level catalog

Program loads and validates levels.json before creating GameApplication or a native
window, then explicitly selects level-1. There is one authored level, no level menu,
no progression. Shared AI behavior profiles are described in [DefenderAI.md](DefenderAI.md).

## Schema

The root contains two required arrays:

- DefenderProfiles: entries with a unique Id and an existing PlayerProfile object.
  These are the four pre-migration defender identities/body/movement ratings,
  copied unchanged from config.json. These body/identity profiles are separate from
  the reusable behavior profiles in defender-profiles.json.
- Levels: entries with Id, Name, FieldWidth, PlayerSpawn and Defenders.

A level entry looks like this (only one defender shown for brevity):

```json
{
  "Id": "level-1",
  "Name": "Level 1",
  "FieldWidth": 24,
  "PlayerSpawn": { "X": 0, "Z": 0 },
  "Defenders": [
    { "X": -5.5, "Z": -18, "Profile": "marcus-hill", "BehaviorProfile": "balanced" }
  ]
}
```

Distances are metres. X spans the field horizontally; forward is negative Z.
Y is always zero. FieldLength, EndZoneLength, asset dimensions and shared tuning
remain in config.json. Level 1 retains all four defenders at (-5.5,-18),
(5.5,-31), (-4.5,-46), (4.5,-61), with the player at (0,0) and width 24.

IDs are case-sensitive lowercase letters/digits separated by hyphens. Every level
requires a nonblank name, explicit finite positive width, explicit X/Z spawn
coordinates, and at least one defender. Width must fit the turf/stadium and exceed
the player boundary diameter. Spawns must lie strictly inside the sidelines and
between Z=0 (inclusive) and the goal line (exclusive). Unknown JSON fields,
duplicate level/profile IDs, missing fields, null entries, invalid player profiles,
and missing/unknown profile references fail loading with contextual errors.

## Applying a level

Startup loads defender-profiles.json, passes its catalog into LevelCatalog.Load,
and validates every required BehaviorProfile reference. LevelCatalog resolves body
and behavior profile references once and exposes immutable LevelDefinition
objects with read-only defender lists. Pass one to:

```csharp
new TackleAlleyGame(tuning, input, assets, levels.Resolve("level-1"))
```

CurrentLevel exposes the chosen setup. The constructor projects geometry into a
private copy of tuning for the existing FootballField and BallCarrier component
APIs, then constructs opponents from CurrentLevel.Defenders. It does not mutate
the caller's config. ResetRun and SelectReturner retain that level and its spawns.

FieldWidth, PlayerSpawn and OpponentSpawns remain C# compatibility properties for
standalone components and existing programmatic tests; they are no longer authored
in config.json. The original game constructor adapts those legacy values to a
level, including deliberately empty practice setups. Normal startup always uses
the explicit catalog-backed constructor, which enforces the nonempty requirement.

Both build and publish copy levels.json and defender-profiles.json. LevelCatalogTests use a frozen pre-migration
fixture to verify Level 1 fidelity, validate malformed catalogs, check geometry
overrides/config isolation, and cover returner selection/reset. ReturnerMenuTests
also run their keyboard/gamepad/mouse flows with the authored Level 1.
