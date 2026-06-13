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
| Sketchfab | https://sketchfab.com/ | CC0 / CC-BY / CC-BY-SA / store (varies per model) | Prefer CC0/CC-BY. For CC-BY, record author + title + link below and in Credits (use the model's "Copy Credits"). |
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
| `Resources/Audio/oga/casual_8bit.wav` (was music loop — **no longer used at runtime**: too upbeat, replaced by the procedural ominous loop. File retained; if re-enabled, restore the CC-BY attribution to the in-game Credits) | OpenGameArt | Kat | **CC-BY 4.0** | https://opengameart.org/content/casual-classic-loop-8-bit |
| `Resources/Chars/slime.prefab` (slime character) | Unity Asset Store | LAYERLAB | Asset Store EULA (free Extension Asset) | https://assetstore.unity.com/packages/2d/characters/2d-characters-casual-monsters-365606 |
| `Resources/Chars/{ninja,pirate,wizard,clown}.prefab` | Unity Asset Store | Jovial Games | Asset Store EULA (free Extension Asset) | https://assetstore.unity.com/packages/2d/characters/free-simple-2d-cute-characters-pack-9-characters-svg-prefabs-371120 |
| `Assets/2D Animal Character Pack/**` (imported, animals) | Unity Asset Store | MiMU STUDIO | Asset Store EULA (free Extension Asset) | https://assetstore.unity.com/packages/2d/characters/2d-animal-character-pack-83019 |
| `Resources/Models/doll.fbx` ("Squid Game Doll" — Simon Says / Red-Light caller) | Sketchfab | ihechiokoro123 | **CC-BY 4.0** | https://skfb.ly/pts9H |
| `Resources/Audio/music_creepy.wav` (background music — **default ship track**; 40s seamless loop) | Pixabay | Cyberwave Orchestra | **Pixabay Content License** — free for commercial use incl. games/apps, no attribution required (credited anyway). Do not redistribute as a standalone audio file. | https://pixabay.com/music/mystery-creepy-suspense-horror-background-music-251989/ |
| `Resources/Audio/music_accralate.wav` (background-music candidate; 40s seamless loop) | Uppbeat (also on incompetech) | Kevin MacLeod | Downloaded via **Uppbeat free** (code `ZK652EAXJPD28T7Z`) — ⚠️ Uppbeat free covers *video content* and **requires Uppbeat Premium for in-game/app use**. ✅ However the same track is **CC-BY 4.0 via incompetech** — usable in-game **with attribution** (credit "Kevin MacLeod / incompetech.com"). Ship under the incompetech CC-BY terms, not the Uppbeat-free code. | https://incompetech.com/music/royalty-free/ |
| `Resources/Audio/music_sinister.wav` (menu / lobby / regular-game loop — "Sinister Music Box"; **trimmed**: source's ~4s fade-out tail removed → 64.2s seamless loop, re-encoded to PCM WAV like the other loops. Pristine source mp3 kept at repo root.) | Pixabay | Universfield | **Pixabay Content License** — free for commercial use incl. games/apps, no attribution required (credited anyway). Do not redistribute as a standalone audio file. | https://pixabay.com/music/ (track `universfield-sinister-music-box-231723`, user `Universfield`) |
| `Resources/Audio/music_danube.mp3` (**Musical Chairs** theme — Blue Danube waltz remix) | Pixabay | Trygve Larsen (user "nesrality") | **Pixabay Content License** — free for commercial use incl. games/apps, no attribution required (credited anyway). Do not redistribute as a standalone audio file. Underlying composition (J. Strauss II, *An der schönen blauen Donau*) is public domain. | https://pixabay.com/music/ (track `nesrality-richard-strauss-the-blue-danube-classical-remix-harmonica-95342`) |
| `Resources/Audio/music_dev.mp3` (**DEV/EDITOR ONLY** — Squid "Pink Soldiers" remix; the editor lobby/menu rotates between this and `music_sinister`, gated by `Application.isEditor` in `HudUi.UpdateMusic`. **Delete before any public/commercial build** — it's under `Resources/`, so it would otherwise be packed in even though no build code path plays it.) | SoundCloud, "The NC Records" channel | Nisalo (remix) / Jung Jae-il (original) | ⚠️ **NOT shippable.** Remix of the copyrighted *Squid Game* OST "Pink Soldiers"; a reuploader's "Copyright Free" label does not license the underlying composition. Editor reference only. | https://soundcloud.com/the-nc-records/squid-game-pink-soldiers-nisalo-remix-copyright-free |

> **Asset Store EULA note:** these packs are imported into the dev's Unity project (E: clone) and are **not redistributable** — don't commit the source pack folders to a public repo. The `Resources/Chars/*.prefab` are thin re-saves that reference the imported pack sprites; they only resolve where the packs are installed. Roster→art mapping lives in `Scripts/View/CharacterArt.cs`.

## Generated 3D model (shipped — our own output, no third-party license)

A player character mesh is produced by **`tools/ModelGen`** as a Wavefront OBJ
(`unity/EliminatedGame/Assets/Eliminated/Resources/Models/player.obj`, Git LFS;
825 verts / 1536 faces) and used by `PlayerView` in place of the primitive sphere
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

These are our own generated output (no third-party license). Real 3D player models
and richer arena props are sourced from the approved CC0 sites in Phase 7 and get
rows in the ledger above.

## Generated character sprites (shipped — our own output, no third-party license)

The 28 roster characters with no Asset Store art — the food/fruit/veg gang
(`avo, egg, egg2, goldegg, berry, brocc, donut, pickle, tomato, pine, shroom,
sushi, nana, plum, orange, blueberry, carrot, dragonfruit, melon, onigiri,
ghostpepper, cosmic`), the leftover critters (`bunny, pig, mouse, hamster,
ghost`) and the frog wizard (`wizard` → `frogwizard`) — are produced by
**`tools/ArtGen/gen_chars.py`** (stdlib-only Python, SDF-rasterized) as 256×256
RGBA PNGs into `unity/EliminatedGame/Assets/Eliminated/Art/Chars/`, style-matched
to the imported cute packs (rounded body, darker same-hue outline, big dark eyes,
blush). In-Editor, `GeneratedCharPrefabBuilder` auto-assembles each PNG into a
single-sprite prefab under `Resources/Chars/` (same shape as the LayerLab slime),
and the roster mapping lives in `Scripts/View/CharacterArt.cs`. Regenerate with:

```bash
python3 tools/ArtGen/gen_chars.py
```

These are our own generated output and carry no third-party license. Unlike the
Asset Store packs they ship in the repo, so the prefabs resolve everywhere.

## Generated SFX (shipped — our own output, no third-party license)

17 real 16-bit PCM WAVs are produced by **`tools/SfxGen`** (bfxr-style procedural
synthesis) and live under
`unity/EliminatedGame/Assets/Eliminated/Resources/Audio/` (Git LFS), loaded at
runtime by `AudioService`. Regenerate with:

```bash
dotnet run --project tools/SfxGen -- unity/EliminatedGame/Assets/Eliminated/Resources/Audio
```

Clips: `blip, click, good, bad, whoosh, throw, catch, explode, beep, alarm,
chime, pickup, death, shatter, jump, drum, win`. These are our own generated
output and carry no third-party license (see `Resources/Audio/SFX_MANIFEST.md`).

## Game Master announcer voice (shipped — generated offline, no runtime dependency)

The web build's robotic Game Master (`lib/client/audio.ts` → browser
`speechSynthesis`) — a **male** announcer revealing each game and barking Simon
Says orders, a **female** voice calling eliminations — is reproduced for Unity,
which has no built-in speech synth. Instead of speaking at runtime we pre-render a
small fixed vocabulary **once** with **Piper** (neural TTS) and ship the 67 clips
under `Resources/Audio/voice/`. The shipped voices are **CC0 / public-domain**
models so they are safe for a commercial build: male announcer = **`norman`**
(public domain), female (eliminations) = **`ljspeech`** (public domain). At runtime
`Announcer` stitches them — e.g. `game_03` + `name_tugofwar` = "Game three. Tug of
war." — and `AudioService.Speak` plays them gaplessly on a dedicated voice pool.
Regenerate by pointing the env vars at a Piper binary + the two `.onnx` voices:

```bash
PIPER_BIN=… PIPER_MALE=…/norman.onnx PIPER_FEMALE=…/en_US-ljspeech-medium.onnx \
  dotnet run --project tools/VoiceGen -- unity/EliminatedGame/Assets/Eliminated/Resources/Audio/voice
```

(`espeak-ng` is the no-env fallback engine — robotic formant synth — when those
vars are unset.) The TTS engine is a **build-time tool only** — not a runtime or
Unity dependency. Speech-synth output is the user's own data, not a derivative of
the synthesizer; only ship CC0 / public-domain voice models (NOT the CC BY-NC ones
like `ryan`/`hfc`/`lessac`). See `Resources/Audio/voice/VOICE_MANIFEST.md`. The
finale reveal ("The final game. …")
fires online too — the server sends a mystery-safe `finalGame` flag in the room
message (see the finale-music note below).

Background music is **no longer generated** — it is now sourced loops (see the
ledger above). A single looping track plays at a time, chosen **per screen/phase**
by the HUD music director (`HudUi.UpdateMusic` → `AudioService.SetMusic`, which is a
dumb player):

| Screen / phase | Track |
|---|---|
| Main menu / lobby / regular rounds | `music_sinister` (Universfield, Pixabay) |
| Mingle & Musical Chairs | `music_danube` (Blue Danube remix, Pixabay) |
| The final game (last scheduled round / Hardcore overtime) | `music_creepy` (Cyberwave Orchestra, Pixabay) |
| Post-game results / champion screen | `music_accralate` (Kevin MacLeod, incompetech CC-BY) |

The finale is detected via `ISnapshotSource.IsFinalGame` (local play computes it from
`GameRoom.RoundIndex`/`TotalRounds`; online play reads a `finalGame` bool the server
puts in the room message — `GameRoom.IsFinalGame`, just a bool so Mystery mode never
leaks the hidden total-round count). In the **editor only**, the lobby/menu rotates
between `music_sinister` and the Squid "Pink Soldiers" loop (`music_dev.mp3`) via
`AudioService.SetMusicPlaylist`, gated by `Application.isEditor` — a real build uses the
fixed `music_sinister`. `music_dev.mp3` is unlicensed and **must be deleted before any
public build** (it lives under `Resources/`). `SfxGen.MusicLoop()` is retained as a
fallback but is intentionally not written, so re-running `SfxGen` never overwrites these tracks.
