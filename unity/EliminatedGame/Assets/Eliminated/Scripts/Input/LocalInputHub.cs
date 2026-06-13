using UnityEngine;
using UnityEngine.InputSystem;
using Eliminated.Sim.Model;
using Eliminated.Game.Net;
using Eliminated.Game.SimBridge;
using Eliminated.Game.View;
using Eliminated.Game.Save;
using Eliminated.Game.Audio;

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
        private ISnapshotSource _sim;
        private ArenaView _arena;

        public void Init(ISnapshotSource sim, ArenaView arena) { _sim = sim; _arena = arena; }

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
                // Red Light is a horizontal race toward the doll on the RIGHT, so "forward" (W / ↑ /
                // D / →) must drive +x and "back" (S / ↓ / A / ←) −x — pressing W runs you toward the
                // finish, not upward. (Lane drifting isn't needed; it's a straight dash.)
                if (game == GameId.RedLight)
                {
                    c.Dx = Mathf.Clamp(c.Dx - c.Dy, -1f, 1f);
                    c.Dy = 0f;
                }
                _sim.SubmitFor(ids[i], GameInput.Move(c.Dx, c.Dy));
                if (c.Primary)
                {
                    // The primary button is a DIFFERENT action per game — sending a bare Tap()
                    // (the old behaviour) silently did nothing in Dodgeball/KotH/Prop Hunt, so a
                    // human could never throw / shove / swing. Route the correct one.
                    string act = PrimaryActionFor(game);
                    _sim.SubmitFor(ids[i], act != null ? GameInput.Action(act) : GameInput.Tap());
                    // Instant audible feedback so mashing FEELS like it's doing something (esp. Tug,
                    // where the rope only reflects NET force and a tap can otherwise feel eaten).
                    if (game == GameId.TugOfWar) AudioService.Instance?.Play("drum", 0.3f);
                }
                if (c.Dash) _sim.SubmitFor(ids[i], GameInput.Action("dash"));
                if (c.HasAim) _sim.SubmitFor(ids[i], GameInput.Aim(c.Aim)); // only set for the aim games
                if (i == 0) HandleDiscreteChoices(ids[0], game); // keyboard player drives Choose-games
            }
        }

        // Current keyboard bindings (player 0), falling back to WASD/Space/Shift.
        private static GameSettings Binds => SaveService.Current?.settings;
        private static Key Up => Binds?.keyUp ?? Key.W;
        private static Key Down => Binds?.keyDown ?? Key.S;
        private static Key Left => Binds?.keyLeft ?? Key.A;
        private static Key Right => Binds?.keyRight ?? Key.D;
        private static Key Action => Binds?.keyAction ?? Key.Space;
        private static Key Dash => Binds?.keyDash ?? Key.LeftShift;

        private static bool Held(Keyboard kb, Key k) => k != Key.None && kb[k].isPressed;
        private static bool Hit(Keyboard kb, Key k) => k != Key.None && kb[k].wasPressedThisFrame;

        /// <summary>The game-specific action the PRIMARY button fires (null = a bare Tap, which
        /// Keepy-Uppy's spike / Jump Rope's jump / Tug's pull all consume).</summary>
        private static string PrimaryActionFor(GameId g)
        {
            switch (g)
            {
                case GameId.Boomerang:
                case GameId.Dodgeball: return "throw";
                case GameId.KingOfTheHill: return "shove";
                case GameId.PropHunt: return "swing";
                default: return null;
            }
        }

        /// <summary>Games where the primary action is AIMED (mouse / right-stick).</summary>
        private static bool UsesAim(GameId g) =>
            g == GameId.Boomerang || g == GameId.Dodgeball ||
            g == GameId.KingOfTheHill || g == GameId.KeepyUppy;

        /// <summary>Keyboard → discrete `Choose` inputs for the turn/timing games.</summary>
        private void HandleDiscreteChoices(string pid, GameId game)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            switch (game)
            {
                case GameId.GlassBridge:
                    // The two panes are the TOP and BOTTOM lanes of the bridge, so pick with up/down.
                    if (kb.upArrowKey.wasPressedThisFrame || Hit(kb, Up)) _sim.SubmitFor(pid, GameInput.Choose("L"));    // top pane (side 0)
                    if (kb.downArrowKey.wasPressedThisFrame || Hit(kb, Down)) _sim.SubmitFor(pid, GameInput.Choose("R")); // bottom pane (side 1)
                    break;
                case GameId.ChutesAndLadders:
                    if (kb.leftArrowKey.wasPressedThisFrame || Hit(kb, Left)) _sim.SubmitFor(pid, GameInput.Choose("L"));
                    if (kb.rightArrowKey.wasPressedThisFrame || Hit(kb, Right)) _sim.SubmitFor(pid, GameInput.Choose("R"));
                    break;
                case GameId.SimonSays:
                    if (Hit(kb, Up)) _sim.SubmitFor(pid, GameInput.Choose("head"));
                    if (Hit(kb, Left)) _sim.SubmitFor(pid, GameInput.Choose("nose"));
                    if (Hit(kb, Down)) _sim.SubmitFor(pid, GameInput.Choose("blink"));
                    if (Hit(kb, Right)) _sim.SubmitFor(pid, GameInput.Choose("flip"));
                    if (Hit(kb, Action)) _sim.SubmitFor(pid, GameInput.Choose("jump"));
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
                    if (Held(kb, Right) || kb.rightArrowKey.isPressed) c.Dx += 1f;
                    if (Held(kb, Left) || kb.leftArrowKey.isPressed) c.Dx -= 1f;
                    if (Held(kb, Up) || kb.upArrowKey.isPressed) c.Dy -= 1f;
                    if (Held(kb, Down) || kb.downArrowKey.isPressed) c.Dy += 1f;
                    c.Primary |= Hit(kb, Action);
                    c.Dash |= Hit(kb, Dash) || kb.rightShiftKey.wasPressedThisFrame;
                }
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    c.Primary |= mouse.leftButton.wasPressedThisFrame;
                    if (UsesAim(game) && _arena != null)
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
                if (UsesAim(game) && rs.sqrMagnitude > 0.12f)
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
