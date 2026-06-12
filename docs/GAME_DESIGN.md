# Game Design Spec (port reference)

This is the authoritative design reference for the simulation, ported from the
original web game so the original repo is not a build dependency. Constants are
copied verbatim from `lib/shared/constants.ts` and each game's source file.

All games run inside the shared simulation: **fixed 20 Hz tick (dt = 0.05 s)**,
**arena 1280 × 720 logical units**, top-down, **circle collision**.

---

## 1. Core constants

```
TICK_HZ = 20            TICK_MS = 50            dt = 0.05 s
ARENA_W = 1280          ARENA_H = 720
PLAYER_RADIUS = 26      PLAYER_SPEED = 240 units/s
ROOM_CODE_LEN = 4       MAX_PLAYERS = 8         MIN_TO_START = 2
BOT_FILL_TARGET = 6
INTRO_MS = 5400   GO_MS = 3200   RESULT_MS = 6000   SERIES_RESULT_MS = 30000
EMPTY_GRACE_MS = 30000  HEARTBEAT = 30000
```

### Marbles (currency ◍) & titles
```
survivePerRound = 50    roundWinBonus = 40    elimParticipation = 5
championBonus = 300      placementCurve = [200, 120, 80, 50, 30]   (1st..5th series)
Titles (by placement): "The Last Player Standing", "First Loser",
  "Bronze Is Just Shiny Last", "Mid-Tier Menace", "Cannon Fodder"
```

### Flow / phases
`lobby → intro → playing → roundResult → seriesResult`
- intro: Game Master reveal (5.4 s) then a 3·2·1·GO hold (3.2 s) where the board
  renders but input is frozen until `playStartsAt`.
- playing: game ticks until `IsDone()`.
- roundResult: 6 s elimination readout. seriesResult: final standings.

---

## 2. Shared gameplay systems

### Movement (`ArenaGame.MoveActor`)
- Input is a normalized `(dx, dy)` in −1..1. Normalize if magnitude > 1.
- Speed = base × powerup multipliers × size effect. Integrate over `dt`.
- Clamp to arena: `x ∈ [r, 1280−r]`, `y ∈ [r, 720−r]`, where `r = PLAYER_RADIUS × scale`.
- Facing = `atan2(dy, dx)` when moving; anim = `run` if speed-input > 0.05 else `idle`.

### Dash (shared, tuned per game)
- Burst 0.18 s at **3.1× speed**, cooldown **1.4 s** (Tag 2.0 s, Mingle/Chairs 1.8 s).
- Direction from movement input, else facing/aim. Brief `ghost` visual.

### Powerups (`Powerups/`)
8 shared kinds; spawned by a per-game `PowerupField` (cadence 2.2–3.2 s, max 4–6
on field, 55–70 % "good" bias, wall margin 120–150). Collect by walking/dashing in.

| Kind | Good? | Effect | Duration |
|---|---|---|---|
| ⚡ Zoomies (speed) | good | 1.6× move speed | 7 s |
| 🛡️ Bubble (shield) | good | blocks one hit/freeze/lava | until used |
| 🔻 Shrink (tiny) | good | 0.62× scale, +nimble, smaller hitbox | 9 s |
| 🔦 Lantern (vision) | good | +320 night-vision radius | 10 s |
| 🌀 Bamboozled (reverse) | bad | controls reversed | 5 s |
| 🐌 Molasses (slow) | bad | 0.5× speed | 6 s |
| 🎈 Embiggen (giant) | bad | 1.5× scale, 0.62× speed, big target | 8 s |
| 💫 Dizzy (dizzy) | bad | input steering wobbles (sine) | 5 s |

Boomerang-only extras: ✨ multishot (3 rangs, 10 s), 🪃 big-rang (2.1× hit radius,
10 s), 🧲 magnet (homing, 10 s).

### Death rules
- **Hardcore** — elimination is permanent for the whole series; dead players
  spectate (and may bet in the Dead Pool). Series ends when one player remains;
  the finale forces a single survivor.
- **Casual** — players respawn each round; win on points across all rounds.

### Night Mode (Hardcore extra)
Random rounds go dark: vision radius `NIGHT_BASE_VISION = 250`, +320 with Lantern.
Rendered as a circular fog-of-war on the client.

### Series pacing / Game Master (`GameRoom`)
- Avoid repeats: track recent + already-played games; draw unplayed first.
- Even-only games (`requiresEven`) skipped on odd headcount.
- Each game tagged cull strength (low/mid/high); early rounds avoid high cullers.
- `intensity ∈ 0..1` passed to each game scales difficulty/cull.
  - Casual: linear ramp ~0.18 → 0.85.
  - Hardcore: solved per round to funnel the field toward ~3 by the finale
    (`FINALE_FIELD_TARGET = 3`, `CULL_COEFF = 0.5`), clamped 0.12..0.9.
- Finale is always last and sets `forceSingleSurvivor`.

### Ranking
Per round: survivors first (by placement), then eliminated in reverse
elimination order (last out ranks highest among the dead); each entry may carry a
note ("Caught moving!", "Frozen at buzzer", …). Series standings: placement 1 =
champion, with total marbles, rounds survived (tiebreak), and a title.

---

## 3. The 16 minigames

> Headline 12 first, then the 4 extras. Each lists: rules, win/lose, controls,
> key constants, and bot AI. Source file names are the original TS classes.

### 3.1 🚦 Red Light, Green Light — `RedLightGreenLight`
- Horizontal race, `START_X = 90` → `FINISH_X = 1190`. `RACE_SPEED = 150`.
- Green window 1.6–3.6 s, red window 1.2–2.9 s, alternating.
- On red after a `GRACE = 0.38 s`, moving faster than `MOVE_EPS = 12` units/s →
  eliminated. Time limit 70 s.
- Win: reach finish (ranked by crossing order); eliminated ranked by distance.
- Controls: move forward (W/↑), strafe lanes (A/D); freeze on red.
- Bot AI: reaction delay 0.12–0.62 s; 25 % "reckless" creep into red.

### 3.2 ❄️ Freeze Tag — `Tag` (requiresEven)
- Two teams: BLUE freezers ("it") vs PINK runners. Round 34 s, speed 175.
- Freeze on contact `FREEZE_R = 48`, freeze cooldown 0.4 s; teammates thaw frozen
  PINK on contact (radius 50). Deep-freeze window in the final 3.5–8 s disables thaw.
- At buzzer: frozen PINK eliminated; BLUE who caught nobody eliminated. Never wipe
  the whole field. Dash 2.6×, cd 2.0 s.
- Bot AI: BLUE hunt nearest unfrozen PINK and fan apart; PINK flee nearest BLUE,
  weave perpendicular, dart to thaw when safe.

### 3.3 🫂 Mingle — `Mingle`
- Central spinning platform (x640 y360 r112) + 6 ring rooms (ring radius 252,
  room radius 84). Phases: Wander 4.5 s → Mingle (call N = 2/3/4, 4.5–7 s) →
  Flash 2.0 s. 2–4 rounds by intensity.
- Eliminate: wrong room size, or still on platform when N is called. Win: be in a
  room holding exactly N. Dash cd 1.8 s.
- Bot AI: mill near center during wander; pick the room that best reaches exactly N.

### 3.4 🪟 Glass Stepping Stones — `GlassBridge`
- One safe side per row (0/1), hidden. Cross in line order; active player guesses the
  frontier row. Correct → advance & reveal; wrong → eliminated (last player gets a
  lucky catch). 4–12 rows by intensity × headcount. Turn 6 s (auto-guess on
  timeout), resolve anim 1.1 s.
- Win: cross all rows. Controls: ← / → (LEFT/RIGHT). Bot AI: 50/50 on unknown rows,
  walk known rows instantly (think 0.6–2.1 s).

### 3.5 🪢 Tug of War — `TugOfWar` (requiresEven)
- Two teams, one rope. Mash to pull; win at rope = ±1.0. Human impulse 1.25/tap,
  bot 1.0/tap; team force ÷ √(team size); rope decays 0.8×/tick, clamp ±1.4.
  Time 30 s; tap cap 14/s. Bot AI: spam 5–7 taps/s.
- Controls: Space/Click mash.

### 3.6 ✊ RPS Minus One — `RpsMinusOne` (requiresEven)
- 1v1 bracket. Pick TWO throws, DROP one, play your kept throw vs opponent's.
  Ties → no elimination, sudden-death rematch. Losers out; winners advance. Finale
  can force a full single-elim bracket to one survivor. Phases: pick 4.5 s, drop
  3.0 s, resolve 2.0 s; timeout forfeits (50/50, forfeiter loses).
- Bot AI: random two throws; drop to maximize expected value vs opponent's pair.

### 3.7 🤸 Killer Jump Rope — `JumpRope`
- Rope sweeps a bridge of planks; jump once per swing to advance a plank. Period
  starts 1.7 s, ×0.945 each swing, min 0.62 s. `JUMP_DUR = 460 ms`. 1–2 grace
  swings. Bridge 8–12 planks; max swings 8–30 (by intensity). Win: cross all
  planks (ranked by order). Finale-capable.
- Controls: Space/Click/Tap jump. Bot AI: jump when time-to-ground ≤ lead
  (0.2–0.26 s) + timing error (0.02–0.09 s, scaled by period).

### 3.8 🪃 Boomerang Brawl — `Boomerang`
- Free-for-all. Throw boomerang: speed 540, life 2.4 s, returns after 42 % of life,
  curves ±1; base hit radius 15 (2× with big-rang); catch radius 36; wall bounce.
  Dash i-frames 0.26 s; shield absorbs one hit. Survive to 1 (or forced 1 in finale)
  after `MIN_PLAY = 12 s`, or `TIME_LIMIT = 50 s`. Pickups every 3.2–5.2 s (max 5,
  all good). Powerups incl. speed/big-rang/multishot/shield/tiny/magnet.
- Controls: move + aim (mouse) + throw (click/space) + dash (shift).
- Bot AI: dodge incoming rangs (intercept calc), hunt nearest, throw/dash
  opportunistically, roam otherwise.

### 3.9 🤾 Dodgeball — `Dodgeball` (requiresEven)
- Two teams split by centerline. Grab a ball, hurl across (`BALL_SPEED = 640`,
  life 1.7 s, hit radius 15). Hit (no shield, not dashing) → out. `n = max(3,
  ⌈team⌉)` balls on the centerline; shield blocks one hit; teams clipped to their
  half ± `DIV_PAD = 8`. Time 45 s; buzzer survivors live. Powerups every 3 s.
- Controls: move + aim + throw + dash. Bot AI: dodge, fetch when empty-handed,
  throw at enemy cluster.

### 3.10 🪑 Musical Chairs — `MusicalChairs`
- Chairs appear only when music stops. Phases: Music 3.5–6.0 s (keep moving —
  still > 1.4 s out; `STILL_SPEED = 40`; DJ throws 0.45 s fake-out "STOP!"s, cd
  1.1 s) → Scramble 4 s (chairs scatter, one seat each) → Eval (standing players out,
  survivors reset to center). Chairs = aliveCount − 1 each round; 1–3 rounds; stop
  at ≤2 alive. Dash cd 1.8 s.
- Bot AI: orbit center during music; pick nearest free chair on scramble (reaction
  by skill).

### 3.11 🎁 Secret Santa Sabotage — `PresentSwap`
- Hidden role. `k` givers chosen (≈ alive·0.25·(0.6+intensity), ≤ ⌊alive/2⌋).
  Gift phase 8 s (dark): each giver picks one receiver from a slate of ≤4. Guess
  phase 11 s: each receiver guesses their giver — correct → giver out, wrong →
  receiver out. Reveal 4.2 s. 1–2 rounds. Gifts travel via per-player `secret`
  (never broadcast). Collision: same receiver → first wins, second reassigned.
- Controls: dark — tap player to gift; lit — tap suspect to guess.
- Bot AI: random receiver; random suspect.

### 3.12 🌋 King of the Lava Islands — `KingOfTheHill` (FINALE)
- Floor is lava (burn after `BURN_GRACE = 0.95 s`). Islands rise → hold → sink
  (`SINK_RATE = 45` units/s); small radius 56–150, final 32. Sudden death when ≤2
  alive or after `OPENING_GRACE = 14 s` → collapse to one shrinking island. Shove
  (click/space): cone 66 units, ±71° arc, knockback `SHOVE_IMPULSE = 380`, cd
  0.6 s. Powerups on islands (55 % good). Win: last player not burning.
- Controls: move + aim + shove + dash.

### Extras (in code, secondary rotation)
- 🦗 **PropHunt** — hide & seek; seeker has a blade, hiders nestle by same-kind decoys.
- 🎈 **KeepyUppy** — juggle a balloon under you; spike rivals' balloons to pop them.
- 🎲 **ChutesAndLadders** — board race; roll to advance, snakes/ladders.
- 🎵 **SimonSays** — repeat the lengthening color/tone sequence.

---

## 4. Characters, arenas, audio (client-side, real assets in Unity)

- **16 player characters** (food + critters: avocado, fox, wizard, sushi, koala…),
  originally drawn procedurally; in Unity these become 3D player models with
  squash/stretch anims (idle/run/cheer/dead/fall), a number bib, and effect
  overlays (shield bubble, ghost, flash, "it" aura, "you" marker). Unlock fancier
  ones with Marbles. Accessory slots: head/eyes/neck/ear (≤1 each).
- **6 arenas**: Sakura Courtyard, Neon Sewer Disco, Candy Wasteland, Toxic Lab,
  Sunset Tide Pools, Haunted Playground — each a ground/accent/wall palette plus
  animated props (petals, bubbles, stars, candy, goo, palms, ghosts).
- **Audio**: originally procedural Web Audio (SFX + arpeggio music + TTS Game
  Master). In Unity: real CC0 SFX + generated bfxr cues + a simple music loop, on
  Master/SFX/Music/Voice mixer buses, with **always-available subtitles** for the
  Game Master lines. See [ASSET_SOURCES.md](ASSET_SOURCES.md).
