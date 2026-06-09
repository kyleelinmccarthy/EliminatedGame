using UnityEngine;
using UnityEngine.InputSystem;
using Eliminated.Sim.Model;
using Eliminated.Game.SimBridge;
using Eliminated.Game.View;

namespace Eliminated.Game.Input
{
    /// <summary>
    /// Reads keyboard/mouse and gamepad each frame and translates them into the
    /// active game's <see cref="GameInput"/> for the local player. Game-aware so
    /// the primary button means "throw" in the brawl, "mash" in tug-of-war, etc.
    /// Direct device polling keeps the slice asset-free; Phase 7 introduces a
    /// remappable Input Actions asset (accessibility).
    /// </summary>
    public sealed class LocalInputRouter : MonoBehaviour
    {
        private SimRunner _sim;
        private ArenaView _arena;

        public void Init(SimRunner sim, ArenaView arena)
        {
            _sim = sim;
            _arena = arena;
        }

        private void Update()
        {
            if (_sim == null || !_sim.HasSeries) return;
            var snap = _sim.Latest;
            if (snap == null) return;
            GameId game = snap.Game;

            var kb = Keyboard.current;
            var gp = Gamepad.current;
            var mouse = Mouse.current;

            // ── Movement ──────────────────────────────────────────────────
            float dx = 0f, dy = 0f;
            if (kb != null)
            {
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dx += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dx -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dy -= 1f;  // up = logical −y
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dy += 1f;
            }
            if (gp != null)
            {
                var s = gp.leftStick.ReadValue();
                dx += s.x;
                dy -= s.y; // stick up (+y) → logical −y
            }
            _sim.Submit(GameInput.Move(dx, dy));

            // ── Primary action ────────────────────────────────────────────
            bool primary = (kb != null && kb.spaceKey.wasPressedThisFrame)
                           || (mouse != null && mouse.leftButton.wasPressedThisFrame)
                           || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            if (primary)
            {
                switch (game)
                {
                    case GameId.Boomerang: _sim.Submit(GameInput.Action("throw")); break;
                    case GameId.TugOfWar: _sim.Submit(GameInput.Tap()); break;
                    default: _sim.Submit(GameInput.Tap()); break;
                }
            }

            // ── Dash (arena games) ────────────────────────────────────────
            bool dash = (kb != null && (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame))
                        || (gp != null && gp.buttonEast.wasPressedThisFrame);
            if (dash) _sim.Submit(GameInput.Action("dash"));

            // ── Aim (combat games) ────────────────────────────────────────
            if (game == GameId.Boomerang)
            {
                var local = _sim.LocalActor;
                if (local != null)
                {
                    if (gp != null && gp.rightStick.ReadValue().sqrMagnitude > 0.12f)
                    {
                        var r = gp.rightStick.ReadValue();
                        _sim.Submit(GameInput.Aim(Mathf.Atan2(-r.y, r.x)));
                    }
                    else if (mouse != null && _arena != null &&
                             _arena.TryScreenToLogical(mouse.position.ReadValue(), out var lg))
                    {
                        _sim.Submit(GameInput.Aim(Mathf.Atan2(lg.Y - local.Pos.Y, lg.X - local.Pos.X)));
                    }
                }
            }
        }
    }
}
