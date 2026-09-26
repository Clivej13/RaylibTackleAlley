# Runtime player uniforms

All players still use `Assets/Models/football_player.glb`. Uniform selection changes
only the named `Uniform` material's albedo texture on an existing model instance.
UVs, draw calls, animation, skeletons and ragdolls are unchanged.

Place atlas PNGs under `Assets/Textures/Uniforms/` and register each in the existing
`assets.json` Assets array, for example:

```json
{ "Key": "TexansHome", "Type": "Texture", "Path": "Assets/Textures/Uniforms/texans_home.png" }
```

Select each team's uniform by asset key in `config.json`:

```json
"OffenseUniform": "OffenseUniform",
"DefenseUniform": "DefenseUniform"
```

At startup, offense is applied to the carrier and defense to all defenders.
The default assets are `Assets/Textures/Uniforms/offense_uniform.png` (navy
jersey, white numbers/pants, gold sleeve stripes) and `defense_uniform.png`
(white jersey, navy numbers/pants, red sleeve stripes). Model previews are in
`Assets/Textures/Uniforms/TeamPreviews/`.
Both textures stay required until shutdown and remain applied across run resets.
The project copies Assets to build and publish output.

To use new artwork, register its PNG as a Texture in `assets.json`, then set
either config value to that asset key and restart the game. Both teams may use
the same key. The original `PlayerUniform` sample remains available.
Do not use `uniform_template.png` directly: it includes labels and guides.
See `Assets/Textures/Uniforms/README.md` for painting instructions.

After the graphics context exists, require/load the texture using the same manager
that owns the player instances, initialize visuals as usual, and apply it:

```csharp
assets.RequireAsset("TexansHome");
while (!assets.ProcessNext()) { }
game.ApplyPlayerUniform(assets, "TexansHome");
// Independently select the defenders' uniform:
game.ApplyOpponentUniforms(assets, "TexansHome");
```

Call these methods again with another loaded texture key to swap at runtime.
Individual `BallCarrier` and `Opponent` objects expose
`ApplyUniform(assets, textureKey)`. For an existing FootballPlayer
`ModelInstance`, use `PlayerUniform.ApplyUniform(instance, assets, textureKey)`.
Call on the graphics thread, after visual initialization.

Player objects now generate an owned atlas copy with their profile's jersey number
on the Chest and Back panels. See [PlayerProfiles.md](PlayerProfiles.md) for layout,
configuration and texture disposal. The following borrowing rules apply to the
lower-level unnumbered PlayerUniform.ApplyUniform API and source team textures.

Textures are borrowed from AssetManager, which caches them by asset key.
Keep each texture required while any instance uses it. Once all users have
switched away, it can be released with `assets.ReleaseAsset(oldKey)` and the
normal `ProcessNext()` work loop. Otherwise keep the small selection loaded
until shutdown. Do not call `UnloadTexture` on these borrowed textures.
The existing `assets.UnloadAll()` before `CloseWindow()` releases model instances
and textures; switching never deletes a texture another player might still use.

raylib 6 does not preserve material names in its native Material struct.
PlayerUniform reads the shared GLB's JSON once, finds the exact name `Uniform`,
and accounts for raylib's prepended default material. It checks the loaded
material count and rejects missing/duplicate Uniform definitions instead of
guessing a jersey mesh or hard-coding a material number. Use the atlas layout
described in `Tools/Blender/player_uniform_atlas_notes.md`; no UV editing or
image flipping is needed.

Validation: `dotnet test Tests/Controls.Tests.csproj --filter FullyQualifiedName~UniformTests`
checks independent swaps, shared texture use, unchanged UVs/vertices/other maps,
failure without mutation, and asset cleanup (GPU deletion assertions on Windows).

Returner-specific atlas keys are configured by `returners.json` -> `Uniform` and
used for both preview and selection; `OffenseUniform` remains the fallback.
See [the five returner variants](../Assets/Textures/Uniforms/Returners/README.md)
for artwork, asset keys, Blender reproduction and visual review notes.
