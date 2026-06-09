# Unity setup (first open)

This repo's `unity/EliminatedGame/` is a Unity 6 (URP) project. It is authored as
source; you build/run it in the Unity Editor (it cannot compile on a headless box
without the Editor). The headless **simulation** tests run without Unity — see
the repo README.

## 1. Install the Editor

- Install **Unity Hub**.
- Install a **Unity 6 LTS** editor — the project pins `6000.0.23f1` in
  `ProjectSettings/ProjectVersion.txt`; **any `6000.0.x` LTS patch works** and Hub
  will offer to open with the patch you have installed.
- During install, add the **build support modules** you need: Windows/Mac/Linux
  (Desktop), Android (mobile), and optionally Linux Dedicated Server (future).

## 2. Open the project

- In Unity Hub → *Add project from disk* → select `unity/EliminatedGame`.
- On first open, Package Manager resolves the packages in `Packages/manifest.json`.
  If a patch version isn't available for your editor, Package Manager will resolve
  to the nearest compatible one — accept it. The embedded local package
  `com.eliminated.sim` (the simulation) resolves from `Packages/com.eliminated.sim`.

## 3. One-time project settings (until committed)

Because the editor can't run on the CI/dev box, the binary `ProjectSettings`
assets are generated on your first open. Set and save these once:

- **Render pipeline**: URP is included; assign the URP asset under
  *Project Settings → Graphics* (created in Phase 2 under
  `Assets/Eliminated/Settings/`). Use **Linear** color space.
- **Active Input Handling**: *Project Settings → Player → Active Input Handling →
  "Input System Package (New)"* (we use the new Input System).
- **Company/Product name**, default icon, and **Scripting Backend**: IL2CPP for
  release builds (Mono is fine in-editor).
- Commit the resulting `ProjectSettings/*.asset` so the next clone is configured.

> **If Package Manager reports a version that doesn't exist** for your exact
> editor patch, accept its offer to resolve to the nearest compatible version
> (or delete the version string for that line and let it pick). The pinned
> versions target Unity 6000.0 LTS and are intentionally a minimal set.

## 4. Packages added in later phases

To keep early phases easy to open, only the packages the current phase uses are
in the manifest. Others are added when their phase begins:

- **Phase 3 — Breadth/UX**: `com.unity.cinemachine`, `com.unity.localization`,
  `com.unity.addressables`.
- **Phase 5 — Online**: `com.unity.netcode.gameobjects`, `com.unity.transport`,
  `com.unity.services.relay`, `com.unity.services.lobby`,
  `com.unity.services.authentication`, `com.unity.services.core`.
- **Phase 6 — Steam**: **Facepunch.Steamworks** (MIT) imported as a plugin DLL
  under `Assets/Plugins/`, plus a `steam_appid.txt` (git-ignored) for testing.

## 5. Run the vertical slice (Phase 2)

The slice is **fully code-driven** — there is no scene to set up. A
`RuntimeInitializeOnLoadMethod` bootstrap (`GameBootstrapper`) builds the camera,
arena, input, and UI on Play.

1. Open any scene (an empty scene is fine — *File → New Scene → Empty*).
2. *Project Settings → Player → Active Input Handling* = **Input System Package
   (New)** (or *Both*).
3. Press **Play**. The menu appears (IMGUI for the slice).
4. Choose **Solo vs Bots — Casual** (or Hardcore). You play 4 rounds against bots
   across Red Light Green Light, Tug of War, and Boomerang Brawl, then see the
   results. Marbles you earn are saved to your local profile.
5. **Local co-op (shared screen):** connect one or more gamepads and pick
   **Local Co-op**. Player 1 uses keyboard&mouse; each gamepad is another player
   (up to 4). Bots fill the rest. (P1's marbles bank to the local profile.)

### Controls (slice)

| Action | Keyboard / Mouse | Gamepad |
|---|---|---|
| Move | WASD / Arrows | Left stick |
| Aim (Boomerang) | Mouse | Right stick |
| Throw / Mash / Jump | Space / Left-click | A (South) |
| Dash | Shift | B (East) |

> Colorblind palettes, subtitles, and volume are in the in-menu **Settings**.
> Remappable controls arrive with the Input Actions asset in a later phase.

## 6. Tests

Run tests via *Window → General → Test Runner* (EditMode). The headless
simulation suite also runs without Unity: `cd sim && dotnet test`.
