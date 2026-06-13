# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All sim/server development is headless .NET (no Unity required):

```bash
# Build + test the simulation (xUnit, 175+ tests)
dotnet build sim/Eliminated.sln --configuration Release
dotnet test sim/Eliminated.sln

# Run a single test (filter by class or method)
dotnet test sim/Eliminated.sln --filter "FullyQualifiedName~DodgeballTests"
dotnet test sim/Eliminated.sln --filter "FullyQualifiedName~DodgeballTests.Teams_split_left_and_right"

# Run the headless dedicated server (ws://localhost:8080/)
dotnet run --project server/Eliminated.Server -- 8080

# Build cross-platform self-contained server binaries into ./dist/
tools/publish-server.sh
```

Unity client: open `unity/EliminatedGame/` in Unity Hub (Unity 6 LTS, 6000.x). The project is code-driven — press Play with any scene open. Git LFS is required for binary assets (`git lfs install`).

`tools/VoiceGen` regenerates the announcer TTS voice bank (requires espeak-ng); generated clips live in `Assets/Eliminated/Resources/Audio/voice/`.

## Architecture

### Single source of truth: the pure-C# sim

The canonical simulation source lives exactly once, as an embedded Unity package:

```
unity/EliminatedGame/Packages/com.eliminated.sim/Runtime/**/*.cs
```

It is compiled two ways with **no duplication**:
1. **Unity** compiles it natively via `Eliminated.Sim.asmdef` (`noEngineReferences: true`).
2. **.NET SDK**: `sim/Eliminated.Sim/Eliminated.Sim.csproj` globs `<Compile Include>` over those same files for headless tests and the dedicated server.

Consequence: sim code must never reference `UnityEngine`. Anything in `com.eliminated.sim` must build under netstandard2.1 and pass `dotnet test`.

### Determinism rules

- Fixed timestep: 20 Hz (`Constants.Dt = 1/20s`). All gameplay timers advance by `dt` — never read wall-clock time.
- All randomness goes through `Rng` (seeded Mulberry32) from `GameContext`/`GameRoom` — never `System.Random` or `Guid`-based randomness. This enables reproducible tests, replays, and netcode.
- The Unity view interpolates actor positions between 20 Hz snapshots for smooth rendering.

### Sim structure (Packages/com.eliminated.sim/Runtime/)

- `Room/` — `RoomManager` → `GameRoom`: series state machine (Lobby → Intro → Playing → RoundResult → SeriesResult), bot auto-fill.
- `Games/` — 16 minigames implementing `IMinigame` (`Start()`, `OnInput()`, `Tick(dt)`, `IsDone`, `Result()`); most inherit `ArenaGame`. Per-game scratch state goes in `Actor.Data`.
- `Model/` — `Actor` (pos/vel/facing, input intent, alive, team, powerup timers), `Snapshot`, `GameInput`, `RoomConfig`.
- `Powerups/` — Boomerang-Fu-style mystery orbs: Blessings (permanent), Curses (timed), Chaos (instant). `PowerupField` spawns/ticks; `PowerupCatalog`/`PowerupEffects` define them.
- `Localization/Loc.cs` — code-embedded string tables (en, es, fr, de, pt-BR, ja, ko, zh-Hans) with English fallback; `{0}` format args. `LocTests` validates table completeness — adding a key requires all locales.
- `Net/Wire` — compact binary snapshot/input serialization shared with Unity NGO and the WebSocket server.
- `Economy/` — Marbles currency, cosmetics.

### Deployment modes (same sim, different transport)

1. **Solo / local co-op** — sim runs in-process via `Assets/Eliminated/Scripts/SimBridge/SimHost.cs`.
2. **Online listen server** — sim runs on the host client; NGO + Relay carries input up / snapshots down (`Scripts/Net/NetSession.cs`). Snapshots are per-client (secret info stripped). No client prediction.
3. **Dedicated server** — `server/Eliminated.Server` runs the sim over WebSockets; E2E tests in `server/Eliminated.Server.Tests`.

### Unity client layer (Assets/Eliminated/Scripts/)

View-only — consumes snapshots, submits input, never mutates sim state directly:
- `View/ArenaView.cs` builds the 2.5D stage and pools `PlayerView`s with interpolation.
- `Audio/AudioService.cs` — SFX pool, music, announcer voice pool (TTS clips from VoiceGen).
- `UI/HudUi.cs` — UI Toolkit HUD and game-loop direction.
- `Input/`, `Save/`, `Platform/` (Steamworks), `Accessibility/`, `Localization/`.

### Testing patterns (sim/Eliminated.Sim.Tests/)

```csharp
// Deterministic game test
var ctx = new GameContext { Rng = new Rng(seed), Actors = actors };
var g = new Dodgeball(ctx);
g.Start();
g.Tick(Constants.Dt);

// Drive room phases with a helper
RunUntil(room, () => room.Phase == RoomPhase.Playing, maxTicks: 400_000);
```

`AllGamesSmokeTests` runs every minigame to completion with bots — a new minigame must pass it. CI (`.github/workflows/ci.yml`) runs the sim test suite on push/PR.

## Documentation

`docs/` contains ARCHITECTURE.md, GAME_DESIGN.md (mechanics for all 16 games), NETCODE.md, IMPLEMENTATION_GUIDE.md, ROADMAP.md, UNITY_SETUP.md, and ASSET_SOURCES.md (third-party asset license tracking — new assets must be recorded there).
