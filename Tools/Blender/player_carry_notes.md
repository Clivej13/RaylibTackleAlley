# Right-arm carry locomotion

Historical notes for the initial 20-joint proxy carry. The current assets use
the production football and 24-joint hand rig. See
[player_football_hand_notes.md](player_football_hand_notes.md) for the current
rebuild, grip, exports, previews and validation results.

Run `create_player_carry_animations.py` with Blender MCP's `run_blender_script`,
then run `validate_player_carry.py` with the same tool. Project root is the
RaylibTackleAlley repository. Requires the existing approved Jog/Run/Sprint
blends and validation GLBs; never regenerates or overwrites those sources.

## Outputs

Each `Assets/Models/football_player_carry_{jog,run,sprint}.blend` retains all
actions from its approved source and adds one active carry action.
The matching `.glb` contains only that carry clip, with the existing player
geometry, named equipment meshes, skeleton and Head-only helmet skinning.

| Action | Source | Keyed frames | Playback frames | FPS | Duration |
| --- | --- | --- | --- | --- | --- |
| CarryJog | Jog | 1-29 | 1-28 | 30 | 28/30 s |
| CarryRun | Run | 1-25 | 1-24 | 30 | 24/30 s |
| CarrySprint | Sprint | 1-21 | 1-20 | 30 | 20/30 s |

Only quaternion rotation tracks for `UpperArm.R`, `LowerArm.R`, and `Hand.R`
are changed. Root, Hips, all leg/foot bones, Spine, Chest, Neck, Head,
both clavicles, and the complete left arm retain their approved curves.
The carrying arm has a small periodic impact response, increasing from Jog
to Sprint, and otherwise follows the existing moving chest.

## Preview football

No standalone football model was found in the repository's assets.
`TEMP_PreviewFootball_DO_NOT_EXPORT` is a temporary 0.28 m long, 0.164 m wide
ellipsoid saved in each preview blend. It is a separate object parented to
`Hand.R`, positioned over the forearm beside the right ribs. The initial
center is (-0.275, 1.244, -0.101) in the chest's undeformed rig coordinates;
the longitudinal axis follows (0.02, 0.115, -0.237). The hand-relative
matrix is stored as a custom property on the proxy for later fitting.

The proxy is explicitly excluded from every GLB. Bone parenting here is
solely for Blender previews, not a proposed runtime attachment mechanism.
A production football must be supplied and integrated as a separate game
object. The existing hand has no finger bones, so the grip is an overall
hand pose rather than articulated finger wrapping.

## Validation and review

`Assets/Models/CarryPreviews` contains quarter-cycle PNG previews, full-cycle
MP4 previews, `validation.json`, and `source_preservation.json`.
The latter hashes pre-existing repository files, excluding build products,
Blender backups and the new carry outputs/scripts. The validator verifies
those files remain unchanged, including existing dirty C# source files.

Validation compares complete original actions, all non-carry curves, and
quarter-frame pose matrices for all other bones. It also compares geometry,
weights, modifiers, hierarchy, rest skeleton, timing, and exported channels.
Right-arm GLB translation/scale decomposition roundoff is below 0.000001.
It checks Head-only helmet skinning, no root motion, loop closure, stable
hand attachment, proxy exclusion from GLB, and sampled proxy/torso clearance.

Reviewed four cycle phases per clip from the carrying side. No obvious ball,
elbow, shoulder or equipment clipping was observed. Sampled proxy vertices
show no torso penetration; this is a preview check, not collision physics.
Ball movement relative to Chest is approximately 5.7/9.7/12.9 mm for
Jog/Run/Sprint. Maximum nearest hand-to-ball surface gap is below 0.13 mm.
All mesh positions, including the proxy, match exactly at loop endpoints.
No game code, asset registry, original animation asset, or source rig was edited.
