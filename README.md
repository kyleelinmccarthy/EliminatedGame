# ◖◗ ELIMINATED — Unity Edition

**A wholesome party game where everyone dies.** Childhood playground games meet
elimination-show stakes and boomerang-fu chaos. A sinister Game Master runs the
gauntlet; your blob runs out of luck. Get eliminated and your blob is neatly
boxed up with a little pink bow on top. The last blob standing keeps the
**Marbles** ◍ and a fistful of bragging rights that, legally, are worth nothing.

This repository is the **Unity 6 / C# remake** of the original web game
([`kyleelinmccarthy/eliminated`](https://github.com/kyleelinmccarthy/eliminated),
Next.js + WebSocket), rebuilt as a **shippable, installable game** for Steam and
beyond.

> **Status:** The authoritative simulation is **feature-complete** — all **16
> minigames** (12 headline + 4 extras), the full series state machine, bots,
> economy, cosmetics, and the netcode wire codec, behind **175 passing headless
> tests** (`dotnet test`). A code-driven Unity slice plays solo-vs-bots today.
> Remaining work is Unity-side integration (local co-op, online, Steam, full UI,
> localization), scoped in [docs/IMPLEMENTATION_GUIDE.md](docs/IMPLEMENTATION_GUIDE.md).
> See [docs/ROADMAP.md](docs/ROADMAP.md) for phase status.

---

## What we're building

| Area | Target |
|---|---|
| Platforms | **PC/Steam first**, then Steam Deck/handheld, mobile, cross-platform |
| Modes | Solo vs bots · Local co-op (shared-screen, bot-fill) · Online (host by code) |
| Input | Keyboard & mouse · Controller · Touch · **remappable** |
| Save | Local save · Steam Cloud (+ optional cross-platform cloud) |
| Accessibility | Colorblind palettes · Subtitles · Remappable controls · Reduce flash/shake |
| Localization | Unity Localization string tables (EN + scaffolded locales) |
| Business | One-time purchase, with future content/support |
| Rating (est.) | **ESRB E10+ / PEGI 7** (mild cartoon violence, "Users Interact" online) |
| Genre tags | survival · casual · party · funny · fighting |

The headline content is **16 minigames** (12 marquee + 4 extra), bots with
per-game AI, a Game Master that reveals a mystery sequence, Hardcore (permadeath)
and Casual (points) rules, Night Mode, powerups, 6 themed arenas, 16 blob
characters + accessories, persistent Marbles currency and a leaderboard.

---

## Architecture in one paragraph

All gameplay logic lives in **`sim/` — a pure C# authoritative simulation with
zero Unity dependencies**. It ticks a fixed 20 Hz and produces full snapshots;
clients only send input and render snapshots. That single library powers solo,
local co-op, the online listen-server host, and a future dedicated server — and
because it has no engine dependency it is **unit-tested headlessly** (real TDD,
`dotnet test`). The Unity project consumes that exact source as an embedded
package and adds rendering (2.5D top-down), input, audio, UI, netcode transport
(Unity Relay + Lobby), save, Steam, accessibility, and localization.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/NETCODE.md](docs/NETCODE.md).

---

## Repository layout

```
sim/                     Pure C# simulation tests + solution (TDD, headless)
unity/EliminatedGame/     Unity 6 (URP) project
  Packages/com.eliminated.sim/   ← canonical sim source (compiled by Unity AND dotnet)
  Assets/Eliminated/             ← client: rendering, input, UI, audio, net, save…
docs/                    ARCHITECTURE · GAME_DESIGN · NETCODE · ASSET_SOURCES · ROADMAP
tools/                   Asset-fetch + SFX generation scripts
server/                  (later) headless dedicated-server build
.github/workflows/       CI (dotnet build+test now; Unity build later)
```

---

## Build & test

### Simulation (headless, no Unity required)

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (8.0+):

```bash
cd sim
dotnet test          # runs the full simulation test suite (+ the networked server smoke)
```

### Headless authoritative server (online, no Unity required)

A dedicated-server deployment runs the simulation over WebSockets and is
buildable/runnable with the .NET SDK alone:

```bash
dotnet run --project server/Eliminated.Server -- 8080   # ws://localhost:8080/
```

The Unity client speaks the same protocol; online play can use Unity Relay
(host-based) or this dedicated server. An end-to-end test
(`server/Eliminated.Server.Tests`) drives a full series across two real
WebSocket clients.

### Unity client

Open `unity/EliminatedGame/` in **Unity Hub** with **Unity 6 LTS (6000.x)**.
The simulation compiles automatically as the embedded package
`com.eliminated.sim`. Press Play on `Assets/Eliminated/Scenes/Boot.unity`.

> This repo uses **Git LFS** for binary assets (models, textures, audio, fonts).
> Run `git lfs install` once after cloning.

---

## License

Proprietary — see [LICENSE](LICENSE). Third-party assets keep their own licenses,
tracked in [docs/ASSET_SOURCES.md](docs/ASSET_SOURCES.md).
