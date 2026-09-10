# Run validation asset

Generate with create_player_run_animation.py through Blender MCP, then validate_player_run.py. Input is the approved lowpoly_human_jog.blend. Output lowpoly_human_run.blend retains Jog and all Pose_* actions unchanged, with Run active and fake-user retained. The separate lowpoly_human_run_validation.glb contains only Run including equipment channels; +Y up / -Z forward is preserved with export_yup=False.

30 fps; 24 intervals, keys 1..25, playback 1..24, duration 0.80 seconds. The duplicate closing key is excluded from the MP4. Same cameras, lighting, 640x720 resolution and rendering settings as Jog.

Compared with Jog: cadence +16.7%; torso lean 9 vs 5 degrees; hip curve reaches 36 vs 26 degrees, rear extension -27 vs -20; knee flexion reaches 82 vs 65; arm swing amplitude 24 vs 16; elbow flexion 72 +/-8 vs 66 +/-5; chest counter-rotation +/-6 vs +/-4 degrees. Foot pitch retains the approved sign, settles at neutral and reaches -27 vs -20 degrees during propulsion before recovering. Measured ankle fore/aft excursion 0.790 vs 0.647 m; hips bob 0.0467 vs 0.0408 m; highest knee relative to hip improves by 0.037 m.

Validation: exact endpoint mesh match and mirrored half-cycle limb transforms. Root identity throughout, Hips vertical translation only. Geometry, weights, hierarchy and Jog curves match source. Helmet attachment rigid; pads stay rigid relative to chest within 0.000001 m. Keyed soles stay at ground within floating-point tolerance. Quarter-frame interpolation has a maximum 1.42 mm sole dip, below visible penetration at preview scale. No Sprint. Existing C# and Jog file hashes unchanged; pre-existing workspace changes were left intact.

All seven requested PNG views were inspected: no major clipping or equipment collapse visible. The existing segmented knee/ankle construction is more noticeable in stronger recovery poses. Review these joints and the MP4 cadence before Sprint. Support feet use Jog's grounded treadmill approach; final world-space skating depends on matching gameplay speed to the clip, and is not verified in-game here. No rig or weight corrections made or required for these poses.

Assets/Models/RunPreviews contains Run_LeftContact_Front.png, Run_LeftPassing_Side.png, Run_LeftPushOff_Side.png, Run_RightContact_Front.png, Run_RightPassing_FrontThreeQuarter.png, Run_RightPushOff_FrontThreeQuarter.png, Run_Stride_FrontThreeQuarter.png, Run_loop.mp4, validation.json, asset_validation.json.
