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
| _(none yet — placeholders/primitives in use)_ | — | — | — | — |

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
