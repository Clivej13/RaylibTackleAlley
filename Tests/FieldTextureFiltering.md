# Field texture filtering — 2026-09-17

## Loading and implementation

assets.json maps FootballField to Assets/Models/football_field.glb. GameApplication.Run requires it through RaylibGameFramework.Assets 0.1.4 and processes the loading queue after window creation. Raylib imports the embedded base-colour image into the model's Albedo material map. Runtime inspection found material 1, a 2048 x 4096 RGB texture; material 0 uses Raylib's shared white default texture.

The Assets package's public configuration/API has no filtering or mipmap option. Existing loading and ownership are retained. FootballField configures only its non-default Albedo textures during its existing one-time mesh initialization, immediately before the first field draw. No separate texture is loaded. The field normal map and unrelated textures are untouched.

Verified against installed Raylib-cs 8.0.0 using reflection and compilation: GenTextureMipmaps(ref Texture2D), SetTextureFilter, MaterialMapIndex.Albedo, TextureFilter.Trilinear and TextureFilter.Anisotropic16X. The native runtime reports raylib 6.0. Its SetTextureFilter anisotropic branch only sets anisotropy, so trilinear must be set first.

Before: one base level, GL_NEAREST minification and magnification, anisotropy 1.
After: 13 mip levels, GL_LINEAR_MIPMAP_LINEAR minification, GL_LINEAR magnification, anisotropy 16 on the test GPU.

Mipmap metadata is updated through a reference to the actual material map. Duplicate texture IDs reuse the updated metadata; the shared default texture is skipped. Existing mip chains are retained. OpenGL clamps the requested anisotropy to its supported maximum; Raylib leaves trilinear min/mag settings intact if anisotropy is unsupported.

References:
- https://github.com/raysan5/raylib/blob/6.0/src/rtextures.c
- https://github.com/raysan5/raylib/blob/6.0/src/rlgl.h
- https://github.com/KhronosGroup/OpenGL-Registry/blob/main/extensions/EXT/EXT_texture_filter_anisotropic.txt

## Validation

Release build and FieldTextureFilteringTests passed. Existing FieldDepthTests passed separately.
The native game-render harness ran the actual TackleAlleyGame with all assets, its normal gameplay camera, and 120 update/draw frames. Additional test-only cameras sampled both field ends at normal and shallow (1.2-yard) heights. Production camera configuration was not changed. This was automated game execution and screenshot inspection, not an interactive playthrough.

The Windows OpenGL test queries the actual texture state, checks preserved texture identity/dimensions, complete mip levels, repeat-draw stability, and unchanged unrelated texture states. The RTX 3060 reports maximum anisotropy 16. Trilinear-only state and renders were checked; hardware lacking anisotropy was not available.

Twelve comparison captures are generated under artifacts/field-filtering:
normal-near, shallow-near, normal-far, shallow-far, each with suffix:
- 0: original nearest/base-level sampling
- 1: trilinear alone
- 2: trilinear plus 16x anisotropy

Visual inspection: original distant hashes disappear unevenly and yard lines break up. Filtered lines are continuous, hashes remain readable much farther away, and near edges are smoother. Trilinear alone is noticeably softer; anisotropy preserves definition at shallow angles.

Small camera translations measured mean absolute frame-to-frame brightness change in two distant-field patches (0–255 scale):
| View | Original | Filtered |
| --- | ---: | ---: |
| Normal near | 0.936 | 0.249 |
| Shallow near | 1.877 | 0.263 |
| Normal far | 1.236 | 0.217 |
| Shallow far | 1.231 | 0.229 |

This is a local temporal-stability indicator including real image motion, not a comprehensive perceptual shimmer score.

1280 x 720, uncapped, GPU-synchronized full-scene drawing: original median 1.311/1.457 ms; filtered 1.371/1.373 ms across repeated batches. No obvious regression in this short test. Mipmaps add roughly one-third to this texture's GPU storage; generation happens once, not every frame.

Remaining very distant markings fade/merge as they become subpixel-sized, consistent with minification and screen resolution. The comparison does not establish an asset-resolution deficiency, and shows no evidence of field geometry/z-fighting. Stadium edge aliasing is outside this field-texture change.

No asset, geometry, UV, source PNG, MSAA, camera or field-dimension changes were made. Pre-existing working-tree changes were preserved. No commit or push.

Run:
```text
dotnet test Tests/Controls.Tests.csproj -c Release --filter FullyQualifiedName~FieldTextureFilteringTests --logger "console;verbosity=detailed"
dotnet test Tests/Controls.Tests.csproj -c Release --no-build --filter FullyQualifiedName~FieldDepthTests
```
