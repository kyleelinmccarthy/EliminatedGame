# Asset sources & licenses

Every art/audio/font asset shipped in the game is listed here with its source and
license. **Rule: nothing ships unless its license is recorded in this file.**
Attribution required by a license is reproduced here *and* in the in-game Credits.

## Approved sources

| Source | URL | Typical license | Notes |
|---|---|---|---|
| OpenGameArt | https://opengameart.org/ | CC0 / CC-BY / GPL/OGA-BY (varies per asset) | **Prefer CC0.** If CC-BY, record author + link below. Avoid GPL for a proprietary game unless the asset is clearly separable. |
| Blend Swap | https://blendswap.com/ | CC0 / CC-BY / CC-BY-SA (varies) | Prefer CC0. Check each model's license tab. |
| Blender-models.com | https://www.blender-models.com/ | varies | Verify per model. |
| Unity Asset Store | https://assetstore.unity.com/ | Asset Store EULA (commercial) | **Cannot be auto-downloaded in CI/dev box.** Purchased assets are imported manually by the dev; record the order/asset name. |
| TurboSquid | https://www.turbosquid.com/ | TurboSquid licenses (Std/Editorial) | Use **Standard** license for in-game use; not Editorial. Paid, imported manually. |
| Freesound | https://www.freesound.org/ | CC0 / CC-BY / CC-Sampling+ (varies) | Prefer CC0. Record author for CC-BY. |
| GameSounds.xyz | https://gamesounds.xyz/ | CC0 / public-domain / CC-BY (varies) | Good for CC0 music + SFX. |
| bfxr | https://www.bfxr.net/ | Generated (you own the output) | Procedurally-generated retro SFX; exported WAVs are ours. See `tools/`. |
| GameMaster Audio | https://www.gamemasteraudio.com/ | Commercial pro packs | Paid, imported manually; record pack name. |

## License compatibility policy (proprietary game)

- ✅ **CC0 / Public Domain** — always fine, no attribution required (we still credit).
- ✅ **CC-BY** — fine; **must** attribute (author + title + link + license) here and in Credits.
- ⚠️ **CC-BY-SA** — copyleft on the asset; acceptable as a standalone media file
  but never merge/derive into other assets we want to keep proprietary. Prefer to avoid.
- ⚠️ **OFL** (fonts) — fine for fonts; keep the OFL.txt alongside the font; don't sell the font itself.
- ❌ **GPL** — avoid for shipped assets in a proprietary game.
- ✅ **Commercial EULA (Asset Store / TurboSquid Standard / GameMaster)** — fine; keep proof of license; don't redistribute source assets.

## Shipped assets (ledger)

> Filled in as assets are added. One row per asset.

| Asset (in-repo path) | Source | Author | License | URL |
|---|---|---|---|---|
| `Resources/Audio/oga/*.ogg` (14 SFX) | OpenGameArt | rubberduck | CC0 1.0 | https://opengameart.org/content/100-cc0-sfx |
| `Resources/Models/kenney/character-*.obj` (+ MTL + 1024² textures) | OpenGameArt / Kenney | Kenney (www.kenney.nl) | CC0 1.0 | https://opengameart.org/content/blocky-characters |
| `Resources/Audio/oga/casual_8bit.wav` (music loop) | OpenGameArt | Kat | **CC-BY 4.0** (attribution required — in-game Credits) | https://opengameart.org/content/casual-classic-loop-8-bit |

## Generated 3D model (shipped — our own output, no third-party license)

A blob character mesh is produced by **`tools/ModelGen`** as a Wavefront OBJ
(`unity/EliminatedGame/Assets/Eliminated/Resources/Models/blob.obj`, Git LFS;
825 verts / 1536 faces) and used by `BlobView` in place of the primitive sphere
(falls back to the sphere if absent). Regenerate:

```bash
dotnet run --project tools/ModelGen -- unity/EliminatedGame/Assets/Eliminated/Resources/Models
```

Our own procedural geometry (no license). Richer character models + animations
are sourced from the approved sites (Blendswap/TurboSquid/Asset Store) in Phase 7
and recorded in the ledger above.

## Generated arena art (shipped — our own output, no third-party license)

6 themed arena floor textures (256×256 32-bit TGA) are produced by **`tools/ArtGen`**
into `unity/EliminatedGame/Assets/Eliminated/Resources/Art/` (Git LFS) and applied
by `ArenaView` (a random theme per round, tiled). Themes/palettes match
[GAME_DESIGN.md](GAME_DESIGN.md): courtyard, neon, candy, toxic, beach, haunt.
Regenerate with:

```bash
dotnet run --project tools/ArtGen -- unity/EliminatedGame/Assets/Eliminated/Resources/Art
```

These are our own generated output (no third-party license). Real 3D blob models
and richer arena props are sourced from the approved CC0 sites in Phase 7 and get
rows in the ledger above.

## Generated SFX + music (shipped — our own output, no third-party license)

18 real 16-bit PCM WAVs are produced by **`tools/SfxGen`** (bfxr-style procedural
synthesis) and live under
`unity/EliminatedGame/Assets/Eliminated/Resources/Audio/` (Git LFS), loaded at
runtime by `AudioService`. Regenerate with:

```bash
dotnet run --project tools/SfxGen -- unity/EliminatedGame/Assets/Eliminated/Resources/Audio
```

Clips: `blip, click, good, bad, whoosh, throw, catch, explode, beep, alarm,
chime, pickup, death, shatter, jump, drum, win` + a 4 s `music` loop. These are
our own generated output and carry no third-party license (see
`Resources/Audio/SFX_MANIFEST.md`). Phase 7 may layer in curated CC0 sets from
Freesound/GameSounds.xyz — those get rows in the ledger above.
