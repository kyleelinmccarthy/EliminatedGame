# Roadmap

Vertical-slice-first. Each phase ends with a concrete, verifiable deliverable.
Headless phases are verifiable in CI / `dotnet test`; Unity phases are verified
by opening the project in Unity Hub.

| Phase | Deliverable | Verify |
|---|---|---|
| **0 — Foundation** | Repo, docs, .gitignore/LFS, Unity skeleton + packages, sim package skeleton, xUnit test project, CI | `dotnet test` green |
| **1 — Sim core + 3 games** | Core (Vec2/Rng/fixed tick), Model, `ArenaGame`, `GameRoom` series state machine, Economy + **Red Light Green Light**, **Tug of War**, **Boomerang Brawl** with bots — all TDD | `dotnet test` green |
| **2 — Unity vertical slice** | SimBridge runs sim in-process; Input System (KB/M + pad); 2.5D BlobView + 1 arena; HUD; menu→lobby→play→results loop; starter CC0 assets + SFX; settings | Plays solo vs bots in Unity |
| **3 — Breadth** | Remaining 13 minigames (TDD) + view modules + 6 arenas + 16 blobs/accessories + progression/unlocks + local leaderboard | `dotnet test`; play in Unity |
| **4 — Local co-op** | `PlayerInputManager` device pairing, shared-screen, bot-fill | Two pads, one screen |
| **5 — Online** | UGS Auth/Relay/Lobby + `NetSession` transport, host-by-code, input RPC + snapshot broadcast, reconnection, cross-play | Two clients, one room |
| **6 — Platforms & Steam** | Steamworks (achievements/cloud/leaderboards/invites/Input), Steam Deck verification, mobile touch + build settings | Deck + mobile builds |
| **7 — Polish & store** | Localization pass, accessibility audit, full audio/music, store assets, EULA/privacy, IARC/ESRB, release pipeline, E2E smoke | Release candidate |

## Current status (175 headless tests green)

- [x] Phase 0 — Foundation (`dotnet test` green, CI wired)
- [x] Phase 1 — Sim core + 3 games (incl. full-series E2E)
- [x] Phase 2 — Unity vertical slice (code-driven, solo vs bots, 3 games rendered,
      menu→play→results, settings, local save) — *open in Unity 6 to verify visually*
- [x] Phase 3 — Breadth: **ALL 16 minigames** (12 headline + 4 extras) with bots,
      the `GameRoom` series state machine, cosmetics economy (38 chars +
      accessories), an all-games smoke sweep, and the `Wire` netcode codec.
      *Remaining client work (per-game view modules, shop UI) folded into Phase 7.*
- [ ] Phase 4 — Local co-op — see [IMPLEMENTATION_GUIDE.md](IMPLEMENTATION_GUIDE.md)
- [ ] Phase 5 — Online (UGS Relay/Lobby + NGO; `Wire` codec ready)
- [ ] Phase 6 — Platforms & Steam (Facepunch.Steamworks)
- [ ] Phase 7 — Polish, accessibility, localization, store

The simulation is feature-complete and exhaustively tested headlessly; the
remaining phases are Unity-side integration, each scoped in
[IMPLEMENTATION_GUIDE.md](IMPLEMENTATION_GUIDE.md).

## Code principles

- **SOLID** — each minigame is a self-contained `IMinigame`; the room, economy,
  bots, and powerups are separate single-responsibility units behind interfaces.
- **DRY** — shared movement/collision/dash/powerup logic lives once in `ArenaGame`;
  constants live once in `Constants`.
- **KISS** — fixed-timestep deterministic sim, full snapshots (no premature delta
  compression), shared-screen local co-op (no split until needed).
- **TDD** — every sim system and minigame gets failing tests first, then code.
