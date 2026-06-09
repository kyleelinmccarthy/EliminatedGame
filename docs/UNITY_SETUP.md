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

## 4. Packages added in later phases

To keep early phases easy to open, networking/services packages are **not** in the
manifest yet. They get added when their phase begins:

- **Phase 5 — Online**: `com.unity.netcode.gameobjects`, `com.unity.transport`,
  `com.unity.services.relay`, `com.unity.services.lobby`,
  `com.unity.services.authentication`, `com.unity.services.core`.
- **Phase 6 — Steam**: **Facepunch.Steamworks** (MIT) imported as a plugin DLL
  under `Assets/Plugins/`, plus a `steam_appid.txt` (git-ignored) for testing.

## 5. Run

- Open `Assets/Eliminated/Scenes/Boot.unity` (added in Phase 2) and press Play.
- Run tests via *Window → General → Test Runner* (EditMode + PlayMode).
