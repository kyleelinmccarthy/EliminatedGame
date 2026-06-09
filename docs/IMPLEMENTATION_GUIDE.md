# Implementation Guide — Phases 4–7

The simulation (`sim/` → 175 headless tests) and a code-driven solo-vs-bots Unity
slice are complete. This guide turns the remaining phases into concrete, scoped
tasks. The recurring theme: **the authoritative `Eliminated.Sim.Room.GameRoom`
never changes** — each phase only changes how input reaches it and how snapshots
leave it. Everything below is Unity-side (open the project in Unity 6 to build).

---

## Phase 4 — Local co-op (shared screen)

The arena is one top-down view, so local co-op is **shared-screen** (no split
needed): several local humans, one `GameRoom`, bots fill the rest.

1. **Packages:** none new (Input System already present).
2. **Device pairing:** add a `LocalPlayerManager` using
   `UnityEngine.InputSystem.PlayerInputManager` (join on button-press). Each
   joined device → one sim player slot: `room.AddPlayer(new Player("local_"+i, …))`.
3. **Input routing:** generalize `LocalInputRouter` to N players — read each paired
   device and call `room.HandleInput("local_"+i, …)`. Reuse the existing
   game-aware mapping.
4. **View:** unchanged — `ArenaView` already renders all actors. Add a small
   "P1/P2…" marker over each local player's blob (color by `Palette.Team`/index).
5. **Lobby UI:** show join prompts; Start once ≥1 human present (bot-fill covers
   the rest, exactly like solo).
6. **Verify:** two gamepads, one screen, a full series; both players' inputs move
   their own blobs.

---

## Phase 5 — Online multiplayer (host-by-code)

Use **NGO + Unity Transport + Unity Relay/Lobby/Auth** as a *transport only*; the
host runs the authoritative `GameRoom`. The `Wire` codec
(`Eliminated.Sim.Net.Wire`, already round-trip tested) is the serialization.

1. **Packages** (`Packages/manifest.json`): `com.unity.netcode.gameobjects`,
   `com.unity.transport`, `com.unity.services.core`,
   `com.unity.services.authentication`, `com.unity.services.relay`,
   `com.unity.services.lobby`.
2. **Services init:** `await UnityServices.InitializeAsync();
   await AuthenticationService.Instance.SignInAnonymouslyAsync();` (later swap for
   a Steam auth token for Steam cross-progression).
3. **Host:** create a Relay allocation → get the **Relay join code**; create a
   **Lobby** storing that join code in lobby data; show the Lobby's short **lobby
   code** as the shareable join code. Configure `UnityTransport` with the Relay
   allocation, then `NetworkManager.StartHost()`.
4. **Join:** look up the lobby by code → read the Relay join code → set
   `UnityTransport` join allocation → `NetworkManager.StartClient()`.
5. **`NetSession : NetworkBehaviour`** — the single transport object:
   - Client→host: `[ServerRpc(RequireOwnership=false)] SubmitInput(byte[] input)`
     → `Wire.DecodeInput` → `room.HandleInput(senderPlayerId, input)`. Send
     `move`/`aim` unreliable, `action`/`choose`/`tap` reliable.
   - Host→client: each tick, `Wire.EncodeFrame(snap.Game, snap.T, snap.StartAt,
     snap.Actors, snap.Fx, JsonUtility.ToJson(snap.Data))` → targeted `ClientRpc`
     **per client**, folding that client's entry from `snap.Secrets` into the
     `DataJson` so hidden-role info (Secret Santa) only reaches its owner.
   - Also broadcast `RoomState` (phase, round, current game, players) on change.
6. **Client side becomes a snapshot consumer:** introduce an `ISnapshotSource`
   that both `SimRunner` (in-process) and a new `NetClient` implement; point
   `ArenaView`/`HudUi` at the interface so they don't care whether play is local
   or online. Decode `DataJson` back into the per-game data type with
   `JsonUtility.FromJson<T>` keyed by `frame.Game`.
7. **Bots** are added by the host into the room as before (never networked).
8. **Reconnection:** rebind a returning client to its existing `Player` by a stable
   `clientId`; its actor kept ticking as idle while away (the sim already tolerates
   no-input actors).
9. **Dedicated server (optional follow-up):** the same `SimHost` in a headless
   build; clients connect via a direct `UnityTransport` endpoint instead of Relay.
10. **Verify:** two editor/players, one enters the other's code, a full series
    runs with both controlling their blobs; kill/rejoin mid-series.

> **Interpolation:** the client already renders ~1 snapshot behind via
> `BlobView`'s smoothing. At 20 Hz this is smooth; add client-side prediction for
> the local blob only if the movement games feel laggy under real latency.

---

## Phase 6 — Platforms & Steam

1. **Steamworks:** import **Facepunch.Steamworks** (MIT) as a plugin under
   `Assets/Plugins/`; add a git-ignored `steam_appid.txt` (480 for testing).
   Wrap it in a `SteamService` (init/shutdown, guarded so non-Steam builds still
   run). Wire:
   - **Achievements** from results the sim computes (first win, champion, survive
     a Hardcore series, play each game…).
   - **Steam Cloud:** auto-sync the `profile.json` written by `SaveService`
     (configure the cloud file in the partner backend; the file path is already in
     `Application.persistentDataPath`).
   - **Leaderboards:** post Marbles/wins; mirror to UGS Leaderboards for cross-play.
   - **Friend invites → Relay handoff:** accept invite → fetch lobby code → the
     Phase-5 join flow. **Rich Presence** advertises the lobby code.
   - **Steam Input:** controller glyphs; ship a default Steam Input config.
2. **Steam Deck verified checklist:** default controller config, ≥9 pt text,
   suspend/resume safe, native 1280×800, no external launchers. Run the Deck
   compatibility tool.
3. **Mobile (Android/iOS):** the on-screen touch controls in `LocalInputRouter`
   need a UI overlay (left joystick + context buttons); add IL2CPP + ARM64 build
   settings; verify the 1280×720 arena letterboxes cleanly.
4. **Cross-play:** UGS Relay/Lobby is platform-agnostic, so PC ↔ mobile ↔ Deck
   share rooms; layer Steam features only on Steam builds (guarded by `SteamService`).

---

## Phase 7 — Polish, accessibility, localization & store

1. **UI Toolkit front end:** replace the IMGUI `HudUi` with UXML/USS screens
   (menu, shop, lobby, HUD, results, settings). Build the **cosmetics shop** from
   `Eliminated.Sim.Economy.Cosmetics` (+ `CosmeticsWallet.TryPurchase`).
2. **Per-game view modules:** the slice renders actors generically + Boomerang
   props. Add a small renderer per game reading its `Snapshot.Data`
   (e.g. `TugOfWar.TugData.RopePos`, `GlassBridge.GlassData`, `RpsMinusOne`,
   `KingOfTheHill.KothData.Islands`, `Mingle` rooms, `MusicalChairs` chairs…).
   These are display-only; the sim already provides the data.
3. **Accessibility:** finish remappable controls via an **Input Actions asset** +
   the Input System rebinding API (save bindings to the profile); honor the
   existing `reduceFlashAndShake` toggle in FX; verify colorblind palettes against
   real assets; add text scaling. Keep subtitles wired to every Game Master line.
4. **Localization:** add `com.unity.localization`; move all UI/VO/subtitle strings
   into String Tables; ship EN + scaffold es/fr/de/pt-BR/ja; RTL-ready TMP fonts.
5. **Audio:** swap generated SFX for curated CC0 sets (see
   [ASSET_SOURCES.md](ASSET_SOURCES.md)); AudioMixer buses already planned in
   settings; add a Game Master VO pass (recorded or TTS) with captions.
6. **Real art:** replace placeholder blob spheres with 3D blob models
   (blendswap/TurboSquid-sourced) + the 6 themed arenas; track every asset's
   license in ASSET_SOURCES.md.
7. **Store/release:** Steam store page + capsule art; age-rating questionnaires
   (IARC/ESRB — target E10+, see README); EULA/privacy; build pipeline
   (GameCI + IL2CPP per platform); enable the commented-out Unity test job in
   `.github/workflows/ci.yml` with a Unity license secret.
8. **E2E:** the headless `AllGamesSmokeTests` already proves every game completes;
   add a Unity PlayMode smoke that boots the Boot scene and plays one series.

---

## Reusable contracts already in place

- **Authoritative loop:** `GameRoom.Tick(dt)` / `HandleInput` / `BuildSnapshot`.
- **Serialization:** `Eliminated.Sim.Net.Wire` (input + snapshot frame, tested).
- **Economy:** `Marbles`, `Cosmetics`, `CosmeticsWallet` (tested).
- **Catalog:** `GameCatalog` (all 16 games + pacing metadata).
- **Save:** `SaveService` + `PlayerProfile` (local JSON; Steam/UGS cloud hooks here).
