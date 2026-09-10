Run from the repository root:

```text
dotnet build -c Release
dotnet test Tests/Controls.Tests.csproj -c Release
```

The tests open hidden Raylib windows and require a working desktop/OpenGL context.
They replay native Raylib keyboard, mouse and gamepad automation events through the
released Input/Menus packages. Reflection is limited to inspecting private game/menu
state and invoking the end-state update; production code uses public package APIs.

Coverage includes mouse normalization/noise, shared-direction rebinding and mouse-axis
capture, auto-forward assist/speed, juke/spin timing, head-fake cancellation, controller
neutral cancellation, Controls names/descriptions, native drawing/scrolling/navigation,
pause/back bindings and the 2.5-second end-state restart.

Manual follow-up:
- Visually inspect all three Controls columns and scrolling at your usual resolution.
- Rebind both device columns, return to play and verify the selected bindings.
- Check cursor capture/release on start, pause, resume and focus changes.
- Tune MouseGestureSensitivity only if needed for your mouse/DPI/frame rate.
- Check actual controller RT, B and both sticks, including back-to-side spins and head fakes.
- Play through touchdown and game-over to confirm the complete visual flow.
