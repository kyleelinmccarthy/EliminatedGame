using UnityEngine;
using UnityEngine.InputSystem;
using Eliminated.Sim.Model;
using Eliminated.Game.Net;
using Eliminated.Game.SimBridge;

namespace Eliminated.Game.Input
{
    /// <summary>
    /// On-screen touch controls for mobile / Steam Deck touch: a left virtual
    /// joystick (move) and right action + dash buttons, feeding the solo local
    /// player the same <see cref="GameInput"/> as keyboard/gamepad. Only drawn when
    /// a touchscreen is present, so it never appears on desktop. Game-aware (the
    /// action button means throw in the brawl, mash in tug-of-war, etc.).
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        private ISnapshotSource _sim;
        private GUIStyle _btn;

        public void Init(ISnapshotSource sim) => _sim = sim;

        private static bool TouchPresent => Touchscreen.current != null;

        private void Update()
        {
            if (!TouchPresent || _sim == null || !_sim.HasSeries || _sim.Latest == null) return;
            string pid = _sim.LocalPlayerIds.Count > 0 ? _sim.LocalPlayerIds[0] : SimRunner.LocalPlayerId;

            // Left joystick: the left-most active touch drives movement.
            var ts = Touchscreen.current;
            Vector2 origin = new Vector2(Screen.width * 0.18f, Screen.height * 0.22f);
            float radius = Screen.height * 0.16f;
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                var p = t.position.ReadValue();
                if (p.x > Screen.width * 0.45f) continue; // right side = action buttons
                float dx = Mathf.Clamp((p.x - origin.x) / radius, -1f, 1f);
                float dy = Mathf.Clamp(-(p.y - origin.y) / radius, -1f, 1f); // screen-up → logical −y
                _sim.SubmitFor(pid, GameInput.Move(dx, dy));
                break;
            }
        }

        private void OnGUI()
        {
            if (!TouchPresent || _sim == null || !_sim.HasSeries || _sim.Latest == null) return;
            if (_btn == null) _btn = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = FontStyle.Bold };

            string pid = _sim.LocalPlayerIds.Count > 0 ? _sim.LocalPlayerIds[0] : SimRunner.LocalPlayerId;
            GameId game = _sim.Latest.Game;
            float s = Screen.height / 900f;
            float bw = 150 * s, bh = 150 * s, m = 40 * s;

            // ACTION (throw / jump / mash) — bottom-right
            if (GUI.Button(new Rect(Screen.width - bw - m, Screen.height - bh - m, bw, bh),
                    game == GameId.Boomerang ? "THROW" : "ACT", _btn))
                _sim.SubmitFor(pid, game == GameId.Boomerang ? GameInput.Action("throw") : GameInput.Tap());

            // DASH — above the action button
            if (GUI.Button(new Rect(Screen.width - bw - m, Screen.height - bh * 2f - m * 1.6f, bw, bh * 0.7f), "DASH", _btn))
                _sim.SubmitFor(pid, GameInput.Action("dash"));

            // a faint joystick hint bottom-left
            GUI.Box(new Rect(Screen.width * 0.18f - 70 * s, Screen.height - Screen.height * 0.22f - 70 * s, 140 * s, 140 * s), "move");
        }
    }
}
