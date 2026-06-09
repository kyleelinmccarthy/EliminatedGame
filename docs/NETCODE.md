# Netcode

One simulation, three deployment modes. The simulation never changes; only the
transport that feeds it input and ships out snapshots changes.

## Modes

| Mode | Where the sim runs | Input source | Snapshot sink |
|---|---|---|---|
| **Solo / Local co-op** | In the client process (`SimHost`) | Local input devices → player slots; bots fill | Local view directly |
| **Online host (listen server)** | On the host client (`SimHost`) | Local devices + remote peers' input RPCs | Local view + per-client snapshot RPCs |
| **Online client** | — (remote) | Sends its own input RPCs | Receives snapshots, renders |
| **Dedicated (built)** | Headless `GameServer` (`server/`) | All players' input frames | All clients' snapshot frames |

## Transport choice

We use **Netcode for GameObjects (NGO) + Unity Transport (UTP) + Unity Relay**
as a *message pipe only*, not for object replication. A single networked object,
`NetSession`, carries:

- **Up (client → host):** `SubmitInput(GameInput)` — `ServerRpc`.
  - `move`/`aim` use the **unreliable** delivery (latest wins; cheap, frequent).
  - `action`/`choose`/`tap` use **reliable** delivery (must not drop).
- **Down (host → client):** serialized `Snapshot` + `RoomState`.
  - Snapshots are sent **per-client** (targeted) so each player's `secret`
    (hidden role info, e.g. Secret Santa givers) is folded in for only that
    player and stripped from everyone else.

The authoritative `GameRoom`/`IMinigame` is the only thing that mutates game
state. Clients are pure renderers + input emitters (no client-side prediction in
v1 — snapshots at 20 Hz + interpolation are smooth enough for a top-down party
game; we can add prediction later for the movement games if needed).

## Rooms & join-by-code (Unity Lobby + Relay)

Maps the original "host a room, share a 4-letter code" flow onto UGS:

1. Host signs in (UGS Authentication — anonymous or Steam token).
2. Host creates a **Relay** allocation → gets a **Relay join code**.
3. Host creates a **Lobby**; stores the Relay join code in lobby data; Lobby
   returns a short **Lobby Code** — *this is the shareable join code shown in UI*.
4. Player enters the Lobby Code → fetches the lobby → reads the Relay join code →
   connects UTP to the host via Relay.
5. NGO starts: host = server, joiners = clients. `NetSession` begins exchanging
   input/snapshots.

Bots are added by the host directly into the sim (`RoomManager.AddBot`) — they
never touch the network; they're just non-human player slots.

## Serialization

`Snapshot`/`GameInput`/`RoomState` implement compact binary read/write
(`INetworkSerializable` on the Unity side, plain `byte[]` writers in the sim
model where shared). Snapshots carry only what the view needs: actor id, x, y,
facing, flags (alive/it/team/frozen/burning/shield/ghost/scale), anim, plus a
per-game `data` blob and one-shot `fx` effects.

## Reconnection

Like the original: a client reconnecting with the same stable `clientId` re-binds
to its existing player slot in the room and resumes mid-game (its actor kept
ticking as idle/no-input while away). Rooms persist briefly after the last human
leaves (grace period) so a quick drop doesn't kill the session.

## Steam interop

Steam features layer on top without replacing UGS transport:
- Friend **invites**: accepting a Steam invite hands off the Lobby Code → same
  Relay join flow.
- **Rich Presence** advertises the joinable Lobby Code.
- Achievements, Cloud saves, and Leaderboards are reported from results the sim
  computes; they don't affect transport.
