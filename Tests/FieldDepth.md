# Field depth investigation — 2026-09-17

Status: the reported large moving triangular patches were not reproduced with the current working-tree assets. No production geometry or clipping changes have been made; there is not yet evidence for a specific fix.

## Actual loaded geometry

The native Raylib loader reports:
- football_field.glb: one mesh, two triangles, X -26.6665..26.6665, Y=0, Z -60..60.
- stadium.glb: 192 meshes, X -55..55, Y -0.24..25.000006, Z -90..90.
- Both model transforms are identity. Current configured dimensions match these horizontal bounds, so FootballField.FitBounds gives unit scale and the same centre translation for both assets.

Clipping every stadium triangle at/below Y=0.05 against the interior field rectangle finds no projected area underneath the field. The stadium already has the requested opening. Sideline runoff starts at X +/-26.6665; end-zone runoff starts at Z +/-60. These are separate perimeter strips, not a full floor hidden under the turf.

The field has no separate end-zone, boundary-paint, hash or yard-line geometry: these are in its texture. FootballField.Draw draws each model once; TackleAlleyGame.Draw calls it once. The equipment-only asset is not loaded alongside the stadium assembly.

The gameplay danger overlay is separate geometry: full sideline strips at Y=0.025, glow bands at Y=0.035 and boundary lines at Y=0.045. These layers deserve attention if a matching reproduction becomes available, especially on a lower-precision depth buffer.

## Isolation experiments

FieldDepthTests opens a hidden native window and reads its framebuffer. It tests 18 viewpoints (centre and both sidelines, three longitudinal positions, facing both ends), reversing field/stadium and stadium mesh submission order to expose depth ties.

Variants include:
- Normal field at Y=0.
- Entire field temporarily raised to Y=0.1.
- Opaque diagnostic strip/glow colours at the gameplay overlay heights.
- Stadium floor/apron meshes (maximum Y <= 0.05) omitted.

It checks both near=0.05 and near=0.1 with far=4000. The opening check and all render probes pass. The initial central views had 10, 5 and 0 order-dependent pixels with the native clip range; raising the entire field left those counts unchanged. These are small stadium details, not evidence of a broad field/floor overlap. The expanded sweep also found no large patches. This is a sampled depth-order probe, not proof that all temporal rendering artefacts are absent; opaque diagnostic overlays deliberately exclude expected alpha-blending order differences.

An earlier offscreen render-texture probe gave the same central-view counts. The final check uses the window framebuffer to avoid relying on an offscreen depth attachment.

## Camera and depth setup

Native clip getters return near=0.05 and far=4000 (80,000:1). The application does not override these defaults. The stadium footprint is only 110 x 180, so a shorter far distance and larger near distance could improve precision. The test's 0.1 near plane reduced the small central-view differences, but did not establish a cause for the reported large patches.

The application uses BeginMode3D/EndMode3D and ClearBackground, with no custom framebuffer, depth-mask override, depth-test disable or custom projection in game code. The actual window depth-bit count was not established. No shader, timing, VSync or rendering-loop changes were made.

## Run

```text
dotnet test Tests/Controls.Tests.csproj -c Release --filter FullyQualifiedName~FieldDepthTests --logger "console;verbosity=detailed"
```

Requires desktop/OpenGL, like the existing native rendering tests. The test restores the clip planes and unloads its models/window. It does not modify assets.

## Remaining reproduction requirement

Confirm whether the recording used these current local assets or an older build. Both field and stadium GLBs were already modified relative to HEAD before this investigation; committed assets cannot be assumed to match the working tree or recording. A matching build/camera sequence is needed before claiming a flicker fix.
