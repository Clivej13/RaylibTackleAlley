# Jog baseline

Run `create_player_jog_animation.py` through Blender MCP, then `validate_player_jog.py`. Input: `Assets/Models/lowpoly_human_rigged.blend`. Outputs: `lowpoly_human_jog.blend`, `lowpoly_human_jog_validation.glb`, and `JogPreviews/`. The canonical rigged source and staged sources are not overwritten.

Jog lasts 28/30 = 0.933333 seconds. FPS 30; keys 1–29 with frame 29 equal to frame 1. Playback/render range 1–28 excludes the duplicate closing state. Each bone is sampled each frame with linear interpolation and cyclic extrapolation. The continuous authoring functions are periodic cubic curves. Major phases are marked on the Blender timeline. Right limbs use the same functions offset by half a cycle.

Root remains identity throughout, with zero world travel. Hips move only vertically, grounded from the lower sole. Forward remains -Z, up +Y. The torso leans 5 degrees forward, with mild counter-rotation; the head compensates. Arms rotate inward from the rest A-pose, swing opposite the legs, and maintain elbow flexion. Caps stay rigid to clavicles and plates to Chest. No shape, weight, parenting, or skeleton corrections were required.

The .blend retains the eight approved Pose_* snapshots alongside Jog. The validation GLB contains only Jog, including rigid equipment transform channels. Blender 3.3 names the merged export Animation; the generator changes only this GLB animation name to Jog. GLB axis conversion is disabled because this asset already uses the desired glTF +Y-up basis. No runtime playback or C# changes.

## Review

Rendered contact, passing, push-off and opposite-side poses were inspected from front, side and three-quarter views. The jog has a short stride, low knee drive, bent elbows, stable pads and helmet, and no major clipping in these inspected views. Existing segmented knee/ankle transitions remain visible, especially on the recovering leg. No new collapse or severe shoulder penetration was observed. Relaxed source hands retain their open shape.

Loop endpoint mesh error is exactly zero; half-cycle reflected limb transform error is zero. Root identity and rigid helmet attachment pass on all 29 keyed frames. Lowest sole is within floating-point tolerance of Y=0. Source/animated geometry, weights, parenting and hierarchy signatures match. GLB validation finds exactly one Jog clip lasting 0.933333 seconds with no duplicate node/property channels. Validation JSON files record the measurements.

This is a usable Jog baseline for review before authoring Run. Foot motion is an in-place treadmill cycle, not a world-space foot lock; final foot-slide assessment requires matching game travel speed to stride distance. That runtime work is intentionally deferred. Inspect the MP4 on repeat to judge the cadence and segmented joints before increasing stride or knee drive for Run.

## Previews

- Jog_loop.mp4 (one cycle; repeat playback)
- Jog_LeftContact_Front.png
- Jog_LeftPassing_Side.png
- Jog_LeftPushOff_Side.png
- Jog_RightContact_Front.png
- Jog_RightPassing_FrontThreeQuarter.png
- Jog_RightPushOff_FrontThreeQuarter.png
- Jog_Stride_FrontThreeQuarter.png
- validation.json and asset_validation.json

Existing C# / project / configuration files were hashed before and after generation: zero changed contents. No commit or push.
