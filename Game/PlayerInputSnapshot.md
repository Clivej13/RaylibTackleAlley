# Frame input and simulation

GameApplication updates the framework InputController once per rendered frame.
TackleAlleyGame.Update captures a readonly PlayerInputSnapshot before starting active-gameplay
substeps. Every substep receives the same value, including both world-space directional
axes, the separate Slow modifier, Sprint and normalized gesture direction/source.
ThirdPerson resolves these axes against the rendered camera yaw once before substeps;
fixed modes retain their world-space interpretation. The snapshot sent to simulation
always describes world-space intent. No input controller or
native device polling is performed by the snapshot-based BallCarrier.Update overload.

RightStickInput.ReadFrame retains existing keyboard/mouse/gamepad binding handling, mouse
normalization and device selection. It consumes IgnoreNextMouseDelta once during capture,
so cursor recapture suppresses the complete frame even when close contact requires many
substeps. Gesture timers and movement still advance per substep. Separate CameraLookInput
is captured once per frame from dedicated actions; GameplayCamera updates after simulation
and never interprets player movement input as camera rotation.

Standalone BallCarrier.Update(InputController, ...) remains compatible and treats each call
as a frame; callers performing substeps must use CaptureInput once and pass its result to the
snapshot overload. Outcome/recovery-only game updates need no gameplay input sample.

Native regression tests cover cursor suppression at 30/60 FPS near a defender and verify the
next frame still triggers a gesture. Snapshot tests mutate live controls between substeps and
confirm the captured movement remains unchanged. Existing controls/gesture tests remain active.
