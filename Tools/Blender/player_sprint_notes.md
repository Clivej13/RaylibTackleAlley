# Sprint

Run create_player_sprint_animation.py followed by validate_player_sprint.py through Blender MCP. The generator loads lowpoly_human_run.blend, preserves its Jog/Run/Pose_* actions, and adds only Sprint with fake-user retention. It saves lowpoly_human_sprint.blend and exports lowpoly_human_sprint_validation.glb with exactly one Sprint animation, including rigid equipment channels. Existing +Y-up/-Z-forward coordinates are retained (export_yup=False).

30 FPS; keys 1..21; playback/video frames 1..20; 20 intervals = 0.666667 seconds. Duplicate closing key is omitted from video playback. Same cameras, lighting, 640x720 Eevee render style as Run.

## Main authoring values (Run -> Sprint, degrees)

- Spine forward lean: 9 -> 16; head compensates by the same amount.
- Hip phase controls: [31,10,-17,-27,2,36] -> [40,12,-24,-34,5,48].
- Knee phase controls: [-29,-37,-24,-61,-82,-53] -> [-33,-43,-26,-72,-100,-61].
- Upper-arm swing amplitude: 24 -> 36; rest-pose inward adjustment stays 19.
- Elbow flexion: 72 +/-8 -> 80 +/-10.
- Hips yaw amplitude: 3 -> 4; chest counter-yaw 6 -> 8; head counter-yaw 3 -> 4.
- Sprint foot pitch controls: [-7,0,-92,-80,-18,-7]. The current Run generator has five pitch values because its expression -85 -21 evaluates to -106. The saved Run action was inspected directly and preserved, not regenerated. Sprint uses six explicit contact/load/extension/recovery controls. At the rendered push-off phases, measured ankle plantarflexion increases from 38.815 to 41.953 degrees, keeping the approved direction without an excessive increase.
- Phase warp v=u+0.04*sin(2*pi*u) advances push-off from 33.3% to approximately 29.5% of each limb cycle. The opposite side uses exactly half a cycle offset.
- Cadence increases 20%; measured ankle fore/aft excursion 0.79039 -> 0.92847 m (+17.5%); peak knee relative to hip rises 5.53 cm; hips bob 0.10367 -> 0.14257 m.

The generator corrects Hips Y for between-frame sole dips, based on eight samples per interval. This adds up to about 14 mm of local clearance and remains mirrored. It retains linear keys compatible with the existing export/runtime workflow. Root remains identity; all translation is vertical on Hips.

## Validation and review

Exact reopened endpoint mesh match, exact mirrored half-cycle limb transforms, fixed Root, unchanged geometry/weights/hierarchy, unchanged Jog and Run curves. Helmet stays rigid to Head; shoulder-pad relative vertex error is below 0.000001 m. Keyed and quarter-frame sole checks find no penetration after correction. Export contains one Sprint clip lasting 0.666667 seconds with unique node/property channels. No other gameplay action added. All seven requested preview views were inspected without major clipping or equipment collapse; existing segmented knee/ankle transitions remain conspicuous at compact recovery.

No rig/weight corrections or C# edits were made by this task. Hash audit preserved Jog/Run assets and generators; Tests/OpponentTests.cs changed concurrently and was left untouched. The validation report records that exception instead of claiming all workspace contents stayed unchanged.

Before opponent integration, review the compact knee/ankle seams, brief sole clearance from interpolation compensation, and ground-speed matching at normal gameplay distance. In-game foot skating, turning and blending are not validated by static Blender previews. Sprint_loop.mp4 is one seamless cycle for repeat playback.

## Preview files in Assets/Models/SprintPreviews

- Sprint_LeftContact_Front.png
- Sprint_LeftPassing_Side.png
- Sprint_LeftPushOff_Side.png
- Sprint_RightContact_Front.png
- Sprint_RightPassing_FrontThreeQuarter.png
- Sprint_RightPushOff_FrontThreeQuarter.png
- Sprint_Stride_FrontThreeQuarter.png
- Sprint_loop.mp4
- validation.json
- asset_validation.json
