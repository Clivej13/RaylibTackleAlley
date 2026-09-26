# Returner selection

The app loads and validates returners.json before creating the window. Both build
and publish output include it. Restart the app after editing the roster.

The root object has a Returners array. Each entry has a unique, case-sensitive
lowercase Id (letters, digits and separating hyphens) and a Profile:

```json
{
  "Returners": [
    {
      "Id": "marcus-reed",
      "Uniform": "ReturnerMarcusUniform",
      "Taunt": "FootballPlayerTauntSpeedSaluteAnimations",
      "Profile": {
        "Name": "Marcus Reed",
        "JerseyNumber": 11,
        "Height": 1.84,
        "Weight": 82,
        "Build": -0.55,
        "Strength": 30,
        "Speed": 98,
        "Acceleration": 62,
        "Agility": 58,
        "Juke": 52
      }
    }
  ]
}
```

Id, Profile and all profile fields are required in this catalog; Uniform and Taunt
are optional asset keys. Unknown fields, empty rosters, null
entries, duplicate/invalid IDs, blank names, invalid numbers and out-of-range
attributes fail startup. Ratings are integers from 1 to 100, jersey numbers are
0–99, height is 1.65–2.05 metres, weight is 70–160 kilograms, and build is -1–1.
The five placeholders emphasize top speed (Marcus Reed), acceleration (Eli Brooks),
agility/jukes (Jalen Price), strength (Darius Stone), and balance (Noah Grant).

Start opens the Menus 0.1.3 ListWithDetail layout. menu.json defines only the menu
and Back item; GameApplication replaces its items from the catalog before creating
MenuManager. Each button's Value carries its stable ID. MenuAction returns that
value as a JsonElement; GameApplication resolves it via ReturnerCatalog, calls
TackleAlleyGame.SelectReturner, and enters Playing after the run resets.

Selection constructs the active BallCarrier with an explicit immutable profile.
Movement, physical dimensions, strength/resistance, appearance and jersey number
derive from that profile. Reset retains the same carrier/profile; returning to
Main and choosing Start permits another selection. Replacing the carrier releases
its old model instance and numbered uniform. The old BallCarrierProfile setting
remains a compatibility fallback for callers constructing a carrier without an
explicit profile; it is not the selected roster and is never mutated by selection.

Generic speed baselines, action durations, input thresholds, camera settings and
physics tuning remain in TackleAlleyConfig. Ratings multiply those baselines:
Speed controls pace, Acceleration controls acceleration, Agility controls steering
and evasion, and Strength controls resistance/support. Juke additionally scales
juke displacement by 0.85/1.0/1.15 at ratings 1/50/100; it does not change spin
displacement or move duration. Height/weight/build also drive the existing visual
and physical calculations. Displayed ratings are the same values used by gameplay.

Highlighting reads SelectedItem and DetailPanelBounds from MenuManager. The detail
area shows name/number, all five ratings and the assigned animated celebration.
One reusable ModelInstance borrows the existing football-player model and clips.
Highlight changes immediately apply the profile scale and numbered jersey and create
a fresh looping AnimationPlayer at frame zero. Subsequent draws advance only that
preview clock; Back/menu exit clears it so re-entry restarts. One numbered texture
and one resizable render target are reused/replaced as needed. No BallCarrier is
created for highlighting. Preview resources are released before AssetManager.UnloadAll
and window shutdown. All five returners remain selectable without progression gates.

Validation:
- ReturnerTests covers catalog validation, selection, reset persistence, unchanged
  global tuning, and movement/physical attribute propagation.
- ReturnerMenuTests uses actual keyboard/gamepad automation and Menus 0.1.3 actions
  for selection, Back, restart and reselection. Hidden-window rendering at 640x480
  and 1280x720 checks visible model pixels, bounds, single-instance reuse, uniforms
  and carrier resource release.
- Run: dotnet test Tests/Controls.Tests.csproj -c Release.

Manual visual review still recommended: text readability and model framing at
your normal resolution/fullscreen, transitions between each returner, and the feel
of the deliberately different strengths during play.

## Gameplay archetypes

ReturnerDefinition.Profile uses the existing PlayerProfile type; there is no
separate ReturnerProfile or duplicate movement implementation.

| Returner | Archetype | Height / mass / build | SPD | ACC | AGI | JUK | STR |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Marcus Reed | Fast/light, weak in contact | 1.84 m / 82 kg / -0.55 | 98 | 62 | 58 | 52 | 30 |
| Eli Brooks | Short acceleration specialist | 1.72 m / 80 kg / -0.20 | 74 | 99 | 72 | 64 | 40 |
| Jalen Price | Agile juke specialist | 1.78 m / 86 kg / -0.35 | 78 | 78 | 99 | 99 | 38 |
| Darius Stone | Tall/heavy power returner | 2.02 m / 138 kg / 0.75 | 44 | 38 | 32 | 30 | 99 |
| Noah Grant | Balanced | 1.90 m / 104 kg / 0.05 | 70 | 70 | 70 | 70 | 70 |

The catalog is the source of player-specific ratings and dimensions. Derived
speeds and forces are computed once with the shared global baselines, rather than
storing another set of conflicting absolute speeds in JSON.

All rating multipliers interpolate linearly from rating 1 to 50 to 100, with 50
exactly neutral:

| Rating | Multipliers at 1 / 50 / 100 | Existing gameplay path |
| --- | --- | --- |
| Speed | 0.80 / 1 / 1.20 | Jog, run and sprint target speeds; gait playback follows actual speed |
| Acceleration | 0.75 / 1 / 1.25 | Forward acceleration toward the target, including pace recovery after evasion/contact/get-up |
| Agility | 0.85 / 1 / 1.15 | Lateral speed, sprint steering response and run-facing response |
| Agility | 1.15 / 1 / 0.85 | Reversal speed loss and acceleration lockout delays |
| Agility | 0.90 / 1 / 1.10 | General juke and spin displacement |
| Juke | 0.85 / 1 / 1.15 | Additional juke-only displacement factor, multiplied by the general evasion factor |
| Strength | 0.80 / 1 / 1.20 | Tackle resistance and active ragdoll motor strength (also tackle force for defenders) |
| Strength | 0.90 / 1 / 1.10 | Balance support and decay of sideways drift after upright contact |

Below-neutral Agility also adds normal-direction input lag; at/above 50 the
existing instantaneous normal steering remains capped. Sprint steering and facing
still differ above 50. Deceleration, action/animation durations, input thresholds,
camera and global tackle rules are unchanged.

At the shipped global baselines Marcus runs about 22% faster than Darius; Eli
accelerates about 33% faster than Darius; Jalen's full-pace juke travels about 39%
farther than Darius's. These advantages have explicit strength/size trade-offs.

Tackle resistance is mass × (2 + opposing normal speed) × strength resistance ×
balance support. Higher Strength therefore lowers the same hit's severity and
impulse transfer and shortens a defender's wrap hold. Outcomes still depend on
angle, relative velocity, body region and defender attributes; no player is
immune. Recovery playback scales by clamp(balance / (mass / 110)^0.15, 0.85, 1.15).

Height determines vertical model scale. Mass relative to height and Build produce
bounded width/depth proportions through PlayerVisualProfile. The same full XYZ
world transform places contact joints, the carrying hand, ragdoll and recovery.
Mass is distributed across the existing body-part proportions; contact, boundary
and ragdoll radii use the existing bounded body-size scale. For all five shipped
bodies that radius scale stays within 10% of the visible horizontal scale; capsule
centres follow the animated bones, and contact shapes do not jump at handoff.

ReturnerArchetypeTests measures actual forward travel, acceleration from rest,
sprint steering and complete jukes using the shipped JSON and global tuning.
PlayerProfileVisualTests exercises all five configured profiles with real assets,
checks contact centres in every carry/cut/juke/spin/taunt clip, identical capsules
at ragdoll activation, football attachment, finite skinning and complete recovery.
It also tests a shared contact taking down the light carrier but leaving the power
carrier upright, and isolates Strength at identical mass/dimensions.

Manual balance checks: compare each returner's speed/steering/reversal feel,
short-field acceleration, juke clearance, wrap resistance and recovery under
different approach angles. Review heavy/light silhouettes, foot sliding and
football grip through cuts, spins and tackles at normal gameplay camera distance.

## Individual returner uniforms

Each roster entry now optionally specifies `Uniform`, an asset key from assets.json.
The shipped roster uses five distinct atlases for both menu preview and gameplay.
Omitted/null keys, unregistered assets and missing export files use the offense fallback. Startup loads roster textures and
keeps them alive through shutdown; reset retains the selected carrier uniform.
See [returner artwork and reproduction](../Assets/Textures/Uniforms/Returners/README.md).

## Assigned touchdown celebrations

The optional `Taunt` asset key beside each roster entry's `Uniform` selects a
single-clip ModelAnimations asset. The five shipped assignments use distinct
2.6-second celebrations, also looped in the menu. Omitted/null keys, unregistered
assets and missing export files retain the original bicep flex; blank keys are rejected. Completion follows the loaded
clip length, the last frame holds, and reset restores carry locomotion.
Startup deserializes the manifest, filters missing optional roster exports before
AssetManager validation, and resolves one runtime catalog shared by preview and
gameplay. The authored JSON and profiles are unchanged. Base offense uniform and
bicep flex remain required; malformed or incompatible present assets still fail
validation instead of silently hiding authoring errors. Existing locomotion,
evasion, recovery and defender animation paths are unchanged.

See [animation authoring and validation](../Tools/Blender/player_returner_taunt_notes.md).
