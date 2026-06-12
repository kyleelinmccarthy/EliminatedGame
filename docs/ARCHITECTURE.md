# Architecture

## The core idea

The original web game is **server-authoritative**: a Node WebSocket server runs
the entire game simulation at 20 Hz and broadcasts full snapshots; browsers only
send input and draw what they're told. We keep that shape, because it gives us
one simulation that serves every play mode and is trivially testable.

```
            ┌──────────────────────────────────────────────┐
            │   Eliminated.Sim  (pure C#, no UnityEngine)    │
            │   fixed 20 Hz tick · seedable RNG · snapshots  │
            │   RoomManager → GameRoom → IMinigame ×16       │
            └──────────────────────────────────────────────┘
                 ▲ input                       │ snapshots
                 │                             ▼
   ┌─────────────┴───────────┐   ┌─────────────────────────────┐
   │ Headless test harness    │   │ Unity client (SimBridge)    │
   │ xUnit · dotnet test       │   │ render · input · audio · UI │
   │ (CI, this dev box)        │   │ net transport · save · Steam│
   └──────────────────────────┘   └─────────────────────────────┘
```

`Eliminated.Sim` has **no dependency on UnityEngine**. It defines its own
`Vec2`, math, and RNG. That single property is what lets us:

1. **Unit-test all gameplay headlessly** with the .NET SDK (`dotnet test`) — no
   Unity, no graphics, fast red/green/refactor loops (TDD).
2. **Reuse the identical simulation** for solo, local co-op, the online listen
   server, and a future dedicated server. The only thing that changes between
   modes is *how input gets in and how snapshots get out* — the transport.

## Single source of truth for the sim

The simulation source lives **once**, as an embedded Unity package:

```
unity/EliminatedGame/Packages/com.eliminated.sim/Runtime/**/*.cs
```

- Unity compiles it natively via `Eliminated.Sim.asmdef`
  (`noEngineReferences: true`, `autoReferenced: true`) — so it is plain C# with
  no engine types and is referenced automatically by gameplay assemblies.
- The headless test project `sim/Eliminated.Sim.Tests/Eliminated.Sim.Tests.csproj`
  **`<Compile Include>`s those same files** and builds them with `net8.0` to run
  xUnit. No DLL copying, no drift — one file set, two compilers.

Because both compilers see the same source, the sim must stay within the
intersection of **netstandard2.1** (Unity's profile) and **net8.0** APIs, and
must never reference `UnityEngine`.

## Layers (Unity client)

| Folder (`Assets/Eliminated/Scripts/`) | Responsibility |
|---|---|
| `App` | Boot, scene/flow director, game-mode setup (solo/coop/online) |
| `SimBridge` | Owns a `SimHost` (in-process sim) or consumes networked snapshots; maps Unity input → `GameInput`; exposes latest snapshot to the view |
| `Net` | Unity Relay + Lobby + Auth; `NetSession` transport (input RPC up, snapshot down); host/client lifecycle |
| `View` | Top-down ortho camera; pooled `PlayerView`s bound to actor ids with interpolation; arena prefabs; per-game view modules; FX/screen-shake |
| `Input` | Input System actions, control schemes, device pairing (local co-op), touch controls, rebinding |
| `UI` | UI Toolkit screens: menu, shop, host/join, lobby, HUD, overlays, settings |
| `Audio` | AudioMixer buses, SFX/music/voice, subtitles |
| `Save` | Local JSON profile/settings; Steam Cloud / UGS Cloud Save |
| `Platform` | Steamworks (achievements, cloud, leaderboards, invites, Steam Input), UGS init |
| `Accessibility` | Colorblind palettes, subtitles, reduce flash/shake, text scaling |
| `Localization` | Unity Localization string tables, locale selection |

## Determinism & the tick

- Fixed timestep `dt = 1/20 s` (0.05). The sim never reads wall-clock for
  gameplay; all timers advance by `dt`. This makes runs reproducible from a seed
  (essential for tests and future replays/netcode reconciliation).
- The Unity view renders at display rate and **interpolates** actor positions
  between the last two snapshots (renders ~1 snapshot behind), so 20 Hz
  simulation looks smooth at 60–144 fps.

## Why not let Netcode-for-GameObjects own the entities?

NGO replicates `NetworkObject`s and `NetworkVariable`s. Our authority is a
plain-C# simulation, not a tree of networked GameObjects. So we use **NGO +
Unity Transport + Relay purely as a message pipe** (a single `NetSession`
behaviour: `SubmitInput` ServerRpc up, snapshot bytes down). This keeps the sim
the sole source of truth and avoids fighting the replication system. See
[NETCODE.md](NETCODE.md).

## Mapping from the original (TypeScript) source

| Original (`/eliminated`) | Here (`Eliminated.Sim`) |
|---|---|
| `lib/shared/constants.ts` | `Core/Constants.cs` |
| `lib/shared/types.ts`, `protocol.ts` | `Model/*.cs` (`Actor`, `Snapshot`, `GameInput`, `RoomConfig`…) |
| `lib/shared/powerups.ts` | `Powerups/*.cs` |
| `lib/server/games/Minigame.ts` | `Games/ArenaGame.cs`, `Games/IMinigame.cs` |
| `lib/server/games/*.ts` | `Games/*.cs` |
| `lib/server/GameRoom.ts`, `RoomManager.ts` | `Room/GameRoom.cs`, `Room/RoomManager.cs` |
| `lib/server/db.ts` (marbles/leaderboard math) | `Economy/*.cs` + client `Save` |
| `scripts/smoke.mjs` | headless E2E test (`SeriesSmokeTests`) |

The detailed mechanics for every minigame are captured in
[GAME_DESIGN.md](GAME_DESIGN.md) so the original repository is **not** a build
dependency.
