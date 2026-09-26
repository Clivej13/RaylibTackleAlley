# Returner touchdown taunts

Generate using Blender MCP `run_blender_script` with
`Tools/Blender/create_returner_taunts.py`; then run
`Tools/Blender/validate_returner_taunts.py` through the same MCP.

| Returner | Action | File stem after football_player_taunt_ |
| --- | --- | --- |
| Marcus Reed, speed | TauntSpeedSalute | speed_salute |
| Eli Brooks, acceleration | TauntBurstPump | burst_pump |
| Jalen Price, agility | TauntHeelFlourish | heel_flourish |
| Darius Stone, power | TauntPowerFlex | power_flex |
| Noah Grant, balanced | TauntCaptainSalute | captain_salute |

Marcus gathers then sweeps his free hand forward in a quick confident salute.
Eli gathers and punches his fist upward twice. Jalen makes two light heel flicks
with an open-hand flourish. Darius performs a broad single-arm bicep flex with two
chest-up squeezes. Noah gives a measured temple salute and nod. The grouped-finger
rig is retained; there is no isolated index-finger pointing control.

Each clip has frames 1-157 at 60 Hz (2.6 seconds), smooth eased pose transitions,
identity Root, a planted right foot, and identical standing entry/exit transforms.
The existing fitted right-hand carry chain is retained so the football stays in
hand. Four clips keep both feet planted; Jalen lifts the left heel while keeping
the foot level. No root motion, extra bones, constraints or runtime IK are added.

The source standing pose comes from the existing bicep taunt, the right-hand grip
from CarryRun, and the clothed mesh/rig from lowpoly_human_rigged.blend. The existing
upgrade_player_hands.export helper writes one named Y-up GLB action with 24 joints.
Preview-only football geometry is added after export and excluded from GLBs.
Saved editable blends retain the 60 Hz clip. Five sampled poses and a full 30 fps
MP4 per clip are under Assets/Models/ReturnerTauntPreviews. Preview materials are
the source rig's neutral colours; runtime uses the selected returner's atlas.

Runtime mapping: the optional `Taunt` field in each returners.json entry references
an assets.json ModelAnimations key, e.g. FootballPlayerTauntSpeedSaluteAnimations.
The asset must contain exactly one valid clip. Startup loads the roster's keys;
BallCarrier selects the assigned clip for touchdown, plays it once and holds its
last frame. Completion uses the loaded frame count at raylib's 60 Hz sampling rate.
Reset restores CarryRun. The menu loops the assigned clip from frame zero on each
highlight change and re-entry using its own playback clock and one reusable model.
Missing/null Taunt, unregistered keys and missing export files use the original
FootballPlayerTauntBicepFlexAnimations;
blank keys fail catalog validation. Defender celebrations are unchanged.

Validation reports record original model/animation SHA-256 preservation, unchanged
rig/mesh/weights, finite bone transforms, identity Root, standing endpoint equality,
planted support, one named export and 24 joints. The separate clearance audit samples
every other frame on clothed meshes and checks the left hand/forearm against jersey,
helmet, opposite hand/forearm and ball, plus finite geometry and cleat floor clearance.
This is a sampled surface-intersection check, not a proof of all possible contacts.

The targeted Release runtime tests exercise five assigned clips on the shared model,
menu poses, football transforms, completion/hold/reset, old default celebration,
and existing carry/cut/juke/spin playback. Existing source animations are not edited.

Manual polish: watch the five MP4s for gesture timing, then compare touchdown
readability at the gameplay camera with actual returner proportions. Review the
carry-to-standing handoff at different locomotion phases: the existing game switches
clips directly, so this pass does not add an animation crossfade. Check sleeve/pad
seams, football-to-torso clearance on the heavy profile, and Jalen's toe clearance
on the rendered field. The neutral source previews do not apply profile scaling.
