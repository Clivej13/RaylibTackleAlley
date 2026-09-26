# Player identity and appearance

Each player owns an immutable PlayerProfile snapshot and a PlayerVisualProfile
calculated at construction. Resetting a run preserves both, including jersey
numbers and the selected uniform. Restart the game to load profile edits.

| Field | Units / valid values | Default |
| --- | --- | --- |
| Name | Nonblank text | Player |
| JerseyNumber | Integer 0–99 (0 is valid; no leading zero) | 10 |
| Height | Metres, finite, 1.65–2.05 inclusive | 2.00 |
| Weight | Kilograms, finite, 70–160 inclusive | 110 |
| Build | Finite adjustment from -1 (leaner) to +1 (heavier) | 0 |
| Strength | Integer 1–100 | 50 |

The default matches the previous 2 m player and unchanged width/depth.
Height is the fitted model height, including the existing helmet and footwear;
animation still adds the authored crouches, strides and jumps. Height and weight
do not change movement ratings; Weight now supplies total physical mass.
Speed, Acceleration and Agility independently modify movement as described below.

Set BallCarrierProfile at the root of config.json. Authored defender identities now
live in levels.json's DefenderProfiles registry; each level spawn names its Profile
reference. See [Levels.md](Levels.md) for the startup schema and validation.

The older standalone configuration shape below remains supported by the C# APIs
for compatibility, but OpponentSpawns is no longer authored in config.json:

```json
{
  "BallCarrierProfile": {
    "Name": "Jordan Reed", "JerseyNumber": 10,
    "Height": 1.85, "Weight": 95, "Build": -0.25
  },
  "OpponentSpawns": [
    {
      "X": -5.5, "Z": -18,
      "Profile": {
        "Name": "Marcus Hill", "JerseyNumber": 24,
        "Height": 1.65, "Weight": 75, "Build": -1
      }
    },
    {
      "X": 4.5, "Z": -61,
      "Profile": {
        "Name": "Sam Taylor", "JerseyNumber": 97,
        "Height": 1.78, "Weight": 150, "Build": 1
      }
    }
  ],
  "ShowPlayerProfiles": true
}
```

Omitted profiles/fields use the default profile for older configurations.
Explicit null profiles, null/blank names, and out-of-range fields are rejected.
Errors identify the profile and field, for example OpponentSpawns[1].Profile.Weight.
The shipped examples span lean, average, stocky and heavy appearances.

## Shared visual calculation

The reference height is the existing default PlayerVisualHeight, 2 m.

- HeightRatio = Height / 2.
- ReferenceWeight = 110 × HeightRatio³.
- BuildValue = clamp(0.9 × (Weight / ReferenceWeight − 1) + 0.5 × Build, -1, 1).
- WidthRatio = clamp(1 + 0.14 × BuildValue, 0.86, 1.14).
- DepthRatio = clamp(1 + 0.18 × BuildValue, 0.82, 1.18).

This compares weight with a similarly proportioned player at that height.
Build descriptions are Lean below -0.2, Average below 0.25, Stocky below 0.65,
and Heavy otherwise. The overlay shows this derived description plus the input
Build adjustment; it is normal for weight to influence the description.

The uniform height scale is (2 / model bounds height) × HeightRatio.
The final XYZ scale multiplies it by (WidthRatio, 1, DepthRatio).
Ground offset is -bounds.Min.Y × vertical scale. Both player classes use
the same calculations; neither changes the original model, bind pose or clips.
Profile Height now controls rendered size; the legacy PlayerVisualHeight and
OpponentVisualHeight configuration fields are retained for old config compatibility
but do not override profile height.

Animation, facing, contact probes, Hand.R football attachment, ragdoll handoff and
recovery use the same XYZ scale. The camera continues to follow the existing player
root with its configured distance/height/look-at settings. It is not scaled with
body width or weight.

Nonuniform proportions can produce affine bone transforms during physics.
Sized ragdolls use exact matrix skinning into per-instance animated buffers.
Recovery blends the residual affine component while interpolating rotation with
quaternions, preserving the handoff pose and returning to the existing get-up clip.
Shared model vertices, animation data and team textures are never edited.

## Uniform numbers and overlay

The selected offense/defense atlas is copied when ApplyUniform runs. The profile's
number is painted on the Chest and Back number patches, then uploaded with mipmaps.
Numbers use contrasting light or navy block digits. All artwork outside those
patches stays intact. Custom uniforms must follow the existing atlas layout and
reserve those patches for numbers; graphics inside them are replaced.

BallCarrier and Opponent own their generated textures, release the old texture
after a successful uniform swap, and implement IDisposable. TackleAlleyGame.Dispose
releases all generated uniforms before AssetManager.UnloadAll and CloseWindow.
The lower-level PlayerUniform.ApplyUniform still supports a borrowed, unnumbered
texture for callers that need the original atlas.

ShowPlayerProfiles enables the dark translucent roster panel with white text and
sky-blue details, matching the existing HUD. It is enabled in the example config.
Set it to false to hide it. Each entry shows name, jersey number, metres, kilograms
and derived build description.

## Validation

Run dotnet test Tests/Controls.Tests.csproj -c Release.
PlayerProfileTests covers defaults, ranges, proportions, configuration and resets.
PlayerProfileVisualTests loads real assets to check animation poses, ground fit,
scaled Hand.R attachment, independent uniform numbers, exact skinning handoff,
and complete recovery at the short/lean and tall/heavy extremes.

## Phase two: movement ratings

Speed, Acceleration and Agility are integer profile fields from 1 to 100.
All three default to 50 when omitted. Fractional values and strings are rejected
by JSON deserialization; values outside the range fail profile validation with
the profile/field path. Each defender has independent ratings alongside its spawn.

PlayerMovementAttributes snapshots the profile and global movement baselines at
player construction. Reset preserves this same object. Restart/recreate players
after changing configuration; resetting a run is not a tuning reload.

| Modifier | Rating 1 | Rating 50 | Rating 100 |
| --- | --- | --- | --- |
| Speed | 0.80 | 1.00 | 1.20 |
| Acceleration | 0.75 | 1.00 | 1.25 |
| Steering, facing and lateral response | 0.85 | 1.00 | 1.15 |
| Reversal speed loss | 1.15 | 1.00 | 0.85 |
| Reversal recovery delays | 1.15 | 1.00 | 0.85 |
| Juke and spin displacement | 0.90 | 1.00 | 1.10 |

All mappings share piecewise linear interpolation through (1, low), (50, 1)
and (100, high). The lower interval has 49 steps and the upper 50 steps, keeping
50 exactly neutral. The existing configuration remains the baseline balance.

Speed multiplies the carrier's jog/run/sprint targets and the defender's
jog/run/engaged-sprint/ready targets by the same amount. It does not multiply
acceleration or deceleration. Recovered defenders still pursue using their
engaged sprint target. Lunge interception uses the defender's actual current
speed and its own rated jog minimum, with the observed carrier velocity.
Pursuit lead uses measured displacement and the carrier's rated tier maximum
rather than assuming the global tier speed.

Acceleration multiplies ForwardAcceleration for every existing ramp: tier
changes, acceleration from rest, post-reversal recovery, cut/evade recovery,
pursuit and resuming after getting up. Deceleration stays at the baseline value.
Existing start/reset speed is preserved (players start at their selected pace);
ratings do not introduce a new start-from-rest state.

Agility multiplies lateral speed, sprint steering response, run-facing response,
defender ready-facing response and the defender's carrier-velocity tracking
response. Normal steering and pursuit direction already snap at the baseline:
ratings at/above 50 retain that instantaneous ceiling; lower ratings add a short,
frame-rate-aware direction lag. Higher agility still increases the other response
rates above baseline. Ready positioning uses this direction response while its
maximum pace remains governed by Speed.

Cuts retain their raw-input gesture thresholds/windows, authored heading handoff
and duration. Their lateral movement and steering on exit use the player's rated
lateral/facing response. Agility does not speed up the cut clip. Jukes/spins keep
their existing timers, input detection and momentum retention but multiply travel
by the evade modifier. Entry pace is captured relative to the player's rated
normal running speed, so recovery during the move cannot lengthen its travel.

Reversal loss is baseline loss × the inverse agility modifier, clamped to [0, 1].
Initial, additional and maximum reversal delays use the same inverse mapping.
Chained penalties, zero-speed bounds and acceleration lockouts remain in place;
the rated acceleration is used only after the delay expires.

Locomotion playback is actual scalar movement pace divided by the original,
unmodified speed of the selected tier, clamped to 0.35–1.5. Defender pursuit
integrates ramp distance so different update subdivisions agree. Moving ready
clips use the ready baseline; the stationary ready pose runs at authored speed.
Cuts, jukes, spins, tackles and celebrations retain their authored timing.
Get-up playback now uses the bounded physical recovery rate described below.
The camera still uses its existing world-speed framing.

Example profile:
```json
{
  "Name": "Jordan Reed", "JerseyNumber": 10,
  "Height": 1.85, "Weight": 95, "Build": -0.25,
  "Speed": 65, "Acceleration": 75, "Agility": 80
}
```

The shipped defenders range from fast/agile (85/90/90) to slower/heavier
(30/30/25). Set all three ratings to 50 to compare with the prior movement balance.
The roster panel now includes SPD / ACC / AGI and current / target speed in m/s.
Physics-owned players show physical velocity and a zero locomotion target.

MovementAttributesTests covers neutral baselines, mapping anchors, independent
caps/rates, lateral/steering response, penalties and delays, evade distances,
per-defender launch speeds, pursuit inputs, reset persistence and validation.
Real-asset playback tests cover extreme ratings and unchanged action clocks.

## Phase three: physical attributes

Every carrier and defender constructs one immutable PlayerPhysicalAttributes object,
shared by its animated contact probe and active ragdoll. Reset retains it and clears
contact diagnostics/cooldowns. Recreate players after editing profiles or physical
baselines. Strength defaults to 50; JSON rejects fractions and validation rejects
ratings outside 1–100. Dimensions, mass proportions, mass totals and multipliers
must be finite and positive.

| Derived value | Formula |
| --- | --- |
| Height ratio h | Height / 2 |
| Radius scale r | clamp(sqrt(h) × sqrt(WidthRatio × DepthRatio), 0.90, 1.05) |
| Collision height | Height in metres |
| Torso/pelvis/limb radii | Existing baseline radius × r (limb contact also uses ContactLimbRadiusScale) |
| Wrap/dive reach | Existing baseline reach × h |
| Nominal tackle contact height | 1.1 × h metres; resolution uses the actual capsule impact point |
| Body mass | Configured Weight in kilograms |
| Body-part mass | Weight × baseline part mass / sum of all baseline part masses |
| Strength, application, resistance, motor factors | Shared piecewise rating conversion: 1 → 0.8, 50 → 1, 100 → 1.2 |
| Balance/support factor | Same conversion: 1 → 0.9, 50 → 1, 100 → 1.1 |
| Impulse cap scale | clamp(Weight / 110, 0.65, 1.45) |
| Recovery playback factor | clamp(balance / (Weight / 110)^0.15, 0.85, 1.15) |
| Momentum | Weight × current velocity, a vector in kg m/s |

Pelvis/chest/head/upper arm/lower arm/upper leg/lower leg baseline proportions are
18/25/6/3/2/9/5, with each limb counted twice (87 total). They are proportions now:
the default 110 kg profile has 110 kg of ragdoll mass, not 87. The final segment
receives the rounding remainder so the distributed masses sum to Weight. Strength
50 leaves every strength-dependent factor exactly neutral; weight changes remain
independent of this neutral strength behavior.

Animated bone transforms already contain height and build scaling. Ragdoll segment
endpoints, joint anchors, ground positions and centre of mass are captured directly
from that world pose, without applying height twice. Capsule radii replace the
height component with r; the bind pose and anatomical limits remain unchanged.
Broad-phase and contact-substep distances include the maximum supported height
ratio (2.05 / 2). Each player's boundary radius also uses r.

### Contact and tackle outcomes

The contact normal n points from defender to carrier. Closing speed is
max(0, dot(vDefender − vCarrier, n)); unrelated tangential speed is excluded.
The actual capsule contact selects the struck body and impact height.

Impact = defender mass × (closing speed + wrap control allowance) ×
defender force factor × type effectiveness × positional effectiveness.

Resistance = carrier mass × (2 + max(0, −dot(vCarrier, n))) ×
carrier resistance factor × balance/support factor.

The 2 m/s resistance term represents stationary weight-bearing support. Grounded
wraps have a 2 m/s control allowance, allowing a close stationary wrap; dives have
no allowance and use their actual airborne velocity, including vertical velocity.
Dive type effectiveness is 1.1, wrap effectiveness 1.0. Position combines facing
alignment with physical reach and reachable contact height; out-of-reach contact
has zero tackle impact. Existing AI commitment/interception cones still apply.

Severity = impact / resistance is continuous. The following bands select response
policy; the solver determines actual motion and only ground contact ends a run:

| Severity | Response |
| --- | --- |
| Below 0.45 | Glancing: carrier stays animated, receives bounded speed loss/deflection |
| 0.45–0.85 | Staggered: stronger animated deflection and speed loss |
| 0.85–1.4, wrap | Controlled wrap: both bodies active, carrier keeps support |
| Other cases below 1.7 | Resisted: active ragdoll with carrier motors/support retained |
| 1.7–2.5 | Driven back: contact-region yielding and physical momentum transfer |
| At least 2.5 | Brought down: decisive impact with regional yielding; ground still decides completion |

Normal impulse = closing / (1/mDefender + 1/mCarrier) × transfer.
Transfer = clamp(force / resistanceFactor × min(1, severity / 0.85), 0, 1.2).
Both bodies receive equal/opposite impulses. The cap uses the smaller participant's
bounded mass scale. Tangential motion is retained at impact; ongoing friction and
wrap control can dissipate it. Ground reaction supports animated glancing contacts.
A 0.4-second per-defender cooldown prevents repeatedly applying the handoff kick.

Wraps maintain a short, dissipative velocity constraint for
clamp(0.65 × defenderForce / carrierResistance × defenderMass / carrierMass,
0.2, 1.2) seconds. Control decays relative velocity at 6 × defenderForce /
carrierResistance per second. The hold breaks beyond 1.5 × wrap reach or on ground
impact. Heavy/strong carriers retain more travel and can drag lighter defenders;
strongly unfavorable wraps remain glancing contact instead of capturing the carrier.

Dive rotation uses actual height / carrier height: below 0.45 multiplies the
angular energy share by 1.35; above 0.75 by 1.15; torso contact by 0.8.
The angular budget never exceeds dissipated collision energy. Existing hit-limb,
pelvis and chest shares, angular-speed caps and anatomical head/neck limits remain.
The head remains excluded as a direct collision target; high contact rotates the
upper body through the existing safe joints.

Active motors multiply correction and angular acceleration by Strength. Motor
impulses divide by summed inverse inertias, so force grows with player mass.
Support is expressed as acceleration applied to each segment (equivalent force =
segment mass × acceleration); heavier bodies therefore retain adequate support.
Balance/support use their narrower Strength factor. Recovery captures the final
scaled ragdoll pose, pelvis position and torso orientation, then blends to the
existing clips using the bounded recovery rate. It never rebuilds a default pose.

The debug HUD shows both carrier and active/nearest defender: Height, Weight,
Strength, total calculated mass, vector momentum, impact/resistance scores,
selected response and last applied contact impulse. Enable ShowPlayerProfiles
or DrawGameplayDebug.

### Example profiles

The root config contains a light carrier and four independent defenders.
BallCarrierProfileExamples holds a validated power-carrier alternative; copy it
into BallCarrierProfile and restart to select it.

| Example | Height | Weight | Speed/Acceleration/Agility | Strength |
| --- | --- | --- | --- | --- |
| Jordan Reed, light carrier | 1.85 m | 95 kg | 65/75/80 | 55 |
| Power carrier alternative | 1.98 m | 135 kg | 45/50/40 | 90 |
| Marcus Hill, agile defender | 1.65 m | 75 kg | 85/90/90 | 35 |
| Andre Cole, strong defender | 2.05 m | 145 kg | 45/40/35 | 95 |

PhysicalAttributesTests covers rating anchors, invalid inputs, mass distribution,
scaled skeleton/contact geometry, momentum, tackle direction, reach, impact region,
wrap control, force normalization, caps and reset persistence. Existing real-asset
profile tests exercise ground alignment and recovery with extreme Strength as well
as the full height/weight/build corner combinations. Contact regressions now assert
normal-only impulse transfer instead of cancelling unrelated tangential velocity.

