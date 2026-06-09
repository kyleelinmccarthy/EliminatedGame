using UnityEngine;
using UnityEngine.InputSystem;
using Eliminated.Sim.Model;
using Eliminated.Game.SimBridge;
using Eliminated.Game.View;

namespace Eliminated.Game.Input
{
    /// <summary>
    /// Reads input for every local human (solo = 1, shared-screen co-op = N) and
    /// routes game-aware <see cref="GameInput"/> to the room per player. Device
    /// assignment: solo uses keyboard&amp;mouse + gamepad 0; in co-op, player 0 is
    /// keyboard&amp;mouse and players 1..N take gamepads 0..N-2. Direct device
    /// polling keeps the slice asset-free; a remappable Input Actions asset
    /// (accessibility) lands in Phase 7.
    /// </summary>
    public sealed class LocalInputHub : MonoBehaviour
    {
        private SimRunner _sim;
        private ArenaView _arena;

        public void Init(SimRunner sim, ArenaView arena) { _sim = sim; _arena = arena; }

        private struct Controls { public float Dx, Dy; public bool Primary, Dash, HasAim; public float Aim; }

        private void Update()
        {
            if (_sim == null || !_sim.HasSeries) return;
            var snap = _sim.Latest;
            if (snap == null) return;
            GameId game = snap.Game;

            var ids = _sim.LocalPlayerIds;
            bool coop = ids.Count > 1;

            for (int i = 0; i < ids.Count; i++)
            {
                var c = ReadControls(i, coop, game);
                _sim.SubmitFor(ids[i], GameInput.Move(c.Dx, c.Dy));
                if (c.Primary)
                    _sim.SubmitFor(ids[i], game == GameId.Boomerang ? GameInput.Action("throw") : GameInput.Tap());
                if (c.Dash) _sim.SubmitFor(ids[i], GameInput.Action("dash"));
                if (game == GameId.Boomerang && c.HasAim) _sim.SubmitFor(ids[i], GameInput.Aim(c.Aim));
                if (i == 0) HandleDiscreteChoices(ids[0], game); // keyboard player drives Choose-games
            }
        }

        /// <summary>Keyboard → discrete `Choose` inputs for the turn/timing games.</summary>
        private void HandleDiscreteChoices(string pid, GameId game)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            switch (game)
            {
                case GameId.GlassBridge:
                case GameId.ChutesAndLadders:
                    if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) _sim.SubmitFor(pid, GameInput.Choose("L"));
                    if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) _sim.SubmitFor(pid, GameInput.Choose("R"));
                    break;
                case GameId.SimonSays:
                    if (kb.wKey.wasPressedThisFrame) _sim.SubmitFor(pid, GameInput.Choose("head"));
                    if (kb.aKey.wasPressedThisFrame) _sim.SubmitFor(pid, GameInput.Choose("nose"));
                    if (kb.sKey.wasPressedThisFrame) _sim.SubmitFor(pid, GameInput.Choose("blink"));
                    if (kb.dKey.wasPressedThisFrame) _sim.SubmitFor(pid, GameInput.Choose("flip"));
                    if (kb.spaceKey.wasPressedThisFrame) _sim.SubmitFor(pid, GameInput.Choose("jump"));
                    break;
            }
        }

        private Controls ReadControls(int index, bool coop, GameId game)
        {
            var c = new Controls();
            bool usesKeyboard = index == 0;            // player 0 always has the keyboard
            Gamepad pad = PadFor(index, coop);

            if (usesKeyboard)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) c.Dx += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) c.Dx -= 1f;
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) c.Dy -= 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) c.Dy += 1f;
                    c.Primary |= kb.spaceKey.wasPressedThisFrame;
                    c.Dash |= kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
                }
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    c.Primary |= mouse.leftButton.wasPressedThisFrame;
                    if (game == GameId.Boomerang && _arena != null)
                    {
                        var local = _sim.ActorFor(_sim.LocalPlayerIds[index]);
                        if (local != null && _arena.TryScreenToLogical(mouse.position.ReadValue(), out var lg))
                        {
                            c.HasAim = true;
                            c.Aim = Mathf.Atan2(lg.Y - local.Pos.Y, lg.X - local.Pos.X);
                        }
                    }
                }
            }

            if (pad != null)
            {
                var s = pad.leftStick.ReadValue();
                c.Dx += s.x;
                c.Dy -= s.y; // stick up (+y) → logical −y
                c.Primary |= pad.buttonSouth.wasPressedThisFrame;
                c.Dash |= pad.buttonEast.wasPressedThisFrame;
                var rs = pad.rightStick.ReadValue();
                if (game == GameId.Boomerang && rs.sqrMagnitude > 0.12f)
                {
                    c.HasAim = true;
                    c.Aim = Mathf.Atan2(-rs.y, rs.x);
                }
            }
            return c;
        }

        /// <summary>The gamepad feeding a given player slot (or null).</summary>
        private static Gamepad PadFor(int index, bool coop)
        {
            var pads = Gamepad.all;
            if (!coop)
                return index == 0 && pads.Count > 0 ? pads[0] : null; // solo: keyboard + pad 0
            // co-op: player 0 = keyboard only; players 1..N = pads 0..N-2
            int padIndex = index - 1;
            return padIndex >= 0 && padIndex < pads.Count ? pads[padIndex] : null;
        }
    }
}
