using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Eliminated.Sim.Model;
using Eliminated.Sim.Games;
using Eliminated.Sim.Powerups;
using Eliminated.Sim.Room;
using Eliminated.Game.SimBridge;
using Eliminated.Game.Save;
using Eliminated.Game.Accessibility;
using Eliminated.Game.Audio;
using Eliminated.Game.Net;
using Eliminated.Game.Platform;
using Eliminated.Sim.Localization;
using Eliminated.Sim.Economy;
using Eliminated.Game.View;

namespace Eliminated.Game.UI
{
    /// <summary>
    /// Immediate-mode HUD, menus, and overlays driving the full loop
    /// (menu → lobby → play → round/series results). Deliberately code-only and
    /// asset-free so the vertical slice runs on Play; Phase 3/7 replace this with
    /// a UI Toolkit front end. Also awards the local player's earned marbles to
    /// the saved profile at series end, and renders subtitles.
    /// </summary>
    public sealed class HudUi : MonoBehaviour
    {
        // _sim hosts local play; _net drives online play; _router is the active
        // backend the in-game HUD/intro/results read from (whichever is live).
        private SimRunner _sim;
        private NetClient _net;
        private SessionRouter _router;
        private RoomPhase _lastPhase = RoomPhase.Lobby;
        private bool _seriesBanked;
        private string _caption = "";
        private float _captionUntil;
        private string _toast = "";   // transient success/confirmation pill (menu + in-game)
        private float _toastUntil;

        // Caption tracking (subtitles for Game-Master cues + eliminations). Updated
        // once per frame in Update so OnGUI's multiple passes never double-fire a cue.
        private readonly HashSet<string> _aliveSeen = new HashSet<string>();
        private GameId? _capGame;     // game whose cue-state we're currently tracking
        private bool _capRed;         // last Red Light / Green Light state captioned
        private string _capSimon;     // last Simon command captioned
        private float _elimVoiceUntil; // throttle the elimination voiceline (trickle deaths shouldn't stutter)

        // Games where eliminations trickle in by hunting/attrition over the round: a running
        // number callout would be chatty (and, for Prop Hunt, half spoils the find), so the
        // voice is HELD and the whole round's casualties are read once at the buzzer
        // ("Players 1, 2, 3, 4 have been eliminated."). Captions still show per death.
        private static readonly HashSet<GameId> DeferElimGames = new HashSet<GameId>
        {
            GameId.PropHunt, GameId.RedLight, GameId.Tag, GameId.Boomerang, GameId.Dodgeball, GameId.KingOfTheHill,
        };
        private readonly List<int> _deferredOut = new List<int>(); // tags banked for the round-end call

        private float _announcerBusyUntil; // Time.time the current announcer line finishes
        private bool _revealPending;       // the next round's reveal is held until she's done
        private float _revealAt;           // when to fire that held reveal

        // Front-of-house navigation. The menu is a little router: one page visible
        // at a time, with a nav bar on the home page and a back button elsewhere.
        private enum Page { Menu, Settings, Leaderboard, HowToPlay, PatchNotes, Account, Controls, Players, Online, Credits }
        private Page _page = Page.Menu;
        private Page _prevPage = Page.Menu; // tracks page entry so re-entered scroll views reset to the top
        private readonly HashSet<GameId> _htpExpanded = new HashSet<GameId>(); // cards with controls open
        private Vector2 _htpScroll, _patchScroll, _playerScroll, _accScroll, _creditsScroll, _setScroll;
        private float _setContentH = 460f; // settings scroll content height, remembered frame-to-frame
        private int _playerTab; // 0 = Players, 1 = Accessories
        private int _rebindIndex = -1;     // which binding is capturing a key, or -1
        private float _premuteVolume = 1f; // remembers level so the mute toggle can restore it
        private string _nameEdit;          // account name field buffer
        private string _urlEdit;           // online server-url field buffer
        private string _codeEdit = "";     // join-by-code field buffer
        private bool _onlineHardcore;      // chosen host difficulty (set by the play wizard)

        // Play wizard: a single Play button → pick a mode → pick difficulty → go.
        // Difficulty (Casual/Hardcore) is shared by every mode.
        private enum PlayStep { None, Mode, Difficulty, Lobby }
        private PlayStep _playStep = PlayStep.None;
        private enum PlayKind { Online, Solo, Coop }

        // Solo/co-op match setup (the "lobby"): how many contestants and rounds.
        private SeriesMode _lobbyMode = SeriesMode.Casual;
        private int _lobbyField = 12;     // total players (you + bots), 2..12
        private int _lobbyRounds = 4;
        private bool _lobbyMystery = false;
        private PlayKind _playKind;

        private GUIStyle _h1, _h2, _body, _pill, _ui;

        // ---- Web "Squid Game" theme — mirrors eliminated/app/globals.css :root ----
        private static readonly Color Bg0 = Hex("#08130f");
        private static readonly Color Bg1 = Hex("#0d2019");
        private static readonly Color Ink = Hex("#f1f7f4");
        private static readonly Color InkDim = Hex("#a6aeb2");
        // Brand accents routed through Palette so a colorblind mode visibly recolors
        // the menus/lobby too (not just in-match team/danger colors). Normal mode
        // returns the original Squid-Game neon hexes. Properties (not fields) so the
        // mode can change live without a restart.
        private static Color Pink => Palette.UiPrimary;     // #ff2e88
        private static Color Teal => Palette.UiSecondary;   // #19d3bd
        private static Color Green => Palette.UiPositive;   // #4cd9a0
        private static Color Yellow => Palette.UiWarning;   // #ffce3a
        private static Color Red => Palette.UiNegative;     // #ff5a4d
        private static readonly Color Panel = new Color(0.051f, 0.122f, 0.098f, 0.86f); // --panel
        private static readonly Color Line = new Color(0.925f, 0.941f, 0.961f, 0.14f);  // --line
        private static readonly Color LineBright = new Color(1f, 0.18f, 0.533f, 0.6f);   // --line-bright
        private static readonly Color OnDark = Hex("#0a1412"); // ink for teal/green faces
        private static readonly Color OnGold = Hex("#3a2a00"); // ink for yellow faces
        private static readonly Color WordTop = Hex("#ff4d97"); // hot-pink wordmark face

        private Texture2D _grad, _soft;

        public void Init(SimRunner sim, NetClient net, SessionRouter router)
        {
            _sim = sim;
            _net = net;
            _router = router;
        }

        private static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        private void EnsureStyles()
        {
            if (_h1 != null) return;
            // The web wordmark/heading font is Bungee; UI chrome is Baloo 2; body is Rubik.
            // (TTFs live in Resources/Fonts; missing fonts just fall back to the default.)
            var bungee = Resources.Load<Font>("Fonts/Bungee-Regular");
            var baloo = Resources.Load<Font>("Fonts/Baloo2-Bold");
            var rubik = Resources.Load<Font>("Fonts/Rubik-Regular");

            _h1 = new GUIStyle(GUI.skin.label) { font = bungee, fontSize = 40, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
            _h2 = new GUIStyle(GUI.skin.label) { font = baloo, fontSize = 24, normal = { textColor = Ink } };
            _body = new GUIStyle(GUI.skin.label) { font = rubik, fontSize = 18, wordWrap = true, normal = { textColor = new Color(Ink.r, Ink.g, Ink.b, 0.92f) } };
            _ui = new GUIStyle(GUI.skin.label) { font = baloo, fontSize = 18, normal = { textColor = Ink } };
            _pill = new GUIStyle(GUI.skin.box) { font = baloo, fontSize = 18, normal = { textColor = Ink } };

            _grad = MakeVerticalGradient(Bg1, Bg0);
            _soft = MakeRadial(96, 1.6f);
        }

        #region Theme drawing helpers

        private static Texture2D MakeVerticalGradient(Color top, Color bottom)
        {
            const int n = 64;
            var tex = new Texture2D(1, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < n; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(n - 1)));
            tex.Apply();
            return tex;
        }

        // White radial sprite: alpha 1 at centre → 0 at edge (falloff^exp). Tinted on draw.
        private static Texture2D MakeRadial(int size, float exp)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), exp)));
                }
            tex.Apply();
            return tex;
        }

        private static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
        private static Color Lighter(Color c, float t) => Color.Lerp(c, Color.white, t);

        // Filled rounded rect (uses Unity 6's rounded GUI.DrawTexture overload).
        private static void Fill(Rect r, Color c, float radius)
            => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);

        // Rounded outline of the given stroke width.
        private static void Stroke(Rect r, Color c, float width, float radius)
            => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, width, radius);

        // Unity 6 renders the GUIStyle.hover state for GUI.Label too, and our styles
        // inherited a near-white hover textColor from GUI.skin.label — so every label
        // (titles, body, numbers, the wordmark, marbles) flashed white on mouseover
        // even though it isn't clickable. All label drawing goes through Lbl, which
        // forces hover/active/focused to match normal so text never changes under the
        // cursor. Clickable controls keep their own hover feedback (background/border).
        private static GUIStyle Flat(GUIStyle s)
        {
            var c = s.normal.textColor;
            s.hover.textColor = s.active.textColor = s.focused.textColor =
                s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = s.onFocused.textColor = c;
            s.hover.background = s.active.background = s.focused.background = null;
            return s;
        }
        private static void Lbl(Rect r, string text, GUIStyle style) => GUI.Label(r, text, Flat(style));
        private static void Lbl(Rect r, GUIContent content, GUIStyle style) => GUI.Label(r, content, Flat(style));
        private static void Lbl(Rect r, string text) { Flat(GUI.skin.label); GUI.Label(r, text); }

        private void Glow(float cx, float cy, float size, Color c, float a)
            => GUI.DrawTexture(new Rect(cx - size / 2f, cy - size / 2f, size, size), _soft,
                ScaleMode.StretchToFill, true, 0f, Alpha(c, a), 0f, 0f);

        // Dark teal base + the four radial accent glows from the web background.
        private void DrawThemeBackdrop(float w, float h)
        {
            GUI.DrawTexture(new Rect(0, 0, w, h), _grad, ScaleMode.StretchToFill);
            Glow(w * 0.08f, h * -0.05f, h * 1.5f, Pink, 0.20f);
            Glow(w * 1.02f, h * 0.04f, h * 1.2f, Teal, 0.16f);
            Glow(w * 0.88f, h * 0.84f, h * 1.0f, Yellow, 0.15f);
            Glow(w * 0.34f, h * 1.18f, h * 1.3f, Pink, 0.13f);
        }

        // Decorative players drifting in the side margins (kept clear of the centre panel).
        private static readonly (float x, float y, float size, string id, float phase)[] _players =
        {
            (0.08f, 0.34f,  96f, "fox",   0.0f),
            (0.14f, 0.66f,  72f, "avo",   1.3f),
            (0.06f, 0.86f,  60f, "bunny", 2.5f),
            (0.92f, 0.30f,  84f, "pig",   0.7f),
            (0.87f, 0.62f, 104f, "cat",   1.9f),
            (0.95f, 0.85f,  66f, "slime", 3.1f),
        };

        private void DrawFloatingPlayers(float w, float h)
        {
            float t = Time.unscaledTime;
            foreach (var b in _players)
            {
                float bob = Mathf.Sin(t * 0.9f + b.phase) * 10f;
                float cx = w * b.x, cy = h * b.y + bob, sz = b.size;
                var body = Palette.Body(b.id);
                GUI.DrawTexture(new Rect(cx - sz / 2f, cy - sz / 2f + 10f, sz, sz * 0.9f), _soft, ScaleMode.StretchToFill, true, 0f, new Color(0, 0, 0, 0.22f), 0f, 0f);
                GUI.DrawTexture(new Rect(cx - sz / 2f, cy - sz / 2f, sz, sz), _soft, ScaleMode.StretchToFill, true, 0f, new Color(body.r, body.g, body.b, 0.9f), 0f, 0f);
                float hs = sz * 0.32f;
                GUI.DrawTexture(new Rect(cx - hs / 2f - sz * 0.12f, cy - sz * 0.26f, hs, hs), _soft, ScaleMode.StretchToFill, true, 0f, new Color(1, 1, 1, 0.32f), 0f, 0f);
            }
        }

        // Chunky candy button: cast shadow, darker 3D base, lifted face, sheen — presses down on click.
        private bool Btn(Rect r, string label, Color c, Color text, bool enabled = true, int fontSize = 21, float radius = 16f)
        {
            bool hover = enabled && GUI.enabled && r.Contains(Event.current.mousePosition);
            bool down = hover && Mouse.current != null && Mouse.current.leftButton.isPressed;

            Fill(new Rect(r.x + 3, r.y + 12, r.width - 6, r.height - 4), new Color(0, 0, 0, enabled ? 0.30f : 0.12f), radius);
            Fill(new Rect(r.x, r.y + 6, r.width, r.height), Alpha(Color.Lerp(c, Color.black, 0.42f), enabled ? 1f : 0.4f), radius);
            var face = new Rect(r.x, r.y + (down ? 5f : 0f), r.width, r.height);
            Fill(face, enabled ? (hover ? Lighter(c, 0.10f) : c) : Alpha(c, 0.4f), radius);
            Fill(new Rect(face.x + 4, face.y + 4, face.width - 8, face.height * 0.42f), new Color(1, 1, 1, 0.10f), radius * 0.7f);

            var st = new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = fontSize, wordWrap = false, normal = { textColor = Alpha(text, enabled ? 1f : 0.55f) } };
            var gc = new GUIContent(label);
            float maxW = r.width - 28f;                                    // never let a label clip:
            while (st.fontSize > 12 && st.CalcSize(gc).x > maxW) st.fontSize -= 1; // shrink to fit,
            if (st.CalcSize(gc).x > maxW) st.wordWrap = true;              // then wrap as a last resort.
            Lbl(face, gc, st);

            bool was = GUI.enabled; GUI.enabled = enabled;
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
            GUI.enabled = was;
            return clicked;
        }

        // Outlined "ghost" button (settings / quit / secondary actions).
        private bool GhostBtn(Rect r, string label, bool enabled = true, int fontSize = 18, float radius = 14f)
        {
            bool hover = enabled && GUI.enabled && r.Contains(Event.current.mousePosition);
            Fill(r, new Color(1, 1, 1, hover ? 0.09f : 0.05f), radius);
            Stroke(r, hover ? LineBright : Line, 2f, radius);
            var st = new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = fontSize, normal = { textColor = Alpha(Ink, enabled ? 1f : 0.5f) } };
            Lbl(r, label, st);

            bool was = GUI.enabled; GUI.enabled = enabled;
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
            GUI.enabled = was;
            return clicked;
        }

        // Small rounded nav pill (the web ".pill"). Optional accent tints the border.
        private bool Pill(Rect r, string label, Color? accent = null, bool active = false)
        {
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            var border = accent ?? Line;
            Fill(r, new Color(1, 1, 1, active ? 0.12f : hover ? 0.09f : 0.05f), r.height / 2f);
            Stroke(r, hover || active ? (accent ?? LineBright) : border, hover || active ? 1.5f : 1f, r.height / 2f);
            Lbl(r, label, new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 15, normal = { textColor = Ink } });
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // Colored category chip (patch-notes tag etc.) — non-interactive.
        private void Chip(Rect r, string label, Color c)
        {
            Fill(r, Alpha(c, 0.16f), r.height / 2f);
            Stroke(r, Alpha(c, 0.6f), 1f, r.height / 2f);
            Lbl(r, label, new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 12, normal = { textColor = c } });
        }

        // Section header + a Loc.Get("ui.back") ghost button with a cynical subtitle.
        private void DrawSubHeader(float w, float h, string title, string subtitle)
        {
            DrawThemeBackdrop(w, h);
            if (GhostBtn(new Rect(40, 36, 130, 40), Loc.Get("ui.back")))
                _page = Page.Menu;
            Lbl(new Rect(0, 40, w, 56), title, new GUIStyle(_h1) { fontSize = 46 });
            if (!string.IsNullOrEmpty(subtitle))
                Lbl(new Rect(0, 96, w, 26), subtitle,
                    new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, fontSize = 16, normal = { textColor = InkDim } });
        }

        private void MarblesPill(Rect r, int marbles)
        {
            Fill(r, Alpha(Yellow, 0.13f), r.height / 2f);
            Stroke(r, Alpha(Yellow, 0.55f), 1.5f, r.height / 2f);
            Lbl(r, $"◍ {Loc.Get("ui.marbles")}: {marbles}",
                new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Yellow } });
        }

        // Big ELIMINATED wordmark: a hot-pink → white vertical gradient face over a
        // crisp dark outline, finished with the dead-player game icon standing in for
        // the period (mirrors the web wordmark). Bungee is already a heavy display
        // face, so we use its true weight — no FontStyle.Bold faux-bolding.
        private static readonly Vector2[] _outlineDirs =
        {
            new Vector2( 1, 0), new Vector2(-1,  0), new Vector2( 0, 1), new Vector2( 0, -1),
            new Vector2( 1, 1), new Vector2( 1, -1), new Vector2(-1, 1), new Vector2(-1, -1),
        };

        // White-on-arena text needs a dark edge to stay legible over the bright/neon floor.
        // Draws a soft drop shadow + a crisp 8-direction near-black outline, then the bright
        // face on top (each pass clones the style so colour mutations don't bleed).
        private void OutlineLbl(Rect r, string text, GUIStyle style, float thickness = 2.5f, float dropY = 5f)
        {
            var face = style.normal.textColor;
            Lbl(new Rect(r.x, r.y + dropY, r.width, r.height), text,
                new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, 0.40f) } });
            foreach (var d in _outlineDirs)
                Lbl(new Rect(r.x + d.x * thickness, r.y + d.y * thickness, r.width, r.height), text,
                    new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, 0.9f) } });
            Lbl(r, text, new GUIStyle(style) { normal = { textColor = face } });
        }

        // A game's status/instruction banner, pinned to the free strip at the very top of the
        // screen — centered in the gap between the round box (right edge x=370) and the top-right
        // info box (left edge x=w-270), so it never sits over the play area. The font scales up to
        // fill the gap (bigger than the old on-board banners) and only shrinks if a string would
        // clip. An optional dim sub-line tucks just beneath, still clear of the board.
        private void TopBanner(float w, string text, Color accent, string sub = null,
                               Color? subColor = null, int fontSize = 27, float ringAlpha = 0.55f)
        {
            const float gapL = 370f;
            float gapR = w - 270f, cx = (gapL + gapR) * 0.5f, maxTextW = gapR - gapL - 32f;
            var style = new GUIStyle(_ui) { fontSize = fontSize, alignment = TextAnchor.MiddleCenter, normal = { textColor = accent } };
            float tw = style.CalcSize(new GUIContent(text)).x;
            if (tw > maxTextW) { style.fontSize = Mathf.Max(16, Mathf.FloorToInt(fontSize * maxTextW / tw)); tw = style.CalcSize(new GUIContent(text)).x; }
            float pillW = Mathf.Min(gapR - gapL, tw + 44f);
            var r = new Rect(cx - pillW / 2f, 8f, pillW, 44f);
            Fill(r, new Color(0f, 0f, 0f, 0.64f), 10f);
            Stroke(r, Alpha(accent, ringAlpha), 2f, 10f);
            OutlineLbl(r, text, style, 2f, 3f);
            if (!string.IsNullOrEmpty(sub))
            {
                var ss = new GUIStyle(_body) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = subColor ?? Color.white } };
                float sw = Mathf.Min(gapR - gapL, ss.CalcSize(new GUIContent(sub)).x + 28f);
                var sr = new Rect(cx - sw / 2f, 54f, sw, 22f);
                Fill(sr, new Color(0f, 0f, 0f, 0.55f), 8f);
                OutlineLbl(sr, sub, ss, 1.5f, 2.5f);
            }
        }

        private void DrawWordmark(float w, float topY, string title)
        {
            const float rowH = 84f;
            var big = new GUIStyle(_h1) { fontSize = 66, alignment = TextAnchor.MiddleLeft };
            float fontSize = big.fontSize;

            // Lay the title + icon out as one centred group: text · gap · icon.
            float textW = big.CalcSize(new GUIContent(title)).x;
            float iconSize = fontSize * 0.5f;
            float gap = fontSize * 0.18f;
            float left = (w - (textW + gap + iconSize)) * 0.5f;
            var textRect = new Rect(left, topY, textW, rowH);

            // Plain stacked labels only — shadow, dark outline, then the pink face.
            // (No GUI.BeginClip: clip-band gradients warp under the global
            // GUIUtility.ScaleAroundPivot matrix, which is what mangled the word.)
            void Face(float dx, float dy, Color c) =>
                Lbl(new Rect(textRect.x + dx, textRect.y + dy, textRect.width, rowH), title,
                    new GUIStyle(big) { normal = { textColor = c } });

            Face(0f, 6f, new Color(0, 0, 0, 0.40f));                            // soft drop shadow
            const float o = 2f;                                                 // crisp dark outline
            foreach (var d in _outlineDirs) Face(d.x * o, d.y * o, new Color(0, 0, 0, 0.85f));
            Face(0f, 0f, WordTop);                                               // solid hot-pink face

            // Dead-player game icon as the period, sitting near the text baseline.
            float iconCy = topY + rowH * 0.5f + fontSize * 0.16f;
            DrawPlayerMark(new Rect(left + textW + gap, iconCy - iconSize * 0.5f, iconSize, iconSize));
        }

        // The dead-player game icon: a dark rounded-square badge with a pink rim and
        // crossed-out (✕ ✕) eyes — the wordmark's period and the app's mascot.
        private void DrawPlayerMark(Rect r)
        {
            float rad = r.width * 0.3f;
            Fill(new Rect(r.x + 2f, r.y + 5f, r.width, r.height), new Color(0, 0, 0, 0.35f), rad); // cast shadow
            Fill(r, Bg0, rad);                                                                     // dark badge
            Stroke(r, Alpha(Pink, 0.85f), 2f, rad);                                                // pink rim
            Glow(r.center.x, r.center.y, r.width * 0.92f, Pink, 0.22f);                            // inner player glow

            float eyeY = r.y + r.height * 0.5f;
            float dx = r.width * 0.18f, len = r.width * 0.2f, th = Mathf.Max(2.5f, r.width * 0.07f);
            DrawCross(new Vector2(r.center.x - dx, eyeY), len, th, Ink);
            DrawCross(new Vector2(r.center.x + dx, eyeY), len, th, Ink);
        }

        // A small "✕" eye: two rounded bars crossed at ±45°.
        private static void DrawCross(Vector2 c, float len, float thick, Color col)
        {
            var bar = new Rect(c.x - len * 0.5f, c.y - thick * 0.5f, len, thick);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);  Fill(bar, col, thick * 0.5f); GUI.matrix = m;
            GUIUtility.RotateAroundPivot(-45f, c); Fill(bar, col, thick * 0.5f); GUI.matrix = m;
        }

        // A single diagonal "off" slash through an icon (the universal muted/disabled
        // stroke) — used to cross out the music note when music is disabled, mirroring
        // the speaker's 🔇. A dark casing keeps it crisp over both the glyph and the bg.
        private static void DrawSlash(Vector2 c, float len, float thick, Color col)
        {
            var bar = new Rect(c.x - len * 0.5f, c.y - thick * 0.5f, len, thick);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);
            Fill(bar, new Color(0f, 0f, 0f, 0.55f), thick); // casing
            Fill(new Rect(bar.x, bar.y + thick * 0.25f, bar.width, thick * 0.5f), col, thick * 0.25f);
            GUI.matrix = m;
        }

        // Render the player's currently-equipped player (real sprite art if available, else a
        // colored player), with a ground shadow and a gentle idle bob — so you see your player
        // on the home screen while you wait to start.
        private void DrawPlayerAvatar(Rect box, PlayerProfile prof)
        {
            string id = prof?.characterId ?? "avo";
            Fill(new Rect(box.center.x - box.width * 0.34f, box.yMax - 6f, box.width * 0.68f, 12f), new Color(0, 0, 0, 0.30f), 6f);
            float bob = Mathf.Sin(Time.unscaledTime * 1.7f) * 3f;
            var pr = new Rect(box.x, box.y + bob, box.width, box.height);
            var thumb = CharacterPreview.Get(id);
            if (thumb.Has) DrawThumb(pr, thumb, 1f);
            else
            {
                var body = Palette.Body(id);
                Fill(new Rect(pr.x + pr.width * 0.12f, pr.y + pr.height * 0.12f, pr.width * 0.76f, pr.height * 0.76f), body, pr.width * 0.4f);
            }

            if (prof != null) DrawWornAccessories(pr, prof, thumb); // equipped cosmetics on the home player too
        }

        // Ghost button whose icon is the brand player (pink mascot) instead of a flat emoji.
        private bool PlayerButton(Rect r, string label)
        {
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            Fill(r, new Color(1, 1, 1, hover ? 0.09f : 0.05f), 14f);
            Stroke(r, hover ? LineBright : Line, 2f, 14f);
            var st = new GUIStyle(_ui) { alignment = TextAnchor.MiddleLeft, fontSize = 14, normal = { textColor = Ink } };
            float tw = st.CalcSize(new GUIContent(label)).x, icon = 18f, gap = 7f;
            float gx = r.center.x - (icon + gap + tw) / 2f;
            DrawBrandPlayer(new Rect(gx, r.center.y - icon / 2f, icon, icon));
            Lbl(new Rect(gx + icon + gap, r.y, tw + 6f, r.height), label, st);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // The brand mascot: a pink player with a highlight and two little eyes.
        private void DrawBrandPlayer(Rect r)
        {
            Fill(r, Pink, r.width * 0.42f); // body (near-circle)
            Fill(new Rect(r.x + r.width * 0.52f, r.y + r.height * 0.14f, r.width * 0.24f, r.height * 0.24f), new Color(1, 1, 1, 0.5f), r.width * 0.12f); // shine
            float es = r.width * 0.16f, ey = r.y + r.height * 0.44f;
            var eye = new Color(0.05f, 0.02f, 0.06f, 0.92f);
            Fill(new Rect(r.x + r.width * 0.34f - es / 2f, ey, es, es * 1.25f), eye, es * 0.5f);
            Fill(new Rect(r.x + r.width * 0.60f - es / 2f, ey, es, es * 1.25f), eye, es * 0.5f);
        }

        #endregion

        private void Update()
        {
            var phase = _router != null && _router.HasSeries ? _router.Phase : RoomPhase.Lobby;
            if (phase != _lastPhase)
            {
                OnPhaseChanged(_lastPhase, phase);
                _lastPhase = phase;
            }

            // Fire a reveal that was held behind a round-end casualty call, once she's done.
            if (_revealPending && Time.time >= _revealAt)
            {
                _revealPending = false;
                if (_router != null && _router.HasSeries && _router.CurrentGame != null) PlayReveal();
            }

            UpdateMusic();                 // pick the background loop for the current screen/phase
            ScreenFx.Tick(Time.deltaTime); // decay screen juice (shake/flash)
            UpdateCaptions();              // refresh subtitle cues from the live snapshot
            CaptureRebind();
        }

        // The single music director: choose the background loop for the current screen/phase and
        // hand it to AudioService (SetMusic no-ops if it's already the active loop). Mapping:
        //   main menu / lobby / regular rounds → music_sinister
        //   Mingle & Musical Chairs            → music_danube
        //   the final game                     → music_creepy
        //   post-game (series result)          → music_accralate
        // Exception: in the EDITOR only, the lobby/menu ROTATES between Sinister and Pink Soldiers
        // (local-dev flavor). Pink Soldiers is unlicensed/editor-only, so a real build always uses
        // the fixed track above.
        // The rotation advances per clip length, and Pink Soldiers (~28s seamless loop) is less than
        // half the length of Sinister (~64s) — so a single pass felt jarringly brief next to it. Pink
        // Soldiers is authored to loop seamlessly, so we list it twice to give it ~56s of airtime,
        // balancing the two tracks in the cycle.
        private static readonly string[] LobbyDevPlaylist = { "music_sinister", "music_dev", "music_dev" };

        private void UpdateMusic()
        {
            var audio = AudioService.Instance;
            if (audio == null) return;
            bool frontOfHouse = _router == null || !_router.HasSeries || _router.Phase == RoomPhase.Lobby;
            if (frontOfHouse && Application.isEditor) audio.SetMusicPlaylist(LobbyDevPlaylist);
            else
            {
                // music_danube is the Mingle / Musical Chairs MECHANIC music (you act when it
                // stops), so it's "game sound": rides the game-sound volume with SFX + announcer
                // and keeps playing when 🎵 mutes music. Every other loop — menu, lobby, regular
                // rounds, the final, results — is background music on the music volume/toggle.
                var track = DesiredMusicTrack();
                audio.SetMusic(track, gameMusic: track == "music_danube");
            }
        }

        private string DesiredMusicTrack()
        {
            // No live series → the front-of-house menu screens.
            if (_router == null || !_router.HasSeries) return "music_sinister";
            switch (_router.Phase)
            {
                case RoomPhase.SeriesResult:  // post-game results / champion screen
                    return "music_accralate";
                case RoomPhase.Lobby:         // pre-start lobby reads like the menu
                    return "music_sinister";
                default:                      // Intro / Playing / RoundResult → by the round's game
                    var g = _router.CurrentGame;
                    if (g == GameId.Mingle || g == GameId.MusicalChairs) return "music_danube";
                    if (_router.IsFinalGame) return "music_creepy";
                    return "music_sinister";
            }
        }

        // While a binding row is "listening", grab the next key pressed (Esc cancels).
        private void CaptureRebind()
        {
            if (_rebindIndex < 0) return;
            var s = SaveService.Current?.settings;
            var kb = Keyboard.current;
            if (s == null || kb == null) { _rebindIndex = -1; return; }
            if (kb.escapeKey.wasPressedThisFrame) { _rebindIndex = -1; return; }
            foreach (var key in kb.allKeys)
            {
                if (!key.wasPressedThisFrame) continue;
                var k = key.keyCode;
                if (k == Key.None || k == Key.Escape) continue;
                SetBind(s, _rebindIndex, k);
                SaveService.Save();
                _rebindIndex = -1;
                break;
            }
        }

        // A property (not a static field) so the labels track the live locale rather
        // than freezing at the load-time language.
        private static string[] BindLabels => new[]
        {
            Loc.Get("ctrl.move_up"), Loc.Get("ctrl.move_down"), Loc.Get("ctrl.move_left"),
            Loc.Get("ctrl.move_right"), Loc.Get("ctrl.action"), Loc.Get("ctrl.dash")
        };
        private static Key GetBind(GameSettings s, int i) => i switch
        { 0 => s.keyUp, 1 => s.keyDown, 2 => s.keyLeft, 3 => s.keyRight, 4 => s.keyAction, 5 => s.keyDash, _ => Key.None };
        private static void SetBind(GameSettings s, int i, Key k)
        {
            switch (i)
            {
                case 0: s.keyUp = k; break; case 1: s.keyDown = k; break; case 2: s.keyLeft = k; break;
                case 3: s.keyRight = k; break; case 4: s.keyAction = k; break; case 5: s.keyDash = k; break;
            }
        }

        private void OnPhaseChanged(RoomPhase from, RoomPhase to)
        {
            // Leaving play (round over) — read out a hunt/attrition game's banked casualties.
            if (from == RoomPhase.Playing) FlushDeferredEliminations();

            // A series just (re)started — re-arm marble banking for it. Covers both
            // local hosting and an online host pressing Start (Lobby → Intro).
            if (from == RoomPhase.Lobby && to != RoomPhase.Lobby) _seriesBanked = false;

            switch (to)
            {
                case RoomPhase.Intro:
                    // The reveal waits its turn behind a round-end casualty call so the female
                    // announcer is never cut off mid-readout (Update fires it when she's done).
                    if (_router.CurrentGame != null)
                    {
                        if (Time.time < _announcerBusyUntil) { _revealPending = true; _revealAt = _announcerBusyUntil; }
                        else PlayReveal();
                    }
                    break;
                case RoomPhase.SeriesResult:
                    BankMarbles();
                    var champ = _router.ChampionId;
                    if (!string.IsNullOrEmpty(champ)) Caption(Loc.Get("gm.champion", _router.NameOf(champ)), 8f);
                    AudioService.Instance?.Play("win");
                    if (champ != null && _router.LocalPlayerIds.Contains(champ)) // a local player won the series
                    {
                        SteamService.Instance?.Unlock("SERIES_WIN");
                        SteamService.Instance?.SubmitLeaderboard("marbles", SaveService.Current?.marbles ?? 0);
                    }
                    SteamService.Instance?.Unlock("FIRST_SERIES"); // played a full series
                    break;
            }
        }

        private void Caption(string text, float seconds)
        {
            _caption = text;
            _captionUntil = Time.time + seconds;
        }

        // Derive subtitles from the live snapshot: Game-Master cues + eliminations.
        // Called from Update (OnGUI can run several times per frame, so cue/elimination
        // detection lives here to fire each caption exactly once).
        // (DrawCaption still gates display on the subtitles setting; we always track
        // state so a mid-round toggle doesn't replay stale cues.)
        private void UpdateCaptions()
        {
            bool playing = _router != null && _router.HasSeries && _router.Phase == RoomPhase.Playing;
            var snap = playing ? _router.Latest : null;
            if (snap?.Actors == null)
            {
                _aliveSeen.Clear();
                _capGame = null; _capSimon = null; _capRed = false;
                return;
            }

            // First frame of a (new) game: seed alive-set + cue state without
            // captioning the initial population.
            if (_capGame != snap.Game)
            {
                _capGame = snap.Game;
                _capSimon = null; _capRed = false;
                _aliveSeen.Clear();
                foreach (var a in snap.Actors) if (a.Alive) _aliveSeen.Add(a.Id);
                return;
            }

            DetectEliminations(snap);
            DetectGameCues(snap);
        }

        // Caption players that were alive last frame and aren't now. Local-player
        // deaths are called out personally; mass wipes are summarized.
        private void DetectEliminations(Snapshot snap)
        {
            List<string> outNow = null;
            var alive = new HashSet<string>();
            foreach (var a in snap.Actors) if (a.Alive) alive.Add(a.Id);
            foreach (var id in _aliveSeen)
                if (!alive.Contains(id)) (outNow ??= new List<string>()).Add(id);
            _aliveSeen.Clear();
            foreach (var id in alive) _aliveSeen.Add(id);
            if (outNow == null) return;

            var locals = _router.LocalPlayerIds;
            string localOutId = locals == null ? null : outNow.FirstOrDefault(id => locals.Contains(id));
            bool localOut = localOutId != null;
            if (localOut)
                Caption(Loc.Get("gm.eliminated_you"), 2.6f);
            else if (outNow.Count == 1)
                Caption(Loc.Get("gm.eliminated_one", _router.NameOf(outNow[0])), 2.4f);
            else
                Caption(Loc.Get("gm.eliminated_many", outNow.Count), 2.4f);
            // Hunt/attrition games hold the voice and bank the tags for one round-end call
            // (see DeferElimGames / FlushDeferredEliminations); captions above still fire now.
            if (DeferElimGames.Contains(snap.Game))
            {
                foreach (var id in outNow) { int n = NumberInSnap(snap, id); if (n > 0) _deferredOut.Add(n); }
                return;
            }
            // Female announcer calls the fallen out BY NUMBER (digit by digit) — the tag over
            // their head and on your HUD is the one you hear. One out → "Player five seven three
            // has been eliminated."; a same-tick wipe enumerates them. Your own death jumps the
            // queue; otherwise the next call waits until this whole line FINISHES (its real
            // spoken length, returned by the announcer) plus a breath — so she's never cut off.
            if (localOut || Time.time >= _elimVoiceUntil)
            {
                float dur;
                if (outNow.Count == 1)
                    dur = Announcer.EliminatedByNumber(NumberInSnap(snap, outNow[0]));
                else
                {
                    var nums = new List<int>(outNow.Count);
                    foreach (var id in outNow) nums.Add(NumberInSnap(snap, id));
                    dur = Announcer.EliminatedMultiple(nums);
                }
                _elimVoiceUntil = Time.time + (dur > 0f ? dur : 2f) + 0.5f;
                if (dur > 0f) _announcerBusyUntil = Time.time + dur + 0.3f; // hold a reveal at the buzzer
            }
        }

        // The eliminated actor lingers in the snapshot (it becomes a coffin), so its
        // lobby Number is read straight from there. 0 if not found → generic fallback.
        private static int NumberInSnap(Snapshot snap, string id)
        {
            if (snap?.Actors != null)
                foreach (var a in snap.Actors) if (a.Id == id) return a.Number;
            return 0;
        }

        // Round over in a deferred game: read the whole round's casualties in one call
        // ("Players five seven three, one two … have been eliminated."). Marks the announcer
        // busy for the line's full length so the next round's reveal holds until she finishes.
        // Called when leaving Playing, so it never carries over.
        private void FlushDeferredEliminations()
        {
            if (_deferredOut.Count == 0) return;
            float dur = _deferredOut.Count == 1
                ? Announcer.EliminatedByNumber(_deferredOut[0])
                : Announcer.EliminatedMultiple(_deferredOut);
            _deferredOut.Clear();
            if (dur > 0f) _announcerBusyUntil = Time.time + dur + 0.3f;
        }

        // The ceremonial round reveal ("Attention, players. Game three. Tug of war. The arena,
        // Neon District."), caption + matching male voice. Held behind a round-end casualty
        // call (see OnPhaseChanged / Update) so the two announcers never talk over each other.
        private void PlayReveal()
        {
            if (_router.CurrentGame == null) return;
            string roomTheme = ArenaThemes.ForRound(_router.RoundIndex, _router.CurrentGame);
            Caption(Loc.Get("gm.game_intro", _router.RoundIndex + 1, GameName(_router.CurrentGame.Value))
                    + "  " + Loc.Get("gm.room_intro", ArenaThemes.DisplayName(roomTheme)), 7f);
            Announcer.Game(_router.RoundIndex + 1, _router.CurrentGame.Value, _router.IsFinalGame, roomTheme);
        }

        // Caption the marquee Game-Master cues that map to audio/visual signals a
        // distracted or deaf player might miss: Red/Green Light flips and Simon's
        // orders. The same per-game pattern extends to other games' cues.
        private void DetectGameCues(Snapshot snap)
        {
            switch (snap.Data)
            {
                case RedLightGreenLight.RlglData rl:
                    if (rl.Red != _capRed)
                    {
                        _capRed = rl.Red;
                        Caption(Loc.Get(rl.Red ? "gm.red_light" : "gm.green_light"), 2.0f);
                    }
                    break;
                case SimonSays.SimonData simon:
                    if (simon.Command != _capSimon)
                    {
                        _capSimon = simon.Command;
                        if (!string.IsNullOrEmpty(simon.Command))
                        {
                            Caption(simon.Freeze || simon.Command == "freeze"
                                ? Loc.Get("gm.simon_freeze")
                                : Loc.Get("gm.simon_says", Loc.Get("simon." + simon.Command)), 2.2f);
                            // Male announcer barks the order ("Simon says, blink." / "Freeze!").
                            Announcer.Simon(simon.Command, simon.Freeze);
                        }
                    }
                    break;
            }
        }

        // A brief confirmation pill (e.g. "Saved"), shown over whatever page is up.
        // Unlike Caption it ignores the subtitles setting, since it's UI feedback.
        private void Toast(string text, float seconds = 2.2f)
        {
            _toast = text;
            _toastUntil = Time.time + seconds;
        }

        private void BankMarbles()
        {
            if (_seriesBanked) return;
            // Source-agnostic: the local player's final standing carries the same
            // earned marbles / rounds as the room (verified against GameRoom). For
            // co-op only P1 (LocalPlayerIds[0]) banks to the saved profile, as before.
            var sr = _router.SeriesResult;
            var ids = _router.LocalPlayerIds;
            if (sr != null && ids.Count > 0 && SaveService.Current != null)
            {
                string localId = ids[0];
                var st = sr.Standings.FirstOrDefault(s => s.PlayerId == localId);
                if (st != null)
                {
                    SaveService.Current.marbles += st.Marbles;
                    if (sr.ChampionId == localId) SaveService.Current.seriesWon += 1;
                    SaveService.Current.roundsSurvived += st.RoundsSurvived;
                    SaveService.Save();
                }
            }
            _seriesBanked = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            float s = Mathf.Max(1f, Screen.height / 900f);
            GUIUtility.ScaleAroundPivot(new Vector2(s, s), Vector2.zero);

            float w = Screen.width / s, h = Screen.height / s;

            // A live round/result owns the screen (any phase past the lobby). The
            // online lobby (HasSeries but still Lobby phase) stays front-of-house so
            // the host/join page can show the room while we wait to start.
            bool inGame = _router != null && _router.HasSeries && _router.Phase != RoomPhase.Lobby;
            if (!inGame)
            {
                // Re-entering a scrollable page should start at the top, not wherever it
                // was left scrolled to last time. Reset offsets on the entry transition.
                if (_page != _prevPage)
                {
                    switch (_page)
                    {
                        case Page.HowToPlay: _htpScroll = Vector2.zero; break;
                        case Page.PatchNotes: _patchScroll = Vector2.zero; break;
                        case Page.Credits: _creditsScroll = Vector2.zero; break;
                        case Page.Players: _playerScroll = Vector2.zero; break;
                        case Page.Settings: _setScroll = Vector2.zero; break;
                        // Re-seed the editable name field from the saved profile on
                        // every entry, so the text box always shows the current name
                        // (the lazy `== null` init below only ever fires once and can
                        // latch onto a stale/default value before the profile loads).
                        case Page.Account: _accScroll = Vector2.zero; _nameEdit = SaveService.Current?.name; break;
                    }
                    _prevPage = _page;
                }
                switch (_page)
                {
                    case Page.Settings: DrawSettings(w, h); break;
                    case Page.Leaderboard: DrawLeaderboard(w, h); break;
                    case Page.HowToPlay: DrawHowToPlay(w, h); break;
                    case Page.PatchNotes: DrawPatchNotes(w, h); break;
                    case Page.Account: DrawAccount(w, h); break;
                    case Page.Controls: DrawControls(w, h); break;
                    case Page.Credits: DrawCredits(w, h); break;
                    case Page.Players: DrawPlayers(w, h); break;
                    case Page.Online: DrawOnline(w, h); break;
                    default: DrawMenu(w, h); break;
                }
                DrawFlash(w, h);
                DrawToast(w, h);
                return;
            }

            switch (_router.Phase)
            {
                case RoomPhase.Intro: DrawIntro(w, h); break;
                case RoomPhase.Playing: DrawHud(w, h); break;
                case RoomPhase.RoundResult: DrawRoundResult(w, h); break;
                case RoomPhase.SeriesResult: DrawSeriesResult(w, h); break;
            }
            DrawFlash(w, h);
            DrawCaption(w, h);
            DrawToast(w, h);
            DrawAudioToggles(w, h);
        }

        // Full-screen accessibility-gated flash (ScreenFx). Alpha is 0 while idle, so
        // this is a no-op most frames; "Reduce flashing & screen shake" keeps it 0.
        private void DrawFlash(float w, float h)
        {
            var c = ScreenFx.FlashColor();
            if (c.a <= 0.001f) return;
            var bg = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = bg;
        }

        private static string GameName(GameId id) => Loc.Get("game." + id);

        private void DrawMenu(float w, float h)
        {
            DrawThemeBackdrop(w, h);
            DrawFloatingPlayers(w, h);
            DrawNavBar(w);

            DrawWordmark(w, h * 0.11f, Loc.Get("ui.title"));
            Lbl(new Rect(0, h * 0.225f, w, 30), MenuContent.Tagline,
                new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontSize = 19, normal = { textColor = InkDim } });

            // Frosted central card (the web ".panel"). Height hugs the active step.
            float ph = _playStep == PlayStep.Mode ? 426f : _playStep == PlayStep.Difficulty ? 396f : _playStep == PlayStep.Lobby ? 414f : 300f;
            float pw = 470f, px = (w - pw) / 2f, py = h * 0.30f;
            var panel = new Rect(px, py, pw, ph);
            Fill(new Rect(panel.x, panel.y + 12f, panel.width, panel.height), new Color(0, 0, 0, 0.35f), 24f);
            Fill(panel, Panel, 24f);
            Stroke(panel, Line, 2f, 24f);

            float pad = 30f, innerW = pw - pad * 2f, cx = px + pad, y = py + 18f;

            // Player card: your equipped player rendered, with its name, your Marbles, and the
            // "Your Player" customise button — grouped together while you wait to start.
            var prof = SaveService.Current;
            const float av = 84f;
            DrawPlayerAvatar(new Rect(cx, y, av, av), prof);
            float rx = cx + av + 16f, rw = innerW - av - 16f;
            string charName = Cosmetics.Characters.FirstOrDefault(c => c.Id == (prof?.characterId ?? "avo")).Name ?? Loc.Get("ui.your_player");
            Lbl(new Rect(rx, y, rw, 26f), charName,
                new GUIStyle(_ui) { fontSize = 18, alignment = TextAnchor.MiddleLeft, normal = { textColor = Ink } });
            MarblesPill(new Rect(rx, y + 32f, rw, 30f), prof?.marbles ?? 0);
            if (PlayerButton(new Rect(rx, y + 68f, rw, 30f), Loc.Get("ui.your_player")))
                _page = Page.Players;
            y += 116f;

            switch (_playStep)
            {
                case PlayStep.Mode: DrawPlayModeStep(cx, innerW, y); break;
                case PlayStep.Difficulty: DrawPlayDifficultyStep(cx, innerW, y); break;
                case PlayStep.Lobby: DrawPlayLobbyStep(cx, innerW, y); break;
                default: DrawPlayHomeStep(cx, innerW, y); break;
            }

            Lbl(new Rect(0, h * 0.92f, w, 22), MenuContent.FooterLine,
                new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, fontSize = 13, normal = { textColor = Alpha(InkDim, 0.75f) } });
        }

        // Home step: one Play button (opens the mode picker) + Settings / Quit.
        private void DrawPlayHomeStep(float cx, float innerW, float y)
        {
            if (Btn(new Rect(cx, y, innerW, 64f), Loc.Get("ui.play"), Green, OnDark))
                _playStep = PlayStep.Mode;
            y += 76f;
            float half = (innerW - 14f) / 2f;
            if (GhostBtn(new Rect(cx, y, half, 42f), Loc.Get("ui.settings")))
                _page = Page.Settings;
            if (GhostBtn(new Rect(cx + half + 14f, y, half, 42f), Loc.Get("ui.quit")))
                Application.Quit();
        }

        // Mode step: Online (first), Solo vs Bots, Local Co-op (gamepad-gated).
        private void DrawPlayModeStep(float cx, float innerW, float y)
        {
            StepLabel(cx, innerW, y, Loc.Get("ui.choose_how")); y += 26f;
            if (Btn(new Rect(cx, y, innerW, 52f), Loc.Get("ui.play_online"), Teal, OnDark))
                { _playKind = PlayKind.Online; _playStep = PlayStep.Difficulty; }
            y += 60f;
            if (Btn(new Rect(cx, y, innerW, 52f), Loc.Get("ui.solo_vs_bots"), Green, OnDark))
                { _playKind = PlayKind.Solo; _playStep = PlayStep.Difficulty; }
            y += 60f;
            int pads = Gamepad.all.Count;
            bool coop = pads > 0;
            string coopLabel = coop ? Loc.Get("ui.local_coop_n", 1 + pads) : Loc.Get("ui.local_coop");
            if (Btn(new Rect(cx, y, innerW, 52f), coopLabel, Yellow, OnGold, coop))
                { _playKind = PlayKind.Coop; _playStep = PlayStep.Difficulty; }
            y += 54f;
            if (!coop)
                Lbl(new Rect(cx, y, innerW, 20f), Loc.Get("ui.coop_needs_gamepad"),
                    new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Italic, normal = { textColor = InkDim } });
            y += 22f;
            if (GhostBtn(new Rect(cx, y, innerW, 40f), Loc.Get("ui.back"))) _playStep = PlayStep.None;
        }

        // Difficulty step: Casual / Hardcore — applies to whichever mode was chosen.
        private void DrawPlayDifficultyStep(float cx, float innerW, float y)
        {
            string kind = _playKind == PlayKind.Online ? Loc.Get("ui.play_online") : _playKind == PlayKind.Solo ? Loc.Get("ui.solo_vs_bots") : Loc.Get("ui.local_coop");
            StepLabel(cx, innerW, y, Loc.Get("ui.choose_difficulty", kind.ToUpper())); y += 30f;
            if (Btn(new Rect(cx, y, innerW, 64f), Loc.Get("ui.casual"), Green, OnDark))
                ChooseDifficulty(SeriesMode.Casual);
            y += 72f;
            if (Btn(new Rect(cx, y, innerW, 64f), Loc.Get("ui.hardcore"), Pink, OnDark))
                ChooseDifficulty(SeriesMode.Hardcore);
            y += 72f;
            if (GhostBtn(new Rect(cx, y, innerW, 40f), Loc.Get("ui.back"))) _playStep = PlayStep.Mode;
        }

        // Online opens its own networked lobby; solo/co-op go to the local match-setup
        // step so the player can pick the contestant count and round count first.
        private void ChooseDifficulty(SeriesMode mode)
        {
            if (_playKind == PlayKind.Online) { StartChosen(mode); return; }
            _lobbyMode = mode;
            _playStep = PlayStep.Lobby;
        }

        // The solo/co-op "lobby": choose number of contestants (you + bots) and rounds.
        private void DrawPlayLobbyStep(float cx, float innerW, float y)
        {
            StepLabel(cx, innerW, y, "MATCH SETUP"); y += 24f;

            Lbl(new Rect(cx, y, innerW, 18), "Contestants (you + bots)",
                new GUIStyle(_body) { fontSize = 12, normal = { textColor = InkDim } }); y += 22f;
            int[] fields = { 4, 6, 8, 12 };
            float fw = (innerW - 3 * 8f) / 4f;
            for (int i = 0; i < fields.Length; i++)
            {
                bool sel = _lobbyField == fields[i];
                if (Btn(new Rect(cx + i * (fw + 8f), y, fw, 44f), fields[i].ToString(), sel ? Yellow : Teal, sel ? OnGold : OnDark, true, 19))
                    _lobbyField = fields[i];
            }
            y += 50f;

            Lbl(new Rect(cx, y, innerW, 18), "Rounds  (★ = mystery)",
                new GUIStyle(_body) { fontSize = 12, normal = { textColor = InkDim } }); y += 22f;
            string[] rl = { "3", "4", "5", "7", "★" };
            int[] rv = { 3, 4, 5, 7, 0 };
            float rw = (innerW - 4 * 8f) / 5f;
            for (int i = 0; i < rl.Length; i++)
            {
                bool sel = rv[i] == 0 ? _lobbyMystery : (!_lobbyMystery && _lobbyRounds == rv[i]);
                if (Btn(new Rect(cx + i * (rw + 8f), y, rw, 44f), rl[i], sel ? Yellow : Teal, sel ? OnGold : OnDark, true, 19))
                {
                    if (rv[i] == 0) _lobbyMystery = true;
                    else { _lobbyMystery = false; _lobbyRounds = rv[i]; }
                }
            }
            y += 50f;

            if (Btn(new Rect(cx, y, innerW, 52f), "▶  START", Green, OnDark))
            {
                _playStep = PlayStep.None;
                var rounds = _lobbyMystery ? RoundsMode.AsMystery() : RoundsMode.Fixed(_lobbyRounds);
                if (_playKind == PlayKind.Coop) StartCoop(_lobbyMode, rounds, _lobbyField);
                else StartSolo(_lobbyMode, rounds, _lobbyField);
            }
            y += 56f;
            if (GhostBtn(new Rect(cx, y, innerW, 38f), Loc.Get("ui.back"))) _playStep = PlayStep.Difficulty;
        }

        private void StepLabel(float cx, float innerW, float y, string text)
            => Lbl(new Rect(cx, y - 2, innerW, 24), text,
                new GUIStyle(_ui) { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = InkDim } });

        // Fire the chosen (mode × difficulty): local starts immediately; online opens
        // the lobby with the difficulty pre-set for hosting.
        private void StartChosen(SeriesMode mode)
        {
            _playStep = PlayStep.None;
            switch (_playKind)
            {
                case PlayKind.Online: _onlineHardcore = mode == SeriesMode.Hardcore; OpenOnline(); break;
                case PlayKind.Solo: StartSolo(mode, RoundsMode.Fixed(4), 12); break;     // fallback; solo normally routes via the lobby
                case PlayKind.Coop: StartCoop(mode, RoundsMode.Fixed(4), 12); break;
            }
        }

        // Centered row of nav pills (the web top bar): Leaderboard · How to Play · Patch Notes · Account · Mute · Music.
        private void DrawNavBar(float w)
        {
            var steam = SteamService.Instance;
            string steamName = steam != null ? steam.PlayerName : null;
            string acct = steamName ?? SaveService.Current?.name ?? "Account";
            if (acct.Length > 14) acct = acct.Substring(0, 13) + "…";

            var labels = new[] { "🏆 " + Loc.Get("nav.leaderboard"), "❔ " + Loc.Get("nav.how_to_play"), "📓 " + Loc.Get("nav.patch_notes"), "💾 " + acct };
            var pages = new[] { Page.Leaderboard, Page.HowToPlay, Page.PatchNotes, Page.Account };
            var widths = new[] { 150f, 150f, 140f, 130f };
            float gap = 10f, muteW = 48f, musicW = 48f, ph = 36f, y = 26f;
            float total = muteW + gap + musicW + gap * labels.Length;
            foreach (var ww in widths) total += ww;

            float px = (w - total) / 2f;
            for (int i = 0; i < labels.Length; i++)
            {
                if (Pill(new Rect(px, y, widths[i], ph), labels[i])) _page = pages[i];
                px += widths[i] + gap;
            }
            DrawMuteAllButton(new Rect(px, y, muteW, ph));
            px += muteW + gap;
            DrawMusicButton(new Rect(px, y, musicW, ph));
        }

        // Mute-all speaker toggle. Mute-all IS master-volume-at-zero (one source of truth
        // shared with the Settings slider), so muting here and the slider there can never
        // disagree. Silences EVERYTHING — SFX, announcer, and both music buckets. Shared
        // by the nav bar and the in-game corner controls.
        private void DrawMuteAllButton(Rect r)
        {
            var snd = SaveService.Current?.settings;
            bool muted = snd != null && snd.masterVolume <= 0f;
            if (AudioButton(r, muted ? "🔇" : "🔊", !muted) && snd != null)
            {
                if (snd.masterVolume > 0f) { _premuteVolume = snd.masterVolume; snd.masterVolume = 0f; }
                else snd.masterVolume = _premuteVolume > 0f ? _premuteVolume : 1f;
                AudioListener.volume = snd.masterVolume;
                SaveService.Save();
            }
        }

        // Background-music-only switch, independent of the mute-all and of game SFX —
        // and of GAMEPLAY music (Mingle / Musical Chairs danube), which is a game cue
        // and keeps playing while this is off. A red slash crosses the note when music
        // is off, the way the speaker shows 🔇. Shares SetMusicEnabled with the Settings
        // toggle so every control always agrees.
        private void DrawMusicButton(Rect r)
        {
            var snd = SaveService.Current?.settings;
            bool musicOn = snd == null || snd.musicEnabled;
            bool musicClick = AudioButton(r, "🎵", musicOn);
            if (!musicOn) DrawSlash(r.center, 30f, 6f, Red);
            if (musicClick && snd != null) SetMusicEnabled(!musicOn);
        }

        // Flip the music loop on/off (master volume and game SFX untouched) and apply +
        // persist immediately. Shared by the Settings toggle and the in-game HUD button
        // so the two controls can never disagree.
        private void SetMusicEnabled(bool on)
        {
            var s = SaveService.Current?.settings;
            if (s == null || s.musicEnabled == on) return;
            s.musicEnabled = on;
            AudioService.Instance?.ApplyVolumes();
            SaveService.Save();
        }

        // Solid-pink audio toggle (the mute-all + music switches share this look). Stands
        // out over both the menu backdrop and the dark arena/intro screens, where the faint
        // 5%-white nav Pill left the glyph nearly invisible. `on` fills hot-pink; when off
        // the fill dims — the caller swaps the glyph (🔊→🔇) and/or adds a red slash so the
        // off-state still reads. 48×36 with a soft drop shadow for depth on dark.
        private bool AudioButton(Rect r, string glyph, bool on)
        {
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            float rad = r.height / 2f;
            Fill(new Rect(r.x, r.y + 4f, r.width, r.height), Alpha(Color.black, 0.30f), rad); // drop shadow
            Fill(r, on ? (hover ? Lighter(Pink, 0.10f) : Pink) : Alpha(Pink, 0.32f), rad);
            Stroke(r, on ? Alpha(Color.white, 0.30f) : Alpha(Pink, 0.55f), 1.5f, rad);
            Lbl(r, glyph, new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 16,
                normal = { textColor = on ? Color.white : Alpha(Ink, 0.7f) } });
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // In-game audio switches (bottom-left): the mute-all speaker (everything) and the
        // background-music note (music loop only — gameplay music + SFX stay on). Drawn on
        // every in-game phase so players can mute without leaving the round; mirror the
        // nav/Settings controls via the shared helpers. They float alone over the dark
        // arena/intro screens, so they use the solid-pink AudioButton.
        private void DrawAudioToggles(float w, float h)
        {
            if (SaveService.Current?.settings == null) return;
            DrawMuteAllButton(new Rect(12f, h - 50f, 48f, 36f));
            DrawMusicButton(new Rect(70f, h - 50f, 48f, 36f));
        }

        // Draw a wrapped paragraph and return the y just below it.
        private static float Para(float x, float y, float wdt, string text, GUIStyle st)
        {
            float hgt = st.CalcHeight(new GUIContent(text), wdt);
            Lbl(new Rect(x, y, wdt, hgt), text, st);
            return y + hgt;
        }

        // ============================ LEADERBOARD ============================
        private void DrawLeaderboard(float w, float h)
        {
            DrawSubHeader(w, h, MenuContent.LeaderboardTitle, MenuContent.LeaderboardSubtitle);
            var prof = SaveService.Current;
            var steam = SteamService.Instance;
            int marbles = prof?.marbles ?? 0, crowns = prof?.seriesWon ?? 0;
            string who = steam?.PlayerName ?? prof?.name ?? "You";
            string title = MenuContent.TitleFor(marbles, crowns);

            float pw = 780f, px = (w - pw) / 2f, py = 150f;
            var panel = new Rect(px, py, pw, 372f);
            Fill(panel, Panel, 22f); Stroke(panel, Line, 2f, 22f);

            float rowX = px + 28f, rowW = pw - 56f, y = py + 20f;
            var hd = new GUIStyle(_ui) { fontSize = 12, normal = { textColor = InkDim } };
            Lbl(new Rect(rowX, y, 40, 22), "#", hd);
            Lbl(new Rect(rowX + 56, y, 200, 22), Loc.Get("acct.col_player"), hd);
            Lbl(new Rect(rowX + 270, y, 240, 22), Loc.Get("acct.col_title"), hd);
            Lbl(new Rect(rowX + 540, y, 80, 22), Loc.Get("acct.col_crowns"), hd);
            Lbl(new Rect(rowX + rowW - 90, y, 90, 22), "◍", new GUIStyle(hd) { alignment = TextAnchor.MiddleRight });
            y += 28f;
            Fill(new Rect(rowX, y, rowW, 1f), Line, 0f); y += 14f;

            var row = new Rect(rowX - 8f, y - 4f, rowW + 16f, 46f);
            Fill(row, Alpha(Pink, 0.12f), 12f); Stroke(row, LineBright, 1.5f, 12f);
            Lbl(new Rect(rowX, y + 2, 44, 36), "🥇", new GUIStyle(_h2) { fontSize = 22 });
            Lbl(new Rect(rowX + 56, y + 4, 210, 34), who, new GUIStyle(_ui) { fontSize = 18, normal = { textColor = Ink } });
            Lbl(new Rect(rowX + 270, y + 6, 260, 34), "“" + title + "”", new GUIStyle(_body) { fontSize = 15, normal = { textColor = InkDim } });
            Lbl(new Rect(rowX + 540, y + 4, 80, 34), crowns.ToString(), new GUIStyle(_ui) { fontSize = 18, normal = { textColor = Ink } });
            Lbl(new Rect(rowX + rowW - 120, y + 4, 120, 34), marbles.ToString(), new GUIStyle(_ui) { alignment = TextAnchor.MiddleRight, fontSize = 18, normal = { textColor = Yellow } });
            y += 64f;

            Lbl(new Rect(rowX, y, rowW, 50), MenuContent.LeaderboardEmpty,
                new GUIStyle(_body) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Italic, normal = { textColor = InkDim } });

            string note = (steam != null && steam.PlayerName != null)
                ? Loc.Get("acct.steam_submit")
                : Loc.Get("acct.steam_global");
            Lbl(new Rect(rowX, py + 330f, rowW, 24), "🏆 " + note,
                new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontSize = 13, normal = { textColor = Alpha(InkDim, 0.8f) } });
        }

        // ============================ HOW TO PLAY ============================
        private void DrawHowToPlay(float w, float h)
        {
            DrawSubHeader(w, h, MenuContent.HowToPlayTitle, MenuContent.HowToPlaySubtitle);
            float vx = 40f, vtop = 138f, vw = w - 80f, vh = h - vtop - 26f, cw = vw - 18f;

            var bodyP = new GUIStyle(_body) { fontSize = 15, wordWrap = true, normal = { textColor = Ink } };
            float introH = bodyP.CalcHeight(new GUIContent(MenuContent.HowToPlayIntro), cw);

            var ids = new List<GameId>();
            foreach (GameId id in System.Enum.GetValues(typeof(GameId)))
                if (GameCatalog.IsRegistered(id)) ids.Add(id);

            // Card copy styles — also drive each card's measured height (title + rules + controls).
            var titleSt = new GUIStyle(_ui) { fontSize = 14, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = Ink } };
            var descSt = new GUIStyle(_body) { fontSize = 12, wordWrap = true, normal = { textColor = Alpha(Ink, 0.82f) } };
            var ctrlTxtSt = new GUIStyle(_body) { fontSize = 12, wordWrap = true, normal = { textColor = Ink } };

            float gap = 14f, cardW = 250f, modeH = 122f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((cw + gap) / (cardW + gap)));
            int rows = Mathf.CeilToInt(ids.Count / (float)cols);

            // Variable card heights; each grid row takes the tallest card in it (web-style).
            var rowH = new float[rows];
            for (int i = 0; i < ids.Count; i++)
            {
                float ch = GameCardHeight(ids[i], cardW, _htpExpanded.Contains(ids[i]), titleSt, descSt, ctrlTxtSt);
                rowH[i / cols] = Mathf.Max(rowH[i / cols], ch);
            }
            float gridH = 0f;
            for (int r = 0; r < rows; r++) gridH += rowH[r] + gap;
            float contentH = introH + 16f + modeH + 28f + 38f + gridH + 20f;

            _htpScroll = GUI.BeginScrollView(new Rect(vx, vtop, vw, vh), _htpScroll, new Rect(0, 0, cw, contentH));
            float y = Para(0, 0, cw, MenuContent.HowToPlayIntro, bodyP) + 16f;

            float half = (cw - 14f) / 2f;
            ModeCard(new Rect(0, y, half, modeH), "💀 " + Loc.Get("ui.hardcore"), MenuContent.HardcoreRule, Red);
            ModeCard(new Rect(half + 14f, y, half, modeH), "🩹 " + Loc.Get("ui.casual"), MenuContent.CasualRule, Green);
            y += modeH + 28f;
            Lbl(new Rect(0, y, cw, 30), Loc.Get("htp.the_games"), new GUIStyle(_h2) { fontSize = 22 });
            y += 38f;

            float gridW = cols * cardW + (cols - 1) * gap, gx0 = (cw - gridW) / 2f;
            var rowY = new float[rows];
            float accY = y;
            for (int r = 0; r < rows; r++) { rowY[r] = accY; accY += rowH[r] + gap; }

            GameId? toggle = null;
            for (int i = 0; i < ids.Count; i++)
            {
                int r = i / cols, c = i % cols;
                var rect = new Rect(gx0 + c * (cardW + gap), rowY[r], cardW, rowH[r]);
                // Only cards actually on screen drive a live preview station (perf + layer budget).
                if (rect.yMax > _htpScroll.y - 40f && rect.y < _htpScroll.y + vh + 40f)
                    GamePreview.Request(ids[i]);
                if (DrawGameCard(rect, ids[i], _htpExpanded.Contains(ids[i]), titleSt, descSt, ctrlTxtSt))
                    toggle = ids[i];
            }
            GUI.EndScrollView();

            // Toggle after the draw so Layout and Repaint agree on expansion state this frame.
            if (toggle.HasValue && !_htpExpanded.Remove(toggle.Value)) _htpExpanded.Add(toggle.Value);
        }

        private void ModeCard(Rect r, string title, string body, Color accent)
        {
            Fill(r, new Color(0, 0, 0, 0.25f), 16f);
            Stroke(r, Alpha(accent, 0.5f), 1.5f, 16f);
            Lbl(new Rect(r.x + 16, r.y + 12, r.width - 32, 28), title, new GUIStyle(_h2) { fontSize = 20, normal = { textColor = accent } });
            Lbl(new Rect(r.x + 16, r.y + 44, r.width - 32, r.height - 52), body, new GUIStyle(_body) { fontSize = 13, wordWrap = true, normal = { textColor = InkDim } });
        }

        // Brand accents cycled across the game cards (used by the loading-tile fallback).
        // A property so it tracks the live (colorblind-aware) accents rather than
        // freezing them at static-init time.
        private static Color[] PreviewAccents =>
            new[] { Pink, Teal, Yellow, Green, new Color(0.49f, 0.34f, 0.76f), Red };

        // ---- Game-card metrics (shared by GameCardHeight + DrawGameCard so they agree) ----
        private const float CardTopPad = 10f, CardGapTP = 4f, CardGapPD = 8f, CardGapDB = 8f,
                            CardBtnH = 26f, CardGapBC = 8f, CardCtrlLabH = 16f, CardBottomPad = 12f;
        private static float CardPreviewH(float cardW) => (cardW - 20f) * 0.52f;
        private static string CardTitle(GameMeta meta) => meta.Icon + "  " + meta.Name.ToUpperInvariant();

        private float GameCardHeight(GameId id, float cardW, bool expanded, GUIStyle titleSt, GUIStyle descSt, GUIStyle ctrlTxtSt)
        {
            float innerW = cardW - 24f;
            var meta = GameCatalog.Of(id);
            var g = MenuContent.GuideFor(id);
            float titleH = Mathf.Max(20f, titleSt.CalcHeight(new GUIContent(CardTitle(meta)), innerW));
            float h = CardTopPad + titleH + CardGapTP + CardPreviewH(cardW) + CardGapPD
                    + descSt.CalcHeight(new GUIContent(g.Rules), innerW) + CardGapDB + CardBtnH + CardBottomPad;
            if (expanded)
                h += CardGapBC + CardCtrlLabH + ctrlTxtSt.CalcHeight(new GUIContent(g.Controls), innerW) + 4f;
            return h;
        }

        // The web's game card: title row, a live preview with a LIVE badge, the rules blurb,
        // and a "🎮 Controls ▾" dropdown that expands the control scheme inline. Returns true
        // the frame the dropdown is clicked.
        private bool DrawGameCard(Rect r, GameId id, bool expanded, GUIStyle titleSt, GUIStyle descSt, GUIStyle ctrlTxtSt)
        {
            Fill(new Rect(r.x, r.y + 4, r.width, r.height), new Color(0, 0, 0, 0.25f), 16f);
            Fill(r, new Color(0.078f, 0.192f, 0.153f, 0.92f), 16f);
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            Stroke(r, hover ? LineBright : Line, 1.5f, 16f);
            var meta = GameCatalog.Of(id);
            var g = MenuContent.GuideFor(id);
            var accent = PreviewAccents[(int)id % PreviewAccents.Length];
            float innerW = r.width - 24f, ix = r.x + 12f, yy = r.y + CardTopPad;

            // Title: icon + GAME NAME.
            float titleH = Mathf.Max(20f, titleSt.CalcHeight(new GUIContent(CardTitle(meta)), innerW));
            Lbl(new Rect(ix, yy, innerW, titleH), CardTitle(meta), titleSt);
            yy += titleH + CardGapTP;

            // Live preview (the real game render) + a LIVE badge.
            var pv = new Rect(r.x + 10f, yy, r.width - 20f, CardPreviewH(r.width));
            var shot = GamePreview.GetLive(id);
            if (shot != null) GUI.DrawTexture(pv, shot, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 10f);
            else DrawPreviewFallback(pv, meta.Icon, accent);
            Stroke(pv, Alpha(accent, hover ? 0.7f : 0.32f), 1.5f, 10f);
            var badge = new Rect(pv.xMax - 82f, pv.y + 6f, 76f, 17f);
            Fill(badge, new Color(0, 0, 0, 0.55f), 8.5f);
            Lbl(badge, "▶ " + Loc.Get("htp.live_bots"), new GUIStyle(_ui) { fontSize = 10, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.86f, 0.9f, 0.95f) } });
            yy = pv.yMax + CardGapPD;

            // Rules blurb.
            float descH = descSt.CalcHeight(new GUIContent(g.Rules), innerW);
            Lbl(new Rect(ix, yy, innerW, descH), g.Rules, descSt);
            yy += descH + CardGapDB;

            // Controls dropdown (expands the control scheme inline).
            bool toggled = DropdownBtn(new Rect(ix, yy, innerW, CardBtnH), "🎮 " + Loc.Get("htp.controls"), expanded);
            yy += CardBtnH;
            if (expanded)
            {
                yy += CardGapBC;
                Lbl(new Rect(ix, yy, innerW, CardCtrlLabH), Loc.Get("htp.controls_caps"),
                    new GUIStyle(_ui) { fontSize = 11, normal = { textColor = InkDim } });
                yy += CardCtrlLabH;
                Lbl(new Rect(ix, yy, innerW, ctrlTxtSt.CalcHeight(new GUIContent(g.Controls), innerW)), g.Controls, ctrlTxtSt);
            }
            return toggled;
        }

        // A small pill toggle: "🎮 Controls ▾ / ▴".
        private bool DropdownBtn(Rect r, string label, bool open)
        {
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            Fill(r, new Color(1f, 1f, 1f, hover ? 0.09f : 0.05f), 8f);
            Stroke(r, Alpha(Pink, hover ? 0.6f : 0.34f), 1f, 8f);
            Lbl(new Rect(r.x + 10f, r.y, r.width - 20f, r.height), label + (open ? "  ▴" : "  ▾"),
                new GUIStyle(_ui) { fontSize = 12, alignment = TextAnchor.MiddleLeft, normal = { textColor = Pink } });
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // Shown in a game card until its real render is baked (or if a bake fails): a clean
        // dark tile with the game's icon, so the card never looks broken or half-drawn.
        private void DrawPreviewFallback(Rect pv, string icon, Color accent)
        {
            Fill(pv, new Color(0.055f, 0.035f, 0.095f, 1f), 10f);
            Glow(pv.center.x, pv.center.y, pv.height * 1.4f, accent, 0.22f);
            Lbl(pv, icon, new GUIStyle(_h1) { fontSize = 40, alignment = TextAnchor.MiddleCenter });
        }

        // ============================ PATCH NOTES ============================
        private void DrawPatchNotes(float w, float h)
        {
            DrawSubHeader(w, h, MenuContent.PatchNotesTitle, MenuContent.PatchNotesSubtitle);
            float vx = 40f, vtop = 138f, vw = w - 80f, vh = h - vtop - 26f, cw = vw - 18f;
            float cardPad = 22f, cardW = Mathf.Min(820f, cw), innerW = cardW - 2f * cardPad, gx = (cw - cardW) / 2f;
            var note = new GUIStyle(_body) { fontSize = 14, wordWrap = true, normal = { textColor = Ink } };

            float contentH = 0f;
            foreach (var p in MenuContent.Changelog) contentH += PatchHeight(p, innerW, note) + 18f;

            _patchScroll = GUI.BeginScrollView(new Rect(vx, vtop, vw, vh), _patchScroll, new Rect(0, 0, cw, contentH));
            float y = 0f;
            foreach (var p in MenuContent.Changelog)
            {
                float ph = PatchHeight(p, innerW, note);
                var card = new Rect(gx, y, cardW, ph);
                Fill(card, Panel, 18f); Stroke(card, Line, 1.5f, 18f);
                float ix = gx + cardPad, iy = y + 18f;
                Lbl(new Rect(ix, iy, innerW - 110, 30), p.Version + " — " + p.Title, new GUIStyle(_h2) { fontSize = 19, normal = { textColor = Ink } });
                Chip(new Rect(card.xMax - cardPad - 92, iy + 2, 92, 24), p.Tag, TagColor(p.Tag));
                iy += 32f;
                Lbl(new Rect(ix, iy, innerW, 20), p.Date, new GUIStyle(_body) { fontSize = 12, normal = { textColor = InkDim } });
                iy += 26f;
                foreach (var n in p.Notes)
                {
                    float nh = note.CalcHeight(new GUIContent(n), innerW - 18);
                    Lbl(new Rect(ix, iy, 16, 20), "•", new GUIStyle(_ui) { fontSize = 15, normal = { textColor = Pink } });
                    Lbl(new Rect(ix + 18, iy, innerW - 18, nh), n, note);
                    iy += nh + 8f;
                }
                y += ph + 18f;
            }
            GUI.EndScrollView();
        }

        private static float PatchHeight(MenuContent.Patch p, float innerW, GUIStyle note)
        {
            float hgt = 18f + 32f + 26f;
            foreach (var n in p.Notes) hgt += note.CalcHeight(new GUIContent(n), innerW - 18) + 8f;
            return hgt + 16f;
        }

        private Color TagColor(string tag)
        {
            switch (tag)
            {
                case "Feature": return Teal;
                case "Balance": return Yellow;
                case "Bugfix": return Pink;
                case "Launch": return Green;
                default: return InkDim;
            }
        }

        // ============================ ACCOUNT ============================
        private void DrawAccount(float w, float h)
        {
            DrawSubHeader(w, h, MenuContent.AccountTitle, Loc.Get("acct.everything"));
            var prof = SaveService.Current;
            var steam = SteamService.Instance;
            string steamName = steam?.PlayerName;
            bool onSteam = steamName != null;
            if (_nameEdit == null) _nameEdit = prof?.name ?? "Player";

            float pw = 640f, px = (w - pw) / 2f, py = 158f;
            var panel = new Rect(px, py, pw, 432f);
            Fill(panel, Panel, 22f); Stroke(panel, Line, 2f, 22f);
            float cx = px + 30f, cw = pw - 60f, y = py + 28f;

            DrawPlayerAvatar(new Rect(cx, y, 86, 86), prof); // real equipped character (same as the menu), with player fallback
            float ix = cx + 106f, iw = cw - 106f;

            if (onSteam)
            {
                Lbl(new Rect(ix, y + 4, iw, 30), steamName, new GUIStyle(_h2) { fontSize = 24, normal = { textColor = Ink } });
                string idLine = Loc.Get("acct.signed_in_steam") + (steam.SteamIdString != null ? "  ·  " + steam.SteamIdString : "");
                Lbl(new Rect(ix, y + 40, iw, 22), idLine, new GUIStyle(_body) { fontSize = 13, normal = { textColor = InkDim } });
                Lbl(new Rect(ix, y + 62, iw, 22), MenuContent.AccountSteamBlurb, new GUIStyle(_body) { fontSize = 13, fontStyle = FontStyle.Italic, normal = { textColor = Alpha(InkDim, 0.85f) } });
            }
            else
            {
                Lbl(new Rect(ix, y, iw, 20), Loc.Get("acct.display_name"), new GUIStyle(_ui) { fontSize = 12, normal = { textColor = InkDim } });
                float fieldW = Mathf.Min(300f, iw - 116f);
                _nameEdit = GUI.TextField(new Rect(ix, y + 24, fieldW, 36), _nameEdit ?? "", 16);
                if (Btn(new Rect(ix + fieldW + 10f, y + 24, 96f, 36f), Loc.Get("ui.save"), Pink, OnDark) && prof != null)
                {
                    if (!string.IsNullOrWhiteSpace(_nameEdit))
                    {
                        prof.name = _nameEdit.Trim();
                        SaveService.Save();
                        Toast(Loc.Get("acct.saved_as", prof.name));
                    }
                    else
                    {
                        Toast(Loc.Get("acct.enter_name"));
                    }
                }
                Lbl(new Rect(ix, y + 66, iw, 22), Loc.Get("acct.guest"), new GUIStyle(_body) { fontSize = 13, fontStyle = FontStyle.Italic, normal = { textColor = InkDim } });
            }
            y += 110f;
            Fill(new Rect(cx, y, cw, 1f), Line, 0f); y += 18f;

            int marbles = prof?.marbles ?? 0, crowns = prof?.seriesWon ?? 0, rounds = prof?.roundsSurvived ?? 0;
            string title = MenuContent.TitleFor(marbles, crowns);
            float sw = (cw - 3f * 12f) / 4f;
            StatCard(new Rect(cx, y, sw, 80f), Loc.Get("acct.stat_marbles"), marbles.ToString(), Yellow);
            StatCard(new Rect(cx + (sw + 12f), y, sw, 80f), Loc.Get("acct.stat_crowns"), crowns.ToString(), Pink);
            StatCard(new Rect(cx + (sw + 12f) * 2f, y, sw, 80f), Loc.Get("acct.stat_rounds"), rounds.ToString(), Teal);
            StatCard(new Rect(cx + (sw + 12f) * 3f, y, sw, 80f), Loc.Get("acct.stat_title"), "", Ink);
            Lbl(new Rect(cx + (sw + 12f) * 3f + 8f, y + 30f, sw - 16f, 44f), title,
                new GUIStyle(_ui) { alignment = TextAnchor.UpperCenter, fontSize = 13, wordWrap = true, normal = { textColor = Ink } });
            y += 98f;

            Para(cx, y, cw, onSteam ? Loc.Get("acct.steam_cloud") : MenuContent.AccountGuestBlurb,
                new GUIStyle(_body) { fontSize = 14, wordWrap = true, normal = { textColor = InkDim } });
        }

        private void StatCard(Rect r, string label, string value, Color accent)
        {
            Fill(r, new Color(0, 0, 0, 0.28f), 14f);
            Stroke(r, Line, 1.5f, 14f);
            Lbl(new Rect(r.x, r.y + 9, r.width, 20), label, new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 12, normal = { textColor = InkDim } });
            if (!string.IsNullOrEmpty(value))
                Lbl(new Rect(r.x, r.y + 30, r.width, 38), value, new GUIStyle(_h2) { alignment = TextAnchor.MiddleCenter, fontSize = 28, normal = { textColor = accent } });
        }

        // ============================ PLAYERS (character picker) ============================
        private void DrawPlayers(float w, float h)
        {
            DrawSubHeader(w, h, Loc.Get("shop.choose_player"), Loc.Get("shop.player_hint"));
            // Never bail to a blank page: a missing profile self-heals via Load().
            var prof = SaveService.Current ?? SaveService.Load() ?? new PlayerProfile();

            float pw = 980f, px = (w - pw) / 2f, py = 150f, ph = Mathf.Max(320f, h - py - 36f);
            var panel = new Rect(px, py, pw, ph);
            Fill(panel, Panel, 22f); Stroke(panel, Line, 2f, 22f);

            // Hero preview: the player's selected player wearing its equipped accessories,
            // so they see exactly what they'll play as before browsing the grid below.
            // Reuses the home-screen avatar (real sprite art + idle bob), which already
            // overlays the worn cosmetics — no separate accessory-icon row needed.
            const float prevSz = 104f;
            var prevBox = new Rect(panel.center.x - prevSz / 2f, py + 14f, prevSz, prevSz);
            Glow(prevBox.center.x, prevBox.center.y + 6f, prevSz * 1.5f, Palette.Body(prof.characterId ?? "avo"), 0.32f);
            DrawPlayerAvatar(prevBox, prof);

            string wornName = Cosmetics.Characters.FirstOrDefault(c => c.Id == prof.characterId).Name ?? prof.characterId ?? "—";
            Lbl(new Rect(px, prevBox.yMax + 2f, pw, 26f), Loc.Get("shop.wearing") + "  " + wornName,
                new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 16, normal = { textColor = Ink } });

            MarblesPill(new Rect(panel.xMax - 214f, py + 14f, 190f, 34f), prof.marbles);

            float tabsY = prevBox.yMax + 30f;
            if (Pill(new Rect(px + 28f, tabsY, 120f, 32f), Loc.Get("shop.tab_players"), Teal, _playerTab == 0)) _playerTab = 0;
            if (Pill(new Rect(px + 156f, tabsY, 156f, 32f), Loc.Get("shop.tab_acc"), Pink, _playerTab == 1)) _playerTab = 1;

            float pad = 24f, cx = px + pad, top = tabsY + 44f, innerW = pw - pad * 2f;
            var view = new Rect(cx, top, innerW, ph - (top - py) - pad);
            try
            {
                if (_playerTab == 0) DrawPlayerGrid(view, prof);
                else DrawAccessoryList(view, prof);
            }
            catch (System.Exception e)
            {
                Lbl(new Rect(cx, top + 16f, innerW, 80f),
                    "⚠ " + Loc.Get("shop.cant_draw") + "\n" + e.GetType().Name + " — " + e.Message,
                    new GUIStyle(_body) { fontSize = 14, wordWrap = true, normal = { textColor = Red } });
                if (Event.current.type == EventType.Repaint) Debug.LogException(e);
            }
        }

        private void DrawPlayerGrid(Rect view, PlayerProfile prof)
        {
            // Only show characters that have real art yet (skip colored-player
            // placeholders for food etc.); more appear as their art is built.
            var chars = Cosmetics.Characters.Where(c => CharacterPreview.Get(c.Id).Has)
                .OrderBy(c => c.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
            const float cellW = 168f, cellH = 150f, gap = 14f;
            float innerW = view.width;
            int cols = Mathf.Max(1, Mathf.FloorToInt((innerW + gap) / (cellW + gap)));
            int rows = Mathf.CeilToInt(chars.Count / (float)cols);
            float gridW = cols * cellW + (cols - 1) * gap;
            float ox = Mathf.Max(0f, (innerW - 16f - gridW) * 0.5f);
            var content = new Rect(0, 0, innerW - 16f, rows * (cellH + gap));
            _playerScroll = GUI.BeginScrollView(view, _playerScroll, content);
            for (int i = 0; i < chars.Count; i++)
            {
                int col = i % cols, row = i / cols;
                DrawPlayerCell(new Rect(ox + col * (cellW + gap), row * (cellH + gap), cellW, cellH), chars[i], prof);
            }
            GUI.EndScrollView();
        }

        private void DrawPlayerCell(Rect cell, CharacterDef c, PlayerProfile prof)
        {
            bool owned = Cosmetics.IsOwned(c.Id, prof.unlocked);
            bool worn = prof.characterId == c.Id;
            bool hover = cell.Contains(Event.current.mousePosition);

            Fill(cell, new Color(0, 0, 0, 0.30f), 16f);
            Stroke(cell, worn ? Pink : (hover ? LineBright : Line), worn ? 2.5f : 1.5f, 16f);

            const float ps = 82f;
            var pr = new Rect(cell.center.x - ps / 2f, cell.y + 12f, ps, ps);
            Fill(new Rect(pr.x + 10f, pr.yMax - 8f, ps - 20f, 12f), new Color(0, 0, 0, 0.25f), 8f); // ground shadow

            var thumb = CharacterPreview.Get(c.Id);
            // Locked characters render at full opacity too — the 🔒 badge + price already mark them
            // as locked. The old 0.4 alpha made dark characters (e.g. the pirate's black beard) wash
            // out to near-invisible against the dark card, so locked items looked broken/transparent.
            if (thumb.Has) DrawThumb(pr, thumb, 1f);
            else
            {
                var body = Palette.Body(c.Id);
                GUI.DrawTexture(pr, _soft, ScaleMode.StretchToFill, true, 0f,
                    new Color(body.r, body.g, body.b, 1f), 0f, 0f);
            }

            if (worn) DrawWornAccessories(pr, prof, thumb); // show your equipped cosmetics on your player

            Lbl(new Rect(cell.x + 6f, pr.yMax + 2f, cell.width - 12f, 34f), c.Name,
                new GUIStyle(_ui) { alignment = TextAnchor.UpperCenter, fontSize = 13, wordWrap = true, normal = { textColor = owned ? Ink : InkDim } });

            if (worn)
                Chip(new Rect(cell.center.x - 48f, cell.yMax - 30f, 96f, 22f), Loc.Get("shop.selected"), Pink);
            else if (owned)
                Lbl(new Rect(cell.x + 8f, cell.yMax - 28f, cell.width - 16f, 20f), Loc.Get("shop.tap_wear"),
                    new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 12, normal = { textColor = Teal } });
            else
                Lbl(new Rect(cell.x + 8f, cell.yMax - 28f, cell.width - 16f, 20f), "🔒 ◍ " + c.UnlockCost,
                    new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 13, normal = { textColor = prof.marbles >= c.UnlockCost ? Yellow : InkDim } });

            if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) SelectOrBuyPlayer(c, prof);
        }

        // Tap an owned player to wear it; tap a locked one to buy (if affordable) and wear.
        private void SelectOrBuyPlayer(CharacterDef c, PlayerProfile prof)
        {
            if (Cosmetics.IsOwned(c.Id, prof.unlocked))
            {
                prof.characterId = c.Id;
                SaveService.Save();
                AudioService.Instance?.Play("click");
                return;
            }
            var set = new HashSet<string>(prof.unlocked ?? new List<string>());
            int marbles = prof.marbles;
            if (CosmeticsWallet.TryPurchase(c.Id, ref marbles, set) == PurchaseResult.Ok)
            {
                prof.marbles = marbles;
                prof.unlocked = set.ToList();
                prof.characterId = c.Id;
                SaveService.Save();
                AudioService.Instance?.Play("good");
            }
            else
            {
                AudioService.Instance?.Play("bad", 0.6f); // can't afford
            }
        }

        // Overlay the equipped accessories on a character's preview thumbnail. Anchor
        // to the character's actual drawn rect — the art is aspect-fit and centered
        // inside the box (see DrawThumb), so a non-square character (e.g. a tall ninja)
        // sits narrower than the box. Pinning accessories to the box instead would let
        // them drift off the head/ear into the corner.
        private void DrawWornAccessories(Rect pr, PlayerProfile prof, CharacterPreview.Thumb thumb)
        {
            if (prof.equipped == null) return;
            var fit = thumb.Has ? FitAspect(pr, thumb.Aspect) : pr;
            foreach (var slot in Cosmetics.Slots)
            {
                var id = prof.equipped.FirstOrDefault(e => Cosmetics.SlotOf(e) == slot);
                var sp = id != null ? AccessoryArt.Get(id) : null;
                if (sp == null) continue;
                // Glasses fit to the character's REAL eyes (a lens on each), tilted to match — the
                // animals' eyes are wide-set and slightly uneven, so a flat centred sprite misses.
                if (slot == "eyes" && thumb.Has && CharacterArt.TryEyes(prof.characterId, out var feL, out var feR, out var feRad))
                {
                    if (id == "eyepatch") DrawSpannedEyewear(sp.texture, thumb, fit, feL, feR); // asymmetric: one patch + strap
                    else DrawFittedEyewear((AccessoryArt.GetLenses(id) ?? sp).texture, thumb, fit, feL, feR, feRad, prof.characterId == "clown" ? fit.width * 0.07f : 0f);             // two lenses, one per eye
                }
                else
                    GUI.DrawTexture(AccPreviewRect(slot, fit, thumb, prof.characterId), sp.texture, ScaleMode.ScaleToFit, true);
            }

            // TEMP DEBUG (remove after diagnosing): draw the runtime head box (yellow), face box
            // (teal) and the glasses' eye anchors (red dots), to compare against the real art.
            if (DebugAccBoxes && thumb.Has)
            {
                Rect Box(Rect b)
                {
                    float bx = thumb.Flip ? (1f - b.x - b.width) : b.x;
                    return new Rect(fit.x + bx * fit.width, fit.y + (1f - b.y - b.height) * fit.height,
                                    b.width * fit.width, b.height * fit.height);
                }
                Stroke(Box(thumb.HeadRect), Yellow, 1.5f, 0f);
                Stroke(Box(thumb.FaceRect), Teal, 1.5f, 0f);
                if (CharacterArt.TryEyes(prof.characterId, out var deL, out var deR, out _))
                {
                    var dl = FacePoint(deL, thumb, fit); var dr = FacePoint(deR, thumb, fit);
                    Fill(new Rect(dl.x - 3f, dl.y - 3f, 6f, 6f), Red, 3f);
                    Fill(new Rect(dr.x - 3f, dr.y - 3f, 6f, 6f), Red, 3f);
                }
            }
        }
        private const bool DebugAccBoxes = false; // TEMP: face/head/eye overlay for accessory diagnosis

        // Eye anchors are DETECTED at runtime from each character's face sprite — see
        // CharacterArt.TryEyes (and CharacterArt.EyewearLensFrac for the lens spacing).

        // A point in face-box Norm (Y-up) -> screen pixels inside the fitted thumbnail rect.
        private static Vector2 FacePoint(Vector2 e, CharacterPreview.Thumb t, Rect fit)
        {
            var f = t.FaceRect;
            float nx = f.x + e.x * f.width, ny = f.y + e.y * f.height;
            if (t.Flip) nx = 1f - nx;
            return new Vector2(fit.x + nx * fit.width, fit.y + (1f - ny) * fit.height);
        }

        // Draw the eyewear so each lens sits on — and is sized to — the character's actual eye.
        // One fixed sprite can't match every character's eye spacing AND size (the owl's big close
        // eyes vs a cat's small wide ones), so we place each lens individually: cropped from the
        // sprite, centred on the detected eye, scaled to that eye.
        private static void DrawFittedEyewear(Texture tex, CharacterPreview.Thumb thumb, Rect fit, Vector2 eyeL, Vector2 eyeR, float eyeRad, float leftShift = 0f)
        {
            Vector2 L = FacePoint(eyeL, thumb, fit), R = FacePoint(eyeR, thumb, fit);
            if (L.x > R.x) { var tmp = L; L = R; R = tmp; }
            float faceW = thumb.FaceRect.width * fit.width;
            float eyeGap = Mathf.Max(1f, Mathf.Abs(R.x - L.x));
            // lens radius: cover the eye (with a floor so tiny-eyed faces still get real glasses),
            // but never so large the two rims collide into a figure-8. The floor is tied to the eye
            // SPACING, not the whole face box — a face box much wider than the eyes (the frog wizard's
            // small, wide-set eyes) otherwise forces lenses ~3× the eye that swallow the face.
            float sr = Mathf.Max(eyeRad * faceW * 1.3f, eyeGap * 0.20f);
            sr = Mathf.Min(sr, faceW * 0.42f);    // never dominate the face (keeps the clown's big eyes in check)
            sr = Mathf.Min(sr, eyeGap * 0.47f);   // lenses just touch at most — no overlap, so the rims can't cross into a second "bridge"
            sr = Mathf.Max(sr, 4f);
            L.x -= leftShift; // slide the left lens left without resizing (size was fixed from the original gap)
            var prev = GUI.color; GUI.color = Color.white;
            // lenses: a TIGHT 28×28 crop of each rim+fill, centred on the detected eye, sized to it.
            // (No temple arms — they read as a stray nub off the side.)
            DrawLens(tex, new Rect(8f / 128f, 50f / 128f, 28f / 128f, 28f / 128f), L, sr);   // left
            DrawLens(tex, new Rect(92f / 128f, 50f / 128f, 28f / 128f, 28f / 128f), R, sr);  // right
            // bridge: a SOLID frame-coloured bar (a 2px swatch of the sprite's own — now raised — bridge
            // line, stretched so it can't vanish into a hairline) along the eye line and lifted toward the
            // brow to sit OVER the nose. The sprite bridge was moved up to match, so the stub baked into
            // each lens crop lifts with it and the two stay one continuous bridge. Follows tilt to land on both.
            Vector2 dir = R - L; float dlen = dir.magnitude; dir = dlen > 0.001f ? dir / dlen : Vector2.right;
            Vector2 up = new Vector2(dir.y, -dir.x);          // perpendicular, toward screen-up
            Vector2 off = up * (sr * 0.43f);                  // match the sprite bridge's lift (6px of a 14px lens)
            Vector2 ba = L + dir * (sr * 0.85f) + off, bb = R - dir * (sr * 0.85f) + off, bmid = (ba + bb) * 0.5f;
            float barLen = Vector2.Distance(ba, bb), barH = Mathf.Max(3f, sr * 0.22f);
            var mtx = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, bmid);
            GUI.DrawTextureWithTexCoords(new Rect(bmid.x - barLen * 0.5f, bmid.y - barH * 0.5f, barLen, barH), tex, new Rect(10f / 128f, 63f / 128f, 2f / 128f, 2f / 128f), true);
            GUI.matrix = mtx;
            GUI.color = prev;
        }

        // Draw one lens — a tight 28×28 crop of the eyewear sprite (one rim+fill) — centred at c,
        // sized so the lens covers radius sr.
        private static void DrawLens(Texture tex, Rect uv, Vector2 c, float sr)
        {
            GUI.DrawTextureWithTexCoords(new Rect(c.x - sr, c.y - sr, 2f * sr, 2f * sr), tex, uv, true);
        }

        // Whole-sprite eyewear (the eyepatch — asymmetric, can't be split per lens): scale + rotate
        // the sprite so its two lens slots span the eyes.
        private static void DrawSpannedEyewear(Texture tex, CharacterPreview.Thumb thumb, Rect fit, Vector2 eyeL, Vector2 eyeR)
        {
            Vector2 L = FacePoint(eyeL, thumb, fit), R = FacePoint(eyeR, thumb, fit);
            if (L.x > R.x) { var tmp = L; L = R; R = tmp; }
            float dist = Vector2.Distance(L, R);
            if (dist < 1f) return;
            float w = dist / CharacterArt.EyewearLensFrac;
            var rect = new Rect((L.x + R.x) * 0.5f - w * 0.5f, (L.y + R.y) * 0.5f - w * 0.5f, w, w);
            float ang = Mathf.Atan2(R.y - L.y, R.x - L.x) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, rect.center);
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
            GUI.matrix = m;
        }

        // Place a worn accessory relative to THIS character's head box (so it lines up
        // on every player, whose head sits at a different spot/size in its thumbnail),
        // rather than a fixed fraction of the whole frame. `fit` is the aspect-fitted
        // thumbnail rect; `thumb.HeadRect` is the head in Norm space (0..1, Y-up).
        private static Rect AccPreviewRect(string slot, Rect fit, CharacterPreview.Thumb thumb, string charId)
        {
            var hr = thumb.Has ? thumb.HeadRect : new Rect(0.15f, 0.45f, 0.70f, 0.50f);
            // Anchor in screen space. When the art is mirrored (Flip), the head sits on the
            // mirrored side of the frame, so mirror the head box's X to match. Anchoring to
            // this screen-space box — rather than mirroring each accessory's X afterwards —
            // keeps side pieces like the flower on the SAME visual side of the head for every
            // character, so flipped art (slime/ninja/rogue/sorcerer) no longer throws the
            // flower to the opposite ear. Y is untouched: the flip is horizontal only.
            float hx = thumb.Flip ? (1f - hr.x - hr.width) : hr.x; // head left edge (screen X)
            float hcx = hx + hr.width * 0.5f;     // head centre X
            float htop = hr.y + hr.height;        // head top (Norm, Y-up)
            float hw = hr.width;
            // Face box (screen X) — glasses ride the real eyes; the ear-inflated head box overshoots them.
            var fr = thumb.FaceRect;
            float fx = thumb.Flip ? (1f - fr.x - fr.width) : fr.x;
            float fcx = fx + fr.width * 0.5f;
            float ax, ay, s; // anchor point (Norm) + size (fraction of head width)
            switch (slot)
            {
                // The "ear" anchor sits inside the head, not at the bounding-box corner: the
                // box is inflated by ears/horns/tufts, so the old top-right corner (0.86/0.80)
                // floated the flower off the head on most players. 0.68 across / 0.69 up tucks it
                // beside the head for every character (bears, demons, cats, owls, cow, …).
                case "head": ax = hcx;             ay = htop + hw * 0.12f;        s = hw * 1.00f; break; // hat on the crown
                case "eyes": ax = fcx;             ay = fr.y + fr.height * 0.62f; s = fr.width * 1.00f; break; // glasses fallback (no measured eyes, e.g. slime/humans): centred on the face
                case "neck": ax = hcx;             ay = hr.y + hr.height * 0.02f; s = hw * 0.80f; break; // collar at the neck
                case "ear":
                    ax = hx + hw * 0.68f; ay = hr.y + hr.height * 0.69f; s = hw * 0.52f; // flower tucked beside the ear
                    // Per-character fixes where the generic spot lands wrong:
                    if (charId == "cow")   ay += hr.height * 0.07f;                                          // cow's eyes sit high in its box
                    if (charId == "slime") s  = hw * 0.72f;                                                    // round player, small default head box → scale the flower up
                    // Sayonara (rogue) is the hooded ninja: a wide, short head with a central eye-slit, so
                    // the width-sized flower blankets the eyes. Shrink it and tuck it at the hood's corner.
                    if (charId == "rogue") { s = hw * 0.36f; ax = hx + hw * 0.82f; ay = hr.y + hr.height * 0.80f; }
                    break;
                default:     ax = hcx;             ay = htop;                     s = hw * 0.80f; break;
            }
            float sizePx = s * fit.width;
            float scx = fit.x + ax * fit.width;
            float scy = fit.y + (1f - ay) * fit.height; // Norm Y-up -> screen Y-down
            return new Rect(scx - sizePx * 0.5f, scy - sizePx * 0.5f, sizePx, sizePx);
        }

        private void DrawAccessoryList(Rect view, PlayerProfile prof)
        {
            const float cellW = 150f, cellH = 94f, gap = 12f, hdrH = 30f;
            float innerW = view.width;
            int cols = Mathf.Max(1, Mathf.FloorToInt((innerW + gap) / (cellW + gap)));

            float contentH = 0f;
            foreach (var slot in Cosmetics.Slots)
            {
                int n = Cosmetics.Accessories.Count(a => a.Slot == slot);
                contentH += hdrH + Mathf.CeilToInt(n / (float)cols) * (cellH + gap) + 10f;
            }
            var content = new Rect(0, 0, innerW - 16f, contentH);

            _accScroll = GUI.BeginScrollView(view, _accScroll, content);
            float y = 0f;
            foreach (var slot in Cosmetics.Slots)
            {
                Lbl(new Rect(2f, y, innerW, hdrH), SlotTitle(slot),
                    new GUIStyle(_ui) { fontSize = 14, normal = { textColor = Pink } });
                y += hdrH;
                var items = Cosmetics.Accessories.Where(a => a.Slot == slot).ToList();
                for (int i = 0; i < items.Count; i++)
                    AccCell(new Rect((i % cols) * (cellW + gap), y + (i / cols) * (cellH + gap), cellW, cellH), items[i], prof);
                y += Mathf.CeilToInt(items.Count / (float)cols) * (cellH + gap) + 10f;
            }
            GUI.EndScrollView();
        }

        private void AccCell(Rect cell, AccessoryDef a, PlayerProfile prof)
        {
            bool owned = Cosmetics.IsOwned(a.Id, prof.unlocked);
            bool worn = prof.equipped != null && prof.equipped.Contains(a.Id);
            bool hover = cell.Contains(Event.current.mousePosition);
            Fill(cell, new Color(0, 0, 0, 0.30f), 14f);
            Stroke(cell, worn ? Pink : (hover ? LineBright : Line), worn ? 2.5f : 1.5f, 14f);

            var icon = AccessoryArt.Get(a.Id);
            if (icon != null)
            {
                var prevC = GUI.color; GUI.color = owned ? Color.white : new Color(1, 1, 1, 0.45f);
                GUI.DrawTexture(new Rect(cell.center.x - 24f, cell.y + 4f, 48f, 40f), icon.texture, ScaleMode.ScaleToFit, true);
                GUI.color = prevC;
            }
            Lbl(new Rect(cell.x + 6f, cell.y + 42f, cell.width - 12f, 20f), Prettify(a.Id),
                new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 12, normal = { textColor = owned ? Ink : InkDim } });
            string status = worn ? Loc.Get("shop.worn_remove") : owned ? Loc.Get("shop.tap_wear") : "🔒 ◍ " + a.Price;
            Lbl(new Rect(cell.x + 6f, cell.yMax - 22f, cell.width - 12f, 18f), status,
                new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 11, normal = { textColor = worn ? Pink : (owned ? Teal : (prof.marbles >= a.Price ? Yellow : InkDim)) } });

            if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) SelectOrBuyAccessory(a, prof);
        }

        // Owned → toggle worn (one per slot); locked → buy (if affordable) then wear.
        private void SelectOrBuyAccessory(AccessoryDef a, PlayerProfile prof)
        {
            if (prof.equipped == null) prof.equipped = new List<string>();
            if (Cosmetics.IsOwned(a.Id, prof.unlocked))
            {
                if (prof.equipped.Contains(a.Id)) prof.equipped.Remove(a.Id);
                else { prof.equipped.RemoveAll(e => Cosmetics.SlotOf(e) == a.Slot); prof.equipped.Add(a.Id); }
                SaveService.Save();
                AudioService.Instance?.Play("click");
                return;
            }
            var set = new HashSet<string>(prof.unlocked ?? new List<string>());
            int marbles = prof.marbles;
            if (CosmeticsWallet.TryPurchase(a.Id, ref marbles, set) == PurchaseResult.Ok)
            {
                prof.marbles = marbles;
                prof.unlocked = set.ToList();
                prof.equipped.RemoveAll(e => Cosmetics.SlotOf(e) == a.Slot);
                prof.equipped.Add(a.Id);
                SaveService.Save();
                AudioService.Instance?.Play("good");
            }
            else AudioService.Instance?.Play("bad", 0.6f);
        }

        private static string SlotTitle(string slot)
        {
            switch (slot)
            {
                case "head": return Loc.Get("shop.slot_hats");
                case "eyes": return Loc.Get("shop.slot_eyewear");
                case "neck": return Loc.Get("shop.slot_neck");
                case "ear":  return Loc.Get("shop.slot_ear");
                default: return slot.ToUpper();
            }
        }

        private static readonly Dictionary<string, string> AccEmojis = new Dictionary<string, string>
        {
            { "beanie", "🧢" }, { "cap", "🧢" }, { "partyhat", "🥳" }, { "cowboy", "🤠" }, { "tophat", "🎩" }, { "crown", "👑" },
            { "glasses", "👓" }, { "specs", "🤓" }, { "cateye", "😎" }, { "rounds", "🕶" }, { "shades", "🕶" }, { "aviators", "🕶" }, { "eyepatch", "🏴‍☠️" },
            { "bandana", "🧣" }, { "bowtie", "🎀" },
            { "banana", "🍌" }, { "greenana", "🍌" }, { "spotnana", "🍌" }, { "flower", "🌼" },
            { "rose", "🌹" }, { "bluebell", "🔔" }, { "sunflower", "🌻" }, { "feather", "🪶" },
        };
        private static string AccEmoji(string id) => AccEmojis.TryGetValue(id, out var e) ? e : "🎁";
        private static string Prettify(string id) => string.IsNullOrEmpty(id) ? id : char.ToUpper(id[0]) + id.Substring(1);

        private static string EquippedEmoji(PlayerProfile prof)
        {
            if (prof.equipped == null || prof.equipped.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var slot in Cosmetics.Slots)
            {
                var id = prof.equipped.FirstOrDefault(e => Cosmetics.SlotOf(e) == slot);
                if (id != null) sb.Append(AccEmoji(id));
            }
            return sb.ToString();
        }

        // Fit a sprite of the given aspect (w/h) centered inside a box.
        private static Rect FitAspect(Rect box, float aspect)
        {
            float w = box.width, hh = box.height;
            if (aspect >= 1f) hh = box.width / Mathf.Max(0.01f, aspect);
            else w = box.height * aspect;
            return new Rect(box.center.x - w / 2f, box.center.y - hh / 2f, w, hh);
        }

        // Composite a full-character thumbnail (all sprite parts, back-to-front).
        private static void DrawThumb(Rect box, CharacterPreview.Thumb thumb, float alpha)
            => DrawThumbInRect(FitAspect(box, thumb.Aspect), thumb, alpha, default, 0f);

        // Draw the composited character into an already-fit rect, optionally lerping
        // every part toward a status color (e.g. frozen → teal).
        private static void DrawThumbInRect(Rect fit, CharacterPreview.Thumb thumb, float alpha, Color tintToward, float tintAmt)
        {
            if (!thumb.Has) return;
            var prev = GUI.color;
            if (thumb.Rt != null) // real Unity render — exact, no composite needed
            {
                var rc = tintAmt > 0f ? Color.Lerp(Color.white, tintToward, tintAmt) : Color.white;
                rc.a *= alpha;
                GUI.color = rc;
                GUI.DrawTexture(fit, thumb.Rt, ScaleMode.ScaleToFit, true);
                GUI.color = prev;
                return;
            }
            if (thumb.Parts == null) { GUI.color = prev; return; }
            foreach (var p in thumb.Parts)
            {
                float w = p.Norm.width * fit.width;
                float h = p.Norm.height * fit.height;
                float nx = thumb.Flip ? (1f - p.Norm.x - p.Norm.width) : p.Norm.x;
                float x = fit.x + nx * fit.width;
                float y = fit.y + (1f - p.Norm.y - p.Norm.height) * fit.height; // GUI is Y-down
                var col = tintAmt > 0f ? Color.Lerp(p.Tint, tintToward, tintAmt) : p.Tint;
                col.a *= alpha;
                GUI.color = col;
                // negative texCoord width mirrors the sprite horizontally
                var uv = thumb.Flip ? new Rect(p.Uv.x + p.Uv.width, p.Uv.y, -p.Uv.width, p.Uv.height) : p.Uv;
                GUI.DrawTextureWithTexCoords(new Rect(x, y, w, h), p.Tex, uv, true);
            }
            GUI.color = prev;
        }

        // ============================ CONTROLS (remap) ============================
        private void DrawControls(float w, float h)
        {
            DrawSubHeader(w, h, MenuContent.ControlsTitle, MenuContent.ControlsSubtitle);
            var s = SaveService.Current?.settings;
            if (s == null) return;

            float pw = 560f, px = (w - pw) / 2f, py = 158f;
            var panel = new Rect(px, py, pw, 96f + BindLabels.Length * 56f + 84f);
            Fill(panel, Panel, 22f); Stroke(panel, Line, 2f, 22f);
            float cx = px + 30f, cw = pw - 60f, y = py + 24f;

            Lbl(new Rect(cx, y, cw, 24), Loc.Get("ctrl.keyboard_p1"), new GUIStyle(_ui) { fontSize = 13, normal = { textColor = InkDim } });
            y += 38f;
            var binds = BindLabels;
            for (int i = 0; i < binds.Length; i++)
            {
                var rowR = new Rect(cx, y, cw, 46f);
                Fill(rowR, new Color(1, 1, 1, 0.04f), 12f);
                Lbl(new Rect(rowR.x + 16, rowR.y, cw * 0.55f, 46f), binds[i],
                    new GUIStyle(_ui) { fontSize = 16, alignment = TextAnchor.MiddleLeft, normal = { textColor = Ink } });
                bool capturing = _rebindIndex == i;
                string keyTxt = capturing ? Loc.Get("ctrl.press_key") : KeyName(GetBind(s, i));
                if (Pill(new Rect(rowR.xMax - 168f, rowR.y + 7f, 158f, 32f), keyTxt, capturing ? Yellow : (Color?)Teal, capturing))
                    _rebindIndex = capturing ? -1 : i;
                y += 56f;
            }
            y += 8f;
            Lbl(new Rect(cx, y, cw, 22), Loc.Get("ctrl.arrows_note"),
                new GUIStyle(_body) { fontSize = 13, fontStyle = FontStyle.Italic, normal = { textColor = InkDim } });
            y += 32f;
            if (GhostBtn(new Rect(cx, y, 210f, 40f), Loc.Get("ctrl.reset")))
            {
                s.ResetBindings(); SaveService.Save(); _rebindIndex = -1;
            }
        }

        private static string KeyName(Key k) => k == Key.None ? "—" : k.ToString();

        // ============================ CREDITS ============================
        // Dedicated, scrollable attribution page (reached from Settings). Replaces the
        // old clipped one-line label so the legally-required CC-BY credits are actually
        // legible. Content is the language-neutral ledger in MenuContent.Credits; only
        // the chrome (title/subtitle/group headings) is localized.
        private void DrawCredits(float w, float h)
        {
            DrawSubHeader(w, h, MenuContent.CreditsTitle, MenuContent.CreditsSubtitle);
            float vx = 40f, vtop = 138f, vw = w - 80f, vh = h - vtop - 26f, cw = vw - 18f;
            float cardPad = 24f, cardW = Mathf.Min(720f, cw), innerW = cardW - 2f * cardPad, gx = (cw - cardW) / 2f;
            var work = new GUIStyle(_body) { fontSize = 15, wordWrap = true, normal = { textColor = Ink } };
            var meta = new GUIStyle(_body) { fontSize = 13, wordWrap = true, fontStyle = FontStyle.Italic, normal = { textColor = InkDim } };

            float contentH = 0f;
            foreach (var g in MenuContent.Credits) contentH += CreditGroupHeight(g, innerW, work, meta) + 18f;

            _creditsScroll = GUI.BeginScrollView(new Rect(vx, vtop, vw, vh), _creditsScroll, new Rect(0, 0, cw, contentH));
            float y = 0f;
            foreach (var g in MenuContent.Credits)
            {
                float ph = CreditGroupHeight(g, innerW, work, meta);
                var card = new Rect(gx, y, cardW, ph);
                Fill(card, Panel, 18f); Stroke(card, Line, 1.5f, 18f);
                float ix = gx + cardPad, iy = y + 18f;
                Lbl(new Rect(ix, iy, innerW, 28), Loc.Get(g.HeadingKey),
                    new GUIStyle(_h2) { fontSize = 20, normal = { textColor = Pink } });
                iy += 38f;
                foreach (var e in g.Entries)
                {
                    float wh = work.CalcHeight(new GUIContent(e.Work), innerW);
                    Lbl(new Rect(ix, iy, innerW, wh), e.Work, work);
                    iy += wh + 2f;
                    string sub = $"{e.Author}  ·  {e.License}  ·  {e.Source}";
                    float sh = meta.CalcHeight(new GUIContent(sub), innerW);
                    Lbl(new Rect(ix, iy, innerW, sh), sub, meta);
                    iy += sh + 12f;
                }
                y += ph + 18f;
            }
            GUI.EndScrollView();
        }

        private static float CreditGroupHeight(MenuContent.CreditGroup g, float innerW, GUIStyle work, GUIStyle meta)
        {
            float hgt = 18f + 38f; // top pad + heading
            foreach (var e in g.Entries)
            {
                hgt += work.CalcHeight(new GUIContent(e.Work), innerW) + 2f;
                hgt += meta.CalcHeight(new GUIContent($"{e.Author}  ·  {e.License}  ·  {e.Source}"), innerW) + 12f;
            }
            return hgt + 10f; // bottom pad
        }

        private void StartSolo(SeriesMode mode, RoundsMode rounds, int fieldSize)
        {
            _seriesBanked = false;
            _router.Active = _sim;
            var prof = SaveService.Current;
            _sim.HostLocalSeries(mode, rounds, prof?.name ?? "You", prof?.characterId ?? "avo", prof?.equipped, fieldSize);
        }

        private void StartCoop(SeriesMode mode, RoundsMode rounds, int fieldSize)
        {
            _seriesBanked = false;
            _router.Active = _sim;
            var prof = SaveService.Current;
            // P1 = keyboard (the saved profile); each connected gamepad = another player.
            int count = Mathf.Clamp(1 + Gamepad.all.Count, 1, 4); // shared screen: cap at 4
            var roster = new List<string> { "fox", "panther", "bunny", "cat" };
            var locals = new List<LocalPlayerInfo>();
            for (int i = 0; i < count; i++)
            {
                string name = i == 0 ? (prof?.name ?? "P1") : $"P{i + 1}";
                string ch = i == 0 ? (prof?.characterId ?? "avo") : roster[i % roster.Count];
                locals.Add(new LocalPlayerInfo("local" + i, name, ch, i == 0 ? prof?.equipped : null));
            }
            _sim.HostLocalCoop(mode, rounds, locals, Mathf.Max(fieldSize, count));
        }

        private void DrawIntro(float w, float h)
        {
            var game = _router.CurrentGame;
            string name = game != null ? GameName(game.Value) : "";
            string icon = game != null ? GameCatalog.Of(game.Value).Icon : "";

            // Match the web build's IntroOverlay: a full-screen dark backdrop covers the
            // bright arena (text floating on the live arena was the legibility bug), then
            // standard ink text on it — round number in the web's yellow accent, the big
            // game title in the default ink, the prompt dimmed.
            DrawThemeBackdrop(w, h);

            Lbl(new Rect(0, h * 0.34f, w, 36), Loc.Get("ui.round", _router.RoundIndex + 1),
                new GUIStyle(_h2) { alignment = TextAnchor.MiddleCenter, fontSize = 22, normal = { textColor = Yellow } });
            OutlineLbl(new Rect(0, h * 0.42f, w, 70), $"{icon} {name}",
                new GUIStyle(_h1) { alignment = TextAnchor.MiddleCenter }, 1.5f, 3f);
            Lbl(new Rect(0, h * 0.54f, w, 36), Loc.Get("ui.get_ready"),
                new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, normal = { textColor = InkDim } });
        }

        private GUIStyle _h2WithCenter() => new GUIStyle(_h2) { alignment = TextAnchor.MiddleCenter };

        private void DrawHud(float w, float h)
        {
            var current = _router.CurrentGame;
            var snap = _router.Latest;
            string game = current != null ? GameName(current.Value) : "";
            int alive = snap?.Actors?.Count(a => a.Alive) ?? 0;

            GUI.Box(new Rect(10, 10, 360, 64), "");
            // Smaller, fully-inside-the-box title — _h2 was tall enough to clip at the screen top.
            Lbl(new Rect(20, 16, 336, 24), Loc.Get("hud.round_game", _router.RoundIndex + 1, game),
                new GUIStyle(_body) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = Ink } });
            Lbl(new Rect(20, 44, 336, 22), Loc.Get("hud.players_alive", alive), _body);

            DrawYourNumber(snap, w, h);
            DrawActivePowerups(snap);

            if (!_router.HasSeries) return;
            if (!_router.PlayStarted)
                OutlineLbl(new Rect(0, h * 0.45f, w, 60), Loc.Get("hud.countdown"), _h1, 3f, 6f);

            DrawControlsHint(current, w, h);
            DrawGameSpecific(snap, w, h);
        }

        // Your assigned tag, kept in front of you: it's the number floating over your
        // head and the one the announcer calls when you're eliminated ("Player N has
        // been eliminated."). A bold gold pill at BOTTOM-CENTER so your identity reads
        // near where your eyes sit during play — parked just ABOVE the transient caption
        // band (h-80) and the controls hint (h-36) so it never fights either; the few
        // games with bottom-centre controls (RPS/Simon) draw their panels on top. Solo
        // only — co-op has several locals, whose overhead tags cover that instead.
        private void DrawYourNumber(Snapshot snap, float w, float h)
        {
            var locals = _router.LocalPlayerIds;
            if (locals == null || locals.Count != 1 || snap?.Actors == null) return;
            int myNum = NumberInSnap(snap, locals[0]);
            if (myNum <= 0) return;

            string text = Loc.Get("hud.you_are", myNum);
            var style = new GUIStyle(_body) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Yellow } };
            float pw = Mathf.Max(150f, style.CalcSize(new GUIContent(text)).x + 36f), ph = 30f;
            var r = new Rect((w - pw) * 0.5f, h - 112f, pw, ph);
            Fill(r, Alpha(Color.black, 0.55f), ph * 0.5f);
            Stroke(r, Alpha(Yellow, 0.55f), 1.5f, ph * 0.5f);
            Lbl(r, text, style);
        }

        private GUIStyle _puPill;

        // Boomerang-Fu-style active-effect strip: the powerups you're carrying right
        // now, as a NAMED list down the top-left, so it's always clear what you've got.
        // BLESSINGS show a steady green bar (they stay with you); CURSES show a
        // draining red bar so you can see exactly when the affliction wears off.
        // Reads the local player's live status straight off the snapshot.
        // NOTE: in-process play only (solo / local co-op). Online's compact NetActor
        // wire format (Net/Wire.cs) doesn't carry the Pu* timers, so this strip stays
        // empty for a networked client — the pickup REVEAL still fires online (effects
        // are serialized), just not this persistent strip. Extend the wire for parity.
        private void DrawActivePowerups(Snapshot snap)
        {
            var locals = _router.LocalPlayerIds;
            if (locals == null || locals.Count == 0 || snap?.Actors == null) return;
            string myId = locals[0];
            Actor me = null;
            foreach (var a in snap.Actors) if (a.Id == myId) { me = a; break; }
            if (me == null || !me.Alive) return;

            var fx = new List<(string icon, string label, float frac, bool held)>();
            // Shared blessings (held) + curses (draining).
            AddHeld(fx, me.Shield, "Shield");
            AddHeld(fx, me.PuSpeedT > 0f, "Speed");
            AddHeld(fx, me.PuTinyT > 0f, "Tiny");
            AddHeld(fx, me.PuVisionT > 0f, "Vision");
            AddHeld(fx, me.PuCaffeineT > 0f, "Caffeine");
            AddHeld(fx, me.PuDisguiseT > 0f || !string.IsNullOrEmpty(me.DisguiseCharId), "Disguise");
            AddTimed(fx, me.PuReverseT, PowerupEffects.ReverseDuration, "Reverse");
            AddTimed(fx, me.PuSlowT, PowerupEffects.SlowDuration, "Slow");
            AddTimed(fx, me.PuGiantT, PowerupEffects.GiantDuration, "Giant");
            AddTimed(fx, me.PuDizzyT, PowerupEffects.DizzyDuration, "Dizzy");
            AddTimed(fx, me.PuSlipperyT, PowerupEffects.SlipperyDuration, "Slippery");
            // Boomerang's own combat drops (its per-actor scratch timers).
            if (snap.Data is Boomerang.BoomData)
            {
                AddTimed(fx, me.Get("speedT"), 8f, "Speed");
                AddTimed(fx, me.Get("bigT"), 10f, "BigRang");
                AddTimed(fx, me.Get("multiT"), 10f, "Multishot");
                AddTimed(fx, me.Get("magnetT"), 10f, "Magnet");
                AddTimed(fx, me.Get("tinyT"), 10f, "Tiny");
            }
            if (fx.Count == 0) return;

            if (_puPill == null)
                _puPill = new GUIStyle(_body) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };

            // A vertical list down the top-left, under the round box. Each row names the
            // powerup in green (a blessing you keep) or red (a curse that's draining),
            // with a matching status bar — so it reads even if the emoji glyph doesn't.
            float x = 12f, y = 82f;
            var good = new Color(0.55f, 0.92f, 0.70f);
            var curse = new Color(1f, 0.50f, 0.52f);
            const float ph = 24f;
            foreach (var e in fx)
            {
                Color c = e.held ? good : curse;
                string text = string.IsNullOrEmpty(e.icon) ? e.label : e.icon + "  " + e.label;
                float pw = Mathf.Max(96f, _puPill.CalcSize(new GUIContent(text)).x + 18f);
                var box = new Rect(x, y, pw, ph);
                Fill(box, new Color(0f, 0f, 0f, 0.62f), 7f);
                var st = new GUIStyle(_puPill) { normal = { textColor = c } };
                Lbl(new Rect(box.x + 8f, box.y, pw - 12f, ph - 3f), text, st);
                var track = new Rect(box.x + 6f, box.yMax - 4f, pw - 12f, 2.5f);
                Fill(track, new Color(1f, 1f, 1f, 0.14f), 1.25f);
                Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(e.frac), track.height), c, 1.25f);
                y += ph + 5f;
            }
        }

        private static void AddHeld(List<(string icon, string label, float frac, bool held)> fx, bool on, string key)
        { if (on && PowerupCatalog.TryGet(key, out var m)) fx.Add((m.Icon, m.Label, 1f, true)); }

        private static void AddTimed(List<(string icon, string label, float frac, bool held)> fx, float t, float max, string key)
        { if (t > 0f && PowerupCatalog.TryGet(key, out var m)) fx.Add((m.Icon, m.Label, Mathf.Clamp01(t / max), false)); }

        private void DrawControlsHint(GameId? game, float w, float h)
        {
            string hint = Loc.Get("hud.hint_default");
            if (game.HasValue)
            {
                switch (game.Value)
                {
                    case GameId.RedLight: hint = Loc.Get("hud.hint_redlight"); break;
                    case GameId.TugOfWar: hint = Loc.Get("hud.hint_tug"); break;
                    case GameId.Boomerang: hint = Loc.Get("hud.hint_boomerang"); break;
                    case GameId.Tag:
                    case GameId.Mingle:
                    case GameId.MusicalChairs: hint = Loc.Get("hud.hint_move_action"); break;
                    case GameId.Dodgeball: hint = Loc.Get("hud.hint_dodgeball"); break;
                    case GameId.KingOfTheHill: hint = Loc.Get("hud.hint_koth"); break;
                    case GameId.PropHunt: hint = Loc.Get("hud.hint_prophunt"); break;
                    case GameId.KeepyUppy: hint = Loc.Get("hud.hint_keepy"); break;
                    case GameId.GlassBridge: hint = Loc.Get("hud.hint_glass"); break;
                    case GameId.ChutesAndLadders: hint = Loc.Get("hud.hint_chutes"); break;
                    case GameId.JumpRope: hint = Loc.Get("hud.hint_jumprope"); break;
                    case GameId.SimonSays: hint = Loc.Get("hud.hint_simon"); break;
                    case GameId.RpsMinusOne: hint = Loc.Get("hud.hint_rps"); break;
                    case GameId.PresentSwap: hint = Loc.Get("hud.hint_present"); break;
                }
            }
            Lbl(new Rect(0, h - 46, w, 26), hint, new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter });
        }

        private void DrawGameSpecific(Snapshot snap, float w, float h)
        {
            switch (snap?.Data)
            {
                case RedLightGreenLight.RlglData rl:
                    // Lethal RED: pulse a red screen-edge so "do not move NOW" is unmissable (honours
                    // the reduce-flashing setting via a gentle, capped alpha).
                    if (rl.Lethal && (SaveService.Current?.settings?.reduceFlashAndShake != true))
                    {
                        float rp2 = 0.10f + 0.06f * Mathf.Abs(Mathf.Sin(Time.time * 7f));
                        var rc = new Color(1f, 0.04f, 0.12f, rp2);
                        Fill(new Rect(0, 0, w, 22), rc, 0f); Fill(new Rect(0, h - 22, w, 22), rc, 0f);
                        Fill(new Rect(0, 0, 22, h), rc, 0f); Fill(new Rect(w - 22, 0, 22, h), rc, 0f);
                    }
                    var c = rl.Red ? (rl.Lethal ? Color.red : new Color(1f, 0.5f, 0f)) : Color.green;
                    var prev = GUI.color; GUI.color = c;
                    GUI.Box(new Rect(w - 180, 10, 170, 40), rl.Red ? (rl.Lethal ? Loc.Get("hud.rl_red_freeze") : Loc.Get("hud.rl_red")) : Loc.Get("hud.rl_green"), _pill);
                    GUI.color = prev;
                    Lbl(new Rect(w - 180, 52, 170, 22), $"{rl.TimeLeft:0}s", new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontSize = 14 });
                    break;
                case TugOfWar.TugData tug:
                    float mid = w / 2f;
                    // Team headcounts flanking the rope bar (web shows TEAM 1 (n) / (m) TEAM 2).
                    Lbl(new Rect(mid - 360, 58, 150, 28), $"TEAM 1  ({tug.Team0Count})",
                        new GUIStyle(_body) { alignment = TextAnchor.MiddleRight, fontSize = 16, normal = { textColor = new Color(0.161f, 0.714f, 0.965f) } });
                    Lbl(new Rect(mid + 210, 58, 150, 28), $"({tug.Team1Count})  TEAM 2",
                        new GUIStyle(_body) { alignment = TextAnchor.MiddleLeft, fontSize = 16, normal = { textColor = new Color(1f, 0.435f, 0.612f) } });
                    GUI.Box(new Rect(mid - 200, 60, 400, 24), "");
                    // knob slides toward the winning team, matching the arena rope (mid − …).
                    float knob = mid - Mathf.Clamp(tug.RopePos, -1f, 1f) * 190f;
                    GUI.Box(new Rect(knob - 6, 60, 12, 24), "");
                    Lbl(new Rect(mid - 200, 86, 400, 20), Loc.Get("hud.time") + $" {tug.TimeLeft:0.0}s", new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter });
                    break;
                case Boomerang.BoomData boom:
                    Info(w, Loc.Get("hud.survive") + $" {boom.Alive}/{boom.Target}", $"{boom.TimeLeft:0}s " + Loc.Get("hud.left"));
                    break;
                case Tag.TagData tag:
                {
                    Info(w, $"FREEZERS {tag.FreezersAlive}  ·  RUNNERS {tag.RunnersAlive}", tag.DeepFreeze ? Loc.Get("hud.deep_freeze") : $"{tag.TimeLeft:0}s");
                    // Tell the player their role outright (the screenshot showed no clue which side
                    // you're on). Few freezers, many runners — so "RUN!" is the common case.
                    string tagId = (_router.LocalPlayerIds != null && _router.LocalPlayerIds.Count > 0) ? _router.LocalPlayerIds[0] : null;
                    Actor meTag = null;
                    if (tagId != null && snap.Actors != null) foreach (var a in snap.Actors) if (a.Id == tagId) { meTag = a; break; }
                    if (meTag != null && meTag.Alive)
                    {
                        bool freezer = meTag.Team == tag.FreezerTeamId;
                        string role = meTag.Frozen ? "FROZEN — a runner must thaw you!"
                                    : freezer ? "YOU'RE IT — freeze the runners!"
                                    : "RUN! — don't let a freezer touch you";
                        Color rc = meTag.Frozen ? new Color(0.6f, 0.85f, 1f) : freezer ? Teal : Pink;
                        bool deep = tag.DeepFreeze && !freezer && !meTag.Frozen;
                        TopBanner(w, role, rc, deep ? "DEEP FREEZE — no more thawing!" : null,
                                  deep ? new Color(1f, 0.5f, 0.5f) : (Color?)null, 27, 0.7f);
                    }
                    // Colour legend so the rings/crowns under the players are decodable — the icy crown
                    // = a FREEZER ("it"), the coral ring = a RUNNER, the gold ring = YOU.
                    var legR = new Rect(w / 2f - 258f, h - 68f, 516f, 26f);
                    Fill(legR, new Color(0f, 0f, 0f, 0.55f), 8f);
                    Lbl(new Rect(legR.x + 14f, legR.y, 250f, 26f), "■ FREEZER (icy crown) — chases you",
                        new GUIStyle(_body) { alignment = TextAnchor.MiddleLeft, fontSize = 13, normal = { textColor = new Color(0.45f, 0.8f, 1f) } });
                    Lbl(new Rect(legR.x + 270f, legR.y, 250f, 26f), "■ RUNNER — flee & thaw frozen friends",
                        new GUIStyle(_body) { alignment = TextAnchor.MiddleLeft, fontSize = 13, normal = { textColor = new Color(1f, 0.55f, 0.68f) } });
                    break;
                }
                case Dodgeball.DodgeData dodge:
                    Info(w, Loc.Get("hud.team_a") + $" {dodge.Team0Alive} — {dodge.Team1Alive} " + Loc.Get("hud.team_b"), $"{dodge.TimeLeft:0}s");
                    break;
                case KingOfTheHill.KothData koth:
                    Info(w, Loc.Get("hud.alive") + $" {koth.Alive}", $"{koth.TimeLeft:0}s — " + Loc.Get("hud.lava"));
                    if (koth.SuddenDeath)
                        TopBanner(w, "SUDDEN DEATH — one island left. Fight for it!",
                            new Color(1f, 0.4f, 0.3f), null, null, 26, 0.5f + 0.4f * Mathf.Sin(Time.time * 6f));
                    break;
                case MusicalChairs.McData mc:
                {
                    Info(w, $"{mc.Phase.ToUpper()} (" + Loc.Get("hud.round_lc") + $" {mc.Round})", mc.Fake ? Loc.Get("hud.psych") : $"{mc.TimeLeft:0.0}s");
                    // Big centered cue so the core rule is unmissable: you MUST keep moving while
                    // the music plays (freeze = out), then scramble for a chair when it stops.
                    string mp = mc.Phase == null ? "" : mc.Phase.ToLowerInvariant();
                    string mcBanner = null, mcSub = null; Color mcCol = Yellow;
                    if (mp == "music")
                    {
                        mcBanner = mc.Fake ? "PSYCH — KEEP MOVING!" : "KEEP MOVING!";
                        mcSub = "stop dancing and the floor claims you";
                        mcCol = mc.Fake ? Pink : Green;
                    }
                    else if (mp == "scramble")
                    {
                        mcBanner = "GRAB A CHAIR!";
                        mcSub = "one seat each — last one standing is out";
                        mcCol = Yellow;
                    }
                    // PERSONAL danger: if YOU'VE been standing still too long, slam a red alarm with
                    // the seconds-to-elimination so the feedback loop is unmissable.
                    string mcId = (_router.LocalPlayerIds != null && _router.LocalPlayerIds.Count > 0) ? _router.LocalPlayerIds[0] : null;
                    if (mp == "music" && mcId != null && mc.Warn != null)
                    {
                        foreach (var wv in mc.Warn)
                            if (wv.Id == mcId)
                            {
                                mcBanner = "MOVE!  MOVE!  MOVE!";
                                mcSub = $"the floor takes you in {wv.Left:0.0}s";
                                mcCol = new Color(1f, 0.28f, 0.28f);
                                break;
                            }
                    }
                    if (mcBanner != null)
                        TopBanner(w, mcBanner, mcCol, mcSub, null, 27, 0.75f);
                    break;
                }
                case Mingle.MingleData mingle:
                {
                    Info(w, mingle.Phase == "Mingle" ? Loc.Get("hud.group_of") + $" {mingle.N}!" : Loc.Get("hud.mingle_wait"), Loc.Get("hud.round_lc") + $" {mingle.Round} · {mingle.TimeLeft:0.0}s");
                    // The CALLED NUMBER is the whole game — show it big in the top strip (was a tiny
                    // top-right line, unreadable under the pile of players on the platform).
                    bool calling = mingle.Phase == "Mingle" || mingle.Phase == "Flash";
                    if (calling)
                        TopBanner(w, $"GROUP OF  {mingle.N}", Yellow, null, null, 32, 0.9f);
                    else
                        TopBanner(w, "MINGLE! — wait for the number…",
                            Alpha(Yellow, 0.6f + 0.3f * Mathf.Sin(Time.time * 3f)), null, null, 26, 0.7f);
                    break;
                }
                case GlassBridge.GlassData glass:
                {
                    Info(w, Loc.Get("hud.row") + $" {glass.Frontier + 1}/{glass.Rows}", glass.Phase == "choose" ? $"{Name(glass.ActiveId)} " + Loc.Get("hud.picks") + $" {glass.TurnTimeLeft:0.0}s" : glass.Phase);
                    string glassId = (_router.LocalPlayerIds != null && _router.LocalPlayerIds.Count > 0) ? _router.LocalPlayerIds[0] : null;
                    bool myTurn = glass.Phase == "choose" && glassId != null && glass.ActiveId == glassId;
                    if (glass.Phase == "choose")
                        // Whose turn it is — so the human knows when to act and why they're waiting
                        // (the old HUD gave NO turn cue, so a human never realised it was their go).
                        TopBanner(w, myTurn ? "YOUR TURN — STEP ONTO A PANE" : $"{Name(glass.ActiveId)} IS CHOOSING…",
                            myTurn ? Yellow : InkDim, null, null, 26, 0.6f);
                    if (myTurn)
                    {
                        // The two glass options are the TOP and BOTTOM lanes of the bridge (that's
                        // how the panes render), so label the buttons to match — not "left/right".
                        float by = h - 168f;
                        var tR = new Rect(w / 2f - 74f, by - 44f, 148f, 26f);
                        Fill(tR, new Color(0f, 0f, 0f, 0.6f), 8f);
                        OutlineLbl(tR, $"{glass.TurnTimeLeft:0.0}s",
                            new GUIStyle(_ui) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = glass.TurnTimeLeft > 2.5f ? Color.white : new Color(1f, 0.4f, 0.4f) } }, 2f, 3f);
                        // A draining bar makes the urgency glanceable (turns red in the last 2.5s).
                        float gtl = Mathf.Clamp01(glass.TurnTimeLeft / 7f);
                        float gbw = 300f, gbx = w / 2f - gbw / 2f, gby = by - 14f;
                        Fill(new Rect(gbx, gby, gbw, 8f), new Color(0f, 0f, 0f, 0.5f), 4f);
                        Fill(new Rect(gbx, gby, gbw * gtl, 8f), glass.TurnTimeLeft > 2.5f ? Green : new Color(1f, 0.32f, 0.32f), 4f);
                        if (Btn(new Rect(w / 2f - 120f, by, 240f, 50f), "▲  TOP PANE", Teal, OnDark, true, 20)) _router.SubmitFor(glassId, GameInput.Choose("L"));
                        if (Btn(new Rect(w / 2f - 120f, by + 56f, 240f, 50f), "▼  BOTTOM PANE", Pink, OnDark, true, 20)) _router.SubmitFor(glassId, GameInput.Choose("R"));
                    }
                    break;
                }
                case JumpRope.RopeData rope:
                {
                    Info(w, Loc.Get("hud.swing") + $" {rope.Swing}", Loc.Get("hud.bridge") + $" {rope.BridgeLen} " + Loc.Get("hud.planks"));
                    string jrId = (_router.LocalPlayerIds != null && _router.LocalPlayerIds.Count > 0) ? _router.LocalPlayerIds[0] : null;
                    JumpRope.JumperView me = default; bool haveMe = false; int across = 0;
                    if (rope.Jumpers != null)
                        foreach (var j in rope.Jumpers) { if (j.Crossed) across++; if (j.Id == jrId) { me = j; haveMe = true; } }

                    // Progress bar — how far the local jumper has crossed (was buried in the corner box).
                    if (haveMe && me.Alive && !me.Crossed && rope.BridgeLen > 0)
                    {
                        float prog = Mathf.Clamp01(me.Pos / (float)rope.BridgeLen);
                        float barW = 360f, bx = w / 2f - barW / 2f, by = h * 0.12f;
                        Fill(new Rect(bx, by, barW, 16f), new Color(0f, 0f, 0f, 0.5f), 6f);
                        Fill(new Rect(bx, by, barW * prog, 16f), prog > 0.66f ? Green : (prog > 0.33f ? Yellow : Teal), 6f);
                        OutlineLbl(new Rect(bx, by - 22f, barW, 20f), $"PLANK {me.Pos}/{rope.BridgeLen}",
                            new GUIStyle(_body) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } }, 1.5f, 2f);
                    }

                    // Timing keyed to the ACTUAL jump window: a jump (≈0.46s airborne) lands you safe
                    // only if the rope sweeps the deck within that window. ttg = seconds until it does.
                    float ttg = Mathf.Max(0f, (1f - rope.Phase) * Mathf.Max(0.0001f, rope.Period));
                    float danger = Mathf.Clamp01(1f - ttg / 0.7f); // ramps as the rope descends
                    bool jumpNow = ttg < 0.46f;                     // the exact "jump and you make it" window
                    float mw = 300f, mx = w / 2f - mw / 2f, my = h * 0.30f;
                    Fill(new Rect(mx, my, mw, 12f), new Color(0f, 0f, 0f, 0.5f), 6f);
                    Fill(new Rect(mx, my, mw * danger, 12f), Color.Lerp(Green, new Color(1f, 0.25f, 0.25f), danger), 6f);
                    if (jumpNow && haveMe && me.Alive && !me.Crossed)
                    {
                        var jR = new Rect(w / 2f - 150f, my - 78f, 300f, 64f);
                        Fill(jR, new Color(0.2f, 0f, 0f, 0.5f), 12f);
                        OutlineLbl(jR, "JUMP!", new GUIStyle(_h1) { fontSize = 46, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.3f, 0.3f) } }, 3.5f, 6f);
                    }
                    if (across > 0)
                        Lbl(new Rect(w / 2f - 100f, h * 0.30f + 18f, 200f, 22f), $"{across} ACROSS",
                            new GUIStyle(_body) { fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = Teal } });
                    break;
                }
                case RpsMinusOne.RpsData rps:
                {
                    Info(w, $"RPS · {rps.Phase.ToUpper()}", Loc.Get("hud.round_lc") + $" {rps.Round} · {rps.TimeLeft:0.0}s");
                    // The human was previously given NO way to play (silent forfeit every duel).
                    // Wire real RPS controls: pick TWO throws, then KEEP one. The sim accumulates
                    // single-letter Choose inputs, so each button press is one throw.
                    string rpsId = (_router.LocalPlayerIds != null && _router.LocalPlayerIds.Count > 0) ? _router.LocalPlayerIds[0] : null;
                    var myDuel = (rpsId == null || rps.Duels == null) ? null : rps.Duels.FirstOrDefault(d => d.A == rpsId || d.B == rpsId);
                    if (myDuel != null && myDuel.Status != "done")
                    {
                        bool isA = myDuel.A == rpsId;
                        var myThrows = isA ? myDuel.AThrows : myDuel.BThrows;
                        string myKeep = isA ? myDuel.AKeep : myDuel.BKeep;
                        string oppId = isA ? myDuel.B : myDuel.A;
                        string[] opts = { "R", "P", "S" };
                        string[] lbl = { "ROCK", "PAPER", "SCISSORS" };
                        Color[] cols = { Teal, Green, Pink }; // distinct per throw so they don't blur together
                        float bx = w / 2f - 192f, by = h - 210f; // raised so the instruction pill can't crop off the bottom

                        // Instruction + a BIG countdown sit right above the buttons (the top-right
                        // clock was too far from the action — you couldn't tell how long you had).
                        string head = rps.Phase == "pick" ? "PICK TWO THROWS"
                                    : rps.Phase == "drop" ? "DROP ONE — KEEP THE OTHER"
                                    : "THROW!";
                        var headR = new Rect(w / 2f - 230f, by - 84f, 460f, 34f);
                        Fill(headR, new Color(0f, 0f, 0f, 0.66f), 10f);
                        OutlineLbl(headR, string.IsNullOrEmpty(oppId) ? head : $"{head}   ·   vs {Name(oppId)}",
                            new GUIStyle(_ui) { fontSize = 20, alignment = TextAnchor.MiddleCenter, normal = { textColor = Yellow } }, 2f, 3f);
                        var tR = new Rect(w / 2f - 74f, by - 46f, 148f, 34f);
                        Fill(tR, new Color(0f, 0f, 0f, 0.66f), 9f);
                        OutlineLbl(tR, $"{rps.TimeLeft:0.0}s",
                            new GUIStyle(_ui) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = rps.TimeLeft > 2f ? Color.white : new Color(1f, 0.4f, 0.4f) } }, 2.5f, 4f);

                        if (rps.Phase == "pick")
                        {
                            for (int i = 0; i < 3; i++)
                                if (Btn(new Rect(bx + i * 130f, by, 122f, 58f), lbl[i], cols[i], OnDark, true, 18))
                                    _router.SubmitFor(rpsId, GameInput.Choose(opts[i]));
                            string chosen = (myThrows == null || myThrows.Count == 0)
                                ? "tap any TWO (repeats ok)"
                                : "chosen:  " + string.Join("  +  ", myThrows.Select(RpsThrowName));
                            GUI.Box(new Rect(bx, by + 62f, 384f, 24f), chosen, _pill);
                        }
                        else if (rps.Phase == "drop" && myThrows != null && myThrows.Count >= 2)
                        {
                            for (int i = 0; i < 2; i++)
                            {
                                string t = myThrows[i];
                                int ci = t == "R" ? 0 : t == "P" ? 1 : 2;
                                bool kept = myKeep == t;
                                if (Btn(new Rect(bx + i * 196f, by, 188f, 58f), (kept ? "KEEP: " : "") + lbl[ci], kept ? Green : cols[ci], OnDark, true, 18))
                                    _router.SubmitFor(rpsId, GameInput.Choose(t));
                            }
                            GUI.Box(new Rect(bx, by + 62f, 384f, 24f), string.IsNullOrEmpty(myKeep) ? "tap the throw you'll KEEP" : "locked in — good luck!", _pill);
                        }
                    }
                    break;
                }
                case SimonSays.SimonData simon:
                {
                    // Phase-aware: SimonSays only accepts input during the brief `call`
                    // window, so we prompt the player to press ONLY then (with the single
                    // correct key + a draining reaction bar). In ready/judge the press
                    // prompt is hidden so a press isn't silently dropped — that phase-blind
                    // HUD is exactly why it felt like "Simon Says doesn't work at all".
                    bool simCall = simon.Phase == "call";
                    Color simAccent = simon.Command == null ? Ink : (simon.Freeze ? Teal : Yellow);
                    Color simRed = new Color(1f, 0.35f, 0.35f);
                    string banner = simon.Command == null ? "SIMON SAYS…"
                        : simon.Freeze ? Loc.Get("hud.simon_freeze_banner")
                        : Loc.Get("hud.simon_banner") + $" {simon.Command.ToUpper()}";
                    TopBanner(w, banner, simAccent, null, null, 27, 0.7f);
                    if (simCall)
                    {
                        string prompt = simon.Freeze ? "DON'T TOUCH ANYTHING" : $"PRESS  {SimonKeyCap(simon.Command)}";
                        var pRect = new Rect(w / 2f - 170, h * 0.16f + 52, 340, 40);
                        Fill(pRect, new Color(0f, 0f, 0f, 0.62f), 10f);
                        OutlineLbl(pRect, prompt, new GUIStyle(_ui) { fontSize = 20, alignment = TextAnchor.MiddleCenter, normal = { textColor = simon.Freeze ? Teal : Color.white } }, 2f, 3f);
                        float bw = 340f, bx = w / 2f - 170f, by = h * 0.16f + 98f;
                        GUI.Box(new Rect(bx, by, bw, 12), "");
                        float left = Mathf.Clamp01(1f - simon.React);
                        var bc = GUI.color; GUI.color = left > 0.5f ? Green : (left > 0.25f ? Yellow : simRed);
                        GUI.Box(new Rect(bx, by, bw * left, 12), "");
                        GUI.color = bc;
                    }
                    else if (simon.Phase == "judge")
                    {
                        int outNow = 0;
                        if (simon.Contestants != null) foreach (var ct in simon.Contestants) if (ct.Result == "out") outNow++;
                        var jRect = new Rect(w / 2f - 170, h * 0.16f + 52, 340, 34);
                        Fill(jRect, new Color(0f, 0f, 0f, 0.62f), 10f);
                        OutlineLbl(jRect, outNow > 0 ? $"{outNow} OUT" : "EVERYONE OBEYED",
                            new GUIStyle(_ui) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = outNow > 0 ? simRed : Green } }, 2f, 3f);
                    }

                    // Personal verdict in the action zone: the player needs explicit "you got it
                    // right / wrong" feedback, not just silently dying when wrong (the whole ask).
                    string simId = (_router.LocalPlayerIds != null && _router.LocalPlayerIds.Count > 0) ? _router.LocalPlayerIds[0] : null;
                    bool haveMe = false; SimonSays.SimonContestant meCt = default;
                    if (simId != null && simon.Contestants != null)
                        foreach (var ct in simon.Contestants) if (ct.Id == simId) { meCt = ct; haveMe = true; break; }
                    if (haveMe)
                    {
                        float fy = h - 150f;
                        if (simon.Phase == "judge")
                        {
                            bool safe = meCt.Result != "out";
                            var vR = new Rect(w / 2f - 170f, fy, 340f, 56f);
                            Fill(vR, new Color(0f, 0f, 0f, 0.72f), 12f);
                            Stroke(vR, safe ? Green : simRed, 3f, 12f);
                            OutlineLbl(vR, safe ? "GOOD — YOU'RE SAFE" : "WRONG — YOU'RE OUT",
                                new GUIStyle(_ui) { fontSize = 24, alignment = TextAnchor.MiddleCenter, normal = { textColor = safe ? Green : simRed } }, 3f, 4f);
                        }
                        else if (simCall && meCt.Alive)
                        {
                            // Confirm the press registered (so a correct input isn't invisible), and
                            // gently warn on FREEZE that any press is a twitch-out.
                            string note = meCt.Did != null
                                ? "LOCKED IN:  " + meCt.Did.ToUpper()
                                : (simon.Freeze ? "HOLD STILL — don't press!" : "press the command now");
                            Color nc = meCt.Did != null ? (simon.Freeze ? simRed : Green) : (simon.Freeze ? Teal : Color.white);
                            var nR = new Rect(w / 2f - 150f, fy + 12f, 300f, 34f);
                            Fill(nR, new Color(0f, 0f, 0f, 0.62f), 9f);
                            OutlineLbl(nR, note, new GUIStyle(_ui) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = nc } }, 2f, 3f);
                        }
                    }
                    Info(w, Loc.Get("hud.beat") + $" {simon.Beat}/{simon.MaxBeats}",
                        simCall ? "GO — do it NOW!" : (simon.Command == null ? "get ready…" : "…hold"));
                    break;
                }
                case ChutesAndLadders.ChutesData chutes:
                {
                    string clId = (_router.LocalPlayerIds != null && _router.LocalPlayerIds.Count > 0) ? _router.LocalPlayerIds[0] : null;
                    DrawChutesBoard(snap, chutes, clId, w, h); // a big, numbered 2D board (the 3D arena can't show numbers)
                    break;
                }
                case PresentSwap.PresentData present:
                {
                    Info(w, present.Phase == "gift" ? Loc.Get("hud.lights_out") : (present.Phase == "guess" ? Loc.Get("hud.guess_giver") : Loc.Get("hud.reveal")), Loc.Get("hud.round_lc") + $" {present.Round} · {present.TimeLeft:0.0}s");
                    // The human receiver had no way to guess their giver → guaranteed "fooled"
                    // (eliminated) every round. Give them the suspect buttons (the web's guess UI).
                    string presId = (_router.LocalPlayerIds != null && _router.LocalPlayerIds.Count > 0) ? _router.LocalPlayerIds[0] : null;
                    if (presId != null && present.Events != null)
                    {
                        // GIFT phase: a human giver picks WHO to sabotage from their secret slate
                        // (previously only bots could gift — the human's whole decision was missing).
                        if (present.Phase == "gift")
                        {
                            PresentSwap.EventView mine = null;
                            foreach (var e in present.Events) if (e.GiverId == presId) { mine = e; break; }
                            if (mine != null && mine.TargetSlate != null && mine.TargetSlate.Count > 0)
                            {
                                string head = mine.Gifted ? "Gift sent — sit tight…" : "Choose who gets YOUR gift — tap a target:";
                                GUI.Box(new Rect(w / 2f - 240f, h - 162f, 480f, 26f), head, _pill);
                                if (!mine.Gifted)
                                {
                                    int n = mine.TargetSlate.Count;
                                    float bw = Mathf.Min(150f, (480f - (n - 1) * 8f) / n);
                                    float total = n * bw + (n - 1) * 8f;
                                    float bx = w / 2f - total / 2f, by = h - 128f;
                                    for (int i = 0; i < n; i++)
                                    {
                                        string cand = mine.TargetSlate[i];
                                        if (Btn(new Rect(bx + i * (bw + 8f), by, bw, 46f), Name(cand), Pink, OnDark, true, 16))
                                            _router.SubmitFor(presId, GameInput.Choose(cand));
                                    }
                                }
                            }
                        }
                        else if (present.Phase == "guess")
                        {
                            // Receivers guess their giver…
                            PresentSwap.EventView mine = null, asGiver = null;
                            foreach (var e in present.Events)
                            {
                                if (e.ReceiverId == presId) mine = e;
                                if (e.GiverId == presId) asGiver = e;
                            }
                            if (mine != null && !mine.Guessed && mine.CandidateIds != null && mine.CandidateIds.Count > 0)
                            {
                                GUI.Box(new Rect(w / 2f - 230f, h - 162f, 460f, 26f), "Who gave you this gift? Tap a suspect:", _pill);
                                int n = mine.CandidateIds.Count;
                                float bw = Mathf.Min(150f, (460f - (n - 1) * 8f) / n);
                                float total = n * bw + (n - 1) * 8f;
                                float bx = w / 2f - total / 2f, by = h - 128f;
                                for (int i = 0; i < n; i++)
                                {
                                    string cand = mine.CandidateIds[i];
                                    if (Btn(new Rect(bx + i * (bw + 8f), by, bw, 46f), Name(cand), Yellow, OnGold, true, 16))
                                        _router.SubmitFor(presId, GameInput.Choose(cand));
                                }
                            }
                            // …and a giver sweats over whether their victim catches them.
                            if (asGiver != null && !string.IsNullOrEmpty(asGiver.ReceiverId))
                            {
                                var gR = new Rect(w / 2f - 230f, h - 196f, 460f, 26f);
                                Fill(gR, new Color(0f, 0f, 0f, 0.55f), 8f);
                                OutlineLbl(gR, $"Your gift's with {Name(asGiver.ReceiverId)} — don't get caught!",
                                    new GUIStyle(_body) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = Pink } }, 1.5f, 2f);
                            }
                        }
                    }
                    break;
                }
                case PropHunt.PropData prop:
                {
                    bool hunt = prop.Phase != "hide";
                    Info(w, prop.Phase == "hide" ? Loc.Get("hud.disguise") : Loc.Get("hud.found") + $" {prop.Found}/{prop.Quota}",
                        hunt ? $"{prop.HidersLeft} " + Loc.Get("hud.hiding") + $" · swings {prop.Swings}/{prop.MaxSwings} · {prop.TimeLeft:0}s"
                             : $"{prop.HidersLeft} " + Loc.Get("hud.hiding") + $" · {prop.TimeLeft:0}s");
                    // Phase banner so everyone knows the beat.
                    TopBanner(w, hunt ? $"THE HUNT — {prop.TimeLeft:0}s" : $"DISGUISE!  hide among the props — {prop.TimeLeft:0}s",
                        hunt ? new Color(1f, 0.33f, 0.33f) : Green, null, null, 26, 0.55f);
                    // Seeker blackout during hide: no peeking while the hiders disguise.
                    string phId = (_router.LocalPlayerIds != null && _router.LocalPlayerIds.Count > 0) ? _router.LocalPlayerIds[0] : null;
                    if (prop.Phase == "hide" && phId != null && phId == prop.SeekerId)
                    {
                        Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.92f), 0f);
                        OutlineLbl(new Rect(0, h * 0.42f, w, 50), "NO PEEKING, SEEKER",
                            new GUIStyle(_h1) { alignment = TextAnchor.MiddleCenter }, 2f, 4f);
                        Lbl(new Rect(0, h * 0.52f, w, 30), $"the props are hiding… {prop.TimeLeft:0}s",
                            new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
                    }
                    break;
                }
                case KeepyUppy.KeepyData keepy:
                    Info(w, Loc.Get("hud.alive") + $" {keepy.Alive}", $"{keepy.TimeLeft:0}s — " + Loc.Get("hud.dont_drop"));
                    break;
            }
        }

        /// <summary>R/P/S → the full throw name for the RPS pick read-out.</summary>
        private static string RpsThrowName(string t)
            => t == "R" ? "Rock" : t == "P" ? "Paper" : t == "S" ? "Scissors" : t;

        /// <summary>The keyboard key that performs a Simon command (W/A/S/D/Space),
        /// matching LocalInputHub's mapping, so the prompt names the exact key to press.</summary>
        private static string SimonKeyCap(string cmd)
        {
            switch (cmd)
            {
                case "head": return "W";
                case "nose": return "A";
                case "blink": return "S";
                case "flip": return "D";
                case "jump": return "Space";
                default: return cmd?.ToUpper() ?? "?";
            }
        }

        // ── Chutes & Ladders: a full 2D board overlay ───────────────────────
        // Rendered in the HUD (NOT the tilted 3D arena, which can't show square NUMBERS and let the
        // neon floor bleed through) — a big numbered serpentine grid on a clean dark panel, with
        // real ladders, chute-forks, pawns clustered per square, a timer bar, and the dice/roll/fork
        // controls in a side column. Covers the arena entirely so it reads as a proper board game.
        private void DrawChutesBoard(Snapshot snap, ChutesAndLadders.ChutesData ch, string clId, float w, float h)
        {
            int cols = Mathf.Max(1, ch.Cols);
            int rows = Mathf.Max(1, (ch.Goal + cols - 1) / cols);

            // Full-screen dark backdrop (hides the clashing arena floor + 3D blobs).
            Fill(new Rect(0, 0, w, h), new Color(0.09f, 0.05f, 0.15f, 1f), 0f);
            Fill(new Rect(0, 0, w, h * 0.6f), new Color(0.16f, 0.09f, 0.26f, 0.5f), 0f); // soft top glow

            // Board geometry: as large as fits, left-aligned, a control column on the right.
            const float ctrlW = 300f;
            float boardSize = Mathf.Clamp(Mathf.Min(h - 96f, w - ctrlW - 80f), 240f, 720f);
            float bx = 44f, by = Mathf.Max((h - boardSize) * 0.5f + 6f, 74f); // keep clear of the top header
            float cw = boardSize / cols, chh = boardSize / rows;
            float ctrlX = bx + boardSize + 34f;

            Vector2 Cell(int sq)
            {
                if (sq <= 0) return new Vector2(bx + cw * 0.5f, by + boardSize + chh * 0.55f);
                int s0 = Mathf.Min(sq, ch.Goal) - 1;
                int r = s0 / cols, within = s0 % cols;
                int col = (r % 2 == 0) ? within : cols - 1 - within; // serpentine
                return new Vector2(bx + col * cw + cw * 0.5f, by + (rows - 1 - r) * chh + chh * 0.5f);
            }

            // Board panel + frame.
            Fill(new Rect(bx - 10f, by - 10f, boardSize + 20f, boardSize + 20f), new Color(0.16f, 0.10f, 0.26f), 12f);
            // Numbered checker cells.
            int numFont = Mathf.Max(9, (int)(cw * 0.24f));
            for (int s = 1; s <= ch.Goal; s++)
            {
                Vector2 c = Cell(s);
                int s0 = s - 1, r = s0 / cols, within = s0 % cols;
                int col = (r % 2 == 0) ? within : cols - 1 - within;
                bool checker = (r + col) % 2 == 0;
                bool goal = s == ch.Goal;
                var cr = new Rect(c.x - cw * 0.5f + 1.5f, c.y - chh * 0.5f + 1.5f, cw - 3f, chh - 3f);
                Fill(cr, goal ? new Color(0.30f, 0.92f, 0.55f, 0.30f) : new Color(1f, 1f, 1f, checker ? 0.075f : 0.03f), 4f);
                Lbl(new Rect(c.x - cw * 0.5f + 5f, c.y - chh * 0.5f + 3f, cw, numFont + 6f), goal ? $"GOAL {ch.Goal}" : s.ToString(),
                    new GUIStyle(_body) { fontSize = numFont, alignment = TextAnchor.UpperLeft, normal = { textColor = goal ? Green : new Color(0.80f, 0.74f, 0.92f, 0.55f) } });
            }
            Stroke(new Rect(bx, by, boardSize, boardSize), new Color(1f, 1f, 1f, 0.22f), 2.5f, 6f);

            // Ladders (climb up).
            if (ch.Ladders != null)
                foreach (var l in ch.Ladders) DrawLadder2D(Cell(l[0]), Cell(l[1]), Mathf.Min(cw, chh));
            // Chute forks (one neighbour resets you, one is the abyss).
            if (ch.Chutes != null)
                foreach (var cv in ch.Chutes)
                    DrawChuteFork2D(Cell(cv.Square), Cell(Mathf.Max(1, cv.Square - 1)), Cell(Mathf.Min(ch.Goal, cv.Square + 1)), cv.Left, cv.Right, Mathf.Min(cw, chh));

            // The Squid Game doll (same caller as Red-Light / Simon) presides over the FINISH —
            // reach her square or be eliminated. We draw the REAL 3D model via an offscreen render
            // (DollPortrait); if that's unavailable she falls back to a procedural drawing. Bobs so
            // she feels alive and watching.
            {
                Vector2 g = Cell(ch.Goal);
                float unit = Mathf.Min(cw, chh);
                float bob = Mathf.Sin(Time.time * 2.4f) * unit * 0.05f;
                Texture dollTex = DollPortrait.Texture;
                if (dollTex != null)
                {
                    float dw = unit * 1.05f, dh = unit * 1.45f;
                    GUI.DrawTexture(new Rect(g.x - dw * 0.5f, g.y - dh * 0.58f + bob, dw, dh), dollTex, ScaleMode.ScaleToFit, true);
                }
                else DrawDoll2D(g.x, g.y - unit * 0.08f + bob, unit * 1.18f);
            }

            // Pawns, clustered per square so a crowd doesn't fully stack.
            float pawnR = Mathf.Max(8f, Mathf.Min(cw, chh) * 0.26f);
            var bySquare = new Dictionary<int, List<ChutesAndLadders.ClimberView>>();
            if (ch.Climbers != null)
                foreach (var cvw in ch.Climbers)
                {
                    int key = cvw.Alive ? cvw.Square : -1000 - cvw.Square;
                    if (!bySquare.TryGetValue(key, out var lst)) { lst = new List<ChutesAndLadders.ClimberView>(); bySquare[key] = lst; }
                    lst.Add(cvw);
                }
            foreach (var grp in bySquare.Values)
                for (int i = 0; i < grp.Count; i++)
                {
                    var cvw = grp[i];
                    Vector2 c = Cell(cvw.Square);
                    if (grp.Count > 1)
                    {
                        float ang = (i / (float)grp.Count) * 6.2831853f, rad = pawnR * (grp.Count > 4 ? 1.5f : 1.1f);
                        c += new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
                    }
                    bool isMe = cvw.Id == clId;
                    Color body = PawnColor(snap, cvw.Id);
                    if (!cvw.Alive) body = new Color(body.r * 0.4f, body.g * 0.4f, body.b * 0.4f, 0.6f);
                    var pr = new Rect(c.x - pawnR, c.y - pawnR, pawnR * 2f, pawnR * 2f);
                    Fill(new Rect(pr.x + 2f, pr.y + 3f, pr.width, pr.height), new Color(0f, 0f, 0f, 0.35f), pawnR); // shadow
                    if (isMe) Stroke(new Rect(pr.x - 3f, pr.y - 3f, pr.width + 6f, pr.height + 6f), Yellow, 3f, pawnR + 3f);
                    Fill(pr, body, pawnR);
                    Stroke(pr, new Color(1f, 1f, 1f, 0.5f), 1.5f, pawnR);
                    if (cvw.Finished) Lbl(pr, "✓", new GUIStyle(_ui) { fontSize = (int)(pawnR * 1.1f), alignment = TextAnchor.MiddleCenter, normal = { textColor = Green } });
                }

            // Header (round + alive) — re-drawn since our backdrop covers the default top box.
            int aliveN = snap?.Actors?.Count(a => a.Alive) ?? 0;
            Lbl(new Rect(20, 14, 360, 24), Loc.Get("hud.round_game", _router.RoundIndex + 1, GameName(GameId.ChutesAndLadders)),
                new GUIStyle(_body) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = Ink } });
            Lbl(new Rect(20, 40, 360, 22), Loc.Get("hud.players_alive", aliveN), _body);

            // ── Control column: a prominent timer, then dice + ROLL, or the FORK picker ──
            float frac = ch.Duration > 0f ? Mathf.Clamp01(ch.TimeLeft / ch.Duration) : 0f;
            Color tcol = ch.TimeLeft <= 5f ? new Color(1f, 0.3f, 0.42f) : ch.TimeLeft <= 12f ? Yellow : Green;
            Lbl(new Rect(ctrlX, by, ctrlW - 30f, 26f), $"RACE TO {ch.Goal}  ·  {Mathf.CeilToInt(ch.TimeLeft)}s",
                new GUIStyle(_ui) { fontSize = 19, alignment = TextAnchor.MiddleCenter, normal = { textColor = tcol } });
            Fill(new Rect(ctrlX, by + 28f, ctrlW - 30f, 8f), new Color(1f, 1f, 1f, 0.12f), 4f);
            Fill(new Rect(ctrlX, by + 28f, (ctrlW - 30f) * frac, 8f), tcol, 4f);

            ChutesAndLadders.ClimberView me = default; bool haveMe = false;
            if (clId != null && ch.Climbers != null)
                foreach (var cvw in ch.Climbers) if (cvw.Id == clId) { me = cvw; haveMe = true; break; }
            float colCx = ctrlX + (ctrlW - 30f) * 0.5f, cy = by + 92f;
            Lbl(new Rect(ctrlX, cy - 30f, ctrlW - 30f, 24f), me.Choosing >= 0 ? "PICK A FORK!" : "ROLL TO CLIMB", new GUIStyle(_ui) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = Yellow } });
            if (haveMe && me.Alive && !me.Finished)
            {
                if (me.Choosing >= 0)
                {
                    int lo = -1, ro = -1;
                    if (ch.Chutes != null) foreach (var cw2 in ch.Chutes) if (cw2.Id == me.Choosing) { lo = cw2.Left; ro = cw2.Right; break; }
                    GUI.Box(new Rect(ctrlX, cy, ctrlW - 30f, 30f), "FORK — one side resets you, one is the ABYSS", _pill);
                    string lLbl = lo == 1 ? "▲ ABYSS" : lo == 0 ? "▲ BACK TO START" : "▲ LEFT";
                    string rLbl = ro == 1 ? "▼ ABYSS" : ro == 0 ? "▼ BACK TO START" : "▼ RIGHT";
                    if (Btn(new Rect(ctrlX, cy + 40f, ctrlW - 30f, 56f), lLbl, Teal, OnDark, true, 18)) _router.SubmitFor(clId, GameInput.Choose("L"));
                    if (Btn(new Rect(ctrlX, cy + 104f, ctrlW - 30f, 56f), rLbl, Pink, OnDark, true, 18)) _router.SubmitFor(clId, GameInput.Choose("R"));
                }
                else
                {
                    var dieR = new Rect(colCx - 44f, cy + 4f, 88f, 88f);
                    Fill(dieR, new Color(0.95f, 0.95f, 0.97f), 12f);
                    if (me.Die > 0) DrawDiePips(dieR, me.Die);
                    else Lbl(dieR, "?", new GUIStyle(_h1) { fontSize = 50, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.6f, 0.6f, 0.7f) } });
                    if (Btn(new Rect(ctrlX, cy + 108f, ctrlW - 30f, 60f), "ROLL  (Space)", Yellow, OnGold, true, 22)) _router.SubmitFor(clId, GameInput.Tap());
                    Lbl(new Rect(ctrlX, cy + 174f, ctrlW - 30f, 22f), me.Die > 0 ? $"rolled {me.Die} — climbing…" : "tap to roll the die",
                        new GUIStyle(_body) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.78f, 0.9f) } });
                }
            }
            else if (haveMe && me.Finished)
                Lbl(new Rect(ctrlX, cy + 30f, ctrlW - 30f, 30f), "SAFE — you made it!", new GUIStyle(_ui) { fontSize = 20, alignment = TextAnchor.MiddleCenter, normal = { textColor = Green } });

            // legend
            float ly = by + boardSize - 84f;
            Lbl(new Rect(ctrlX, ly, ctrlW - 30f, 22f), "▮ ladder — climb up", new GUIStyle(_body) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.86f, 0.66f, 0.38f) } });
            Lbl(new Rect(ctrlX, ly + 24f, ctrlW - 30f, 22f), "▮ fork — gamble: reset or abyss", new GUIStyle(_body) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.69f, 0.42f, 0.90f) } });
            Lbl(new Rect(ctrlX, ly + 48f, ctrlW - 30f, 22f), "● = a racer   ◉ gold = YOU", new GUIStyle(_body) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.85f, 0.95f) } });

            // Keep the pre-round GO countdown legible on top of our takeover.
            if (!_router.PlayStarted)
                OutlineLbl(new Rect(0, h * 0.42f, w, 60), Loc.Get("hud.countdown"), _h1, 3f, 6f);
        }

        // A thick 2D line between two screen points (rotated rounded bar).
        private void Line2D(Vector2 a, Vector2 b, float thick, Color c, float radius = 0f)
        {
            Vector2 mid = (a + b) * 0.5f; float len = Vector2.Distance(a, b);
            if (len < 0.5f) return;
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, mid);
            Fill(new Rect(mid.x - len * 0.5f, mid.y - thick * 0.5f, len, thick), c, radius);
            GUI.matrix = m;
        }

        // A wooden ladder: two rails + rungs, with an up-arrow head at the top (the 'to' end).
        private void DrawLadder2D(Vector2 a, Vector2 b, float cell)
        {
            Vector2 dir = (b - a); float len = dir.magnitude; if (len < 1f) return; dir /= len;
            Vector2 perp = new Vector2(-dir.y, dir.x) * Mathf.Clamp(cell * 0.17f, 5f, 15f);
            var rail = new Color(0.86f, 0.66f, 0.38f); var rung = new Color(0.72f, 0.50f, 0.26f);
            Line2D(a + perp, b + perp, 4f, rail, 2f);
            Line2D(a - perp, b - perp, 4f, rail, 2f);
            int n = Mathf.Clamp(Mathf.RoundToInt(len / 22f), 2, 14);
            for (int i = 1; i < n; i++) { Vector2 p = Vector2.Lerp(a, b, i / (float)n); Line2D(p + perp, p - perp, 3f, rung, 1.5f); }
        }

        // A chute fork: a hazard mouth on the chute square, slide-arms to the two neighbour squares,
        // tinted by outcome (red = abyss, cyan = back-to-start, purple = unknown).
        private void DrawChuteFork2D(Vector2 from, Vector2 left, Vector2 right, int lo, int ro, float cell)
        {
            Color C(int o) => o == 1 ? new Color(1f, 0.30f, 0.43f) : o == 0 ? new Color(0.15f, 0.78f, 0.86f) : new Color(0.69f, 0.42f, 0.90f);
            Line2D(from, left, 6f, C(lo), 3f);
            Line2D(from, right, 6f, C(ro), 3f);
            float hr = Mathf.Max(7f, cell * 0.30f);
            Fill(new Rect(from.x - hr, from.y - hr, hr * 2f, hr * 2f), new Color(0.49f, 0.25f, 0.72f, 0.55f), hr);
            float er = Mathf.Max(5f, cell * 0.16f);
            Fill(new Rect(left.x - er, left.y - er, er * 2f, er * 2f), C(lo), er);
            Fill(new Rect(right.x - er, right.y - er, er * 2f, er * 2f), C(ro), er);
        }

        // The Squid Game doll ("Younghee"), drawn flat for the 2D Chutes board's finish square —
        // orange pinafore, pale round head, bowl-cut bangs, two ribboned pigtails, red scanning eyes.
        // (cx,cy) is the doll's centre; s is its overall size (~one board cell).
        private void DrawDoll2D(float cx, float cy, float s)
        {
            var dress  = new Color(0.96f, 0.55f, 0.20f);
            var shirt  = new Color(0.96f, 0.82f, 0.34f);
            var skin   = new Color(1f, 0.86f, 0.70f);
            var hair   = new Color(0.18f, 0.12f, 0.10f);
            var ribbon = new Color(0.86f, 0.18f, 0.30f);
            var eye    = new Color(1f, 0.12f, 0.24f);

            float headR = s * 0.27f;
            float hy = cy - s * 0.14f;             // head centre (above the body)
            void Disc(float x, float y, float r, Color c) => Fill(new Rect(x - r, y - r, r * 2f, r * 2f), c, r);

            Fill(new Rect(cx - s * 0.37f, hy + headR * 0.45f, s * 0.74f, s * 0.58f), dress, s * 0.16f); // pinafore skirt
            Fill(new Rect(cx - s * 0.17f, hy + headR * 0.25f, s * 0.34f, s * 0.24f), shirt, s * 0.06f);  // collar
            Disc(cx, hy - headR * 0.18f, headR * 1.12f, hair);                                 // hair mass behind
            Disc(cx, hy, headR, skin);                                                         // head
            float pr = s * 0.115f;
            Disc(cx - headR * 0.96f, hy + pr * 0.2f, pr, hair);                                // left pigtail
            Disc(cx + headR * 0.96f, hy + pr * 0.2f, pr, hair);                                // right pigtail
            Disc(cx - headR * 0.96f, hy + pr * 1.05f, pr * 0.5f, ribbon);                      // ribbons
            Disc(cx + headR * 0.96f, hy + pr * 1.05f, pr * 0.5f, ribbon);
            Fill(new Rect(cx - headR * 0.86f, hy - headR * 0.92f, headR * 1.72f, headR * 0.66f), hair, headR * 0.24f); // bangs
            float er = s * 0.045f;
            Disc(cx - s * 0.10f, hy + s * 0.02f, er, eye);                                     // red scanning eyes
            Disc(cx + s * 0.10f, hy + s * 0.02f, er, eye);
            Fill(new Rect(cx - s * 0.045f, hy + headR * 0.5f, s * 0.09f, s * 0.03f), new Color(0.82f, 0.34f, 0.32f), s * 0.015f); // mouth
        }

        // A climber's body colour from the snapshot's character (for board pawns).
        private static Color PawnColor(Snapshot snap, string id)
        {
            if (snap?.Actors != null)
                foreach (var a in snap.Actors) if (a.Id == id) return Palette.Body(a.CharacterId);
            return new Color(0.7f, 0.7f, 0.8f);
        }

        // Pip layouts for a die face 1..6 (offsets in {-1,0,1} grid units), mirroring the
        // web's DIE_PIPS — drawn as dark dots on a white die so Chutes shows a real dice.
        private static readonly int[][][] DiePips =
        {
            null,
            new[] { new[] { 0, 0 } },
            new[] { new[] { -1, -1 }, new[] { 1, 1 } },
            new[] { new[] { -1, -1 }, new[] { 0, 0 }, new[] { 1, 1 } },
            new[] { new[] { -1, -1 }, new[] { 1, -1 }, new[] { -1, 1 }, new[] { 1, 1 } },
            new[] { new[] { -1, -1 }, new[] { 1, -1 }, new[] { 0, 0 }, new[] { -1, 1 }, new[] { 1, 1 } },
            new[] { new[] { -1, -1 }, new[] { 1, -1 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { -1, 1 }, new[] { 1, 1 } },
        };

        private void DrawDiePips(Rect r, int val)
        {
            if (val < 1 || val > 6) return;
            float cx = r.center.x, cy = r.center.y, step = r.width * 0.26f, pr = r.width * 0.09f;
            var c0 = GUI.color; GUI.color = Hex("#241a33");
            foreach (var p in DiePips[val])
                GUI.DrawTexture(new Rect(cx + p[0] * step - pr, cy + p[1] * step - pr, pr * 2f, pr * 2f), Texture2D.whiteTexture);
            GUI.color = c0;
        }

        /// <summary>Top-right two-line status box.</summary>
        private void Info(float w, string line1, string line2)
        {
            // Pinned to the top strip (y=10), level with the round box — was at y=48 which
            // dropped it onto the arena.
            GUI.Box(new Rect(w - 270, 10, 260, 52), "");
            Lbl(new Rect(w - 262, 14, 250, 24), line1, _body);
            Lbl(new Rect(w - 262, 38, 250, 22), line2, new GUIStyle(_body) { fontSize = 14 });
        }

        private void DrawRoundResult(float w, float h)
        {
            Fill(new Rect(0, 0, w, h), Alpha(Bg0, 0.55f), 0f);
            var report = _router.LastRoundReport;
            Lbl(new Rect(0, h * 0.10f, w, 50), Loc.Get("ui.reckoning"), _h1);
            if (report == null) return;
            var pr = new Rect(w * 0.5f - 330f, h * 0.18f, 660f, h * 0.62f);
            Fill(pr, Panel, 22f);
            Stroke(pr, Line, 2f, 22f);

            // Who am I? Highlight the local player's row + show every player's JERSEY number
            // (the "#N" identity the announcer calls), so between games you can find yourself
            // and see how your tag placed — matches the series-result screen's number badge.
            var locals = _router.LocalPlayerIds;
            string meId = (locals != null && locals.Count == 1) ? locals[0] : null;

            float rowX = w * 0.5f - 300f, rowW = 600f, y = h * 0.22f;
            var badgeStyle = new GUIStyle(_body) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.086f, 0.125f, 0.114f) } };
            var jerseyBg = new Color(0.96f, 0.97f, 0.96f, 0.92f);
            foreach (var e in report.Entries)
            {
                bool mine = meId != null && e.PlayerId == meId;
                if (mine) Fill(new Rect(rowX - 8f, y - 2f, rowW + 16f, 26f), Alpha(Yellow, 0.12f), 7f);

                Lbl(new Rect(rowX, y, 40f, 24f), $"#{e.Placement}",
                    new GUIStyle(_body) { fontStyle = FontStyle.Bold, normal = { textColor = mine ? Yellow : Ink } });

                int num = _router.NumberOf(e.PlayerId);
                var badge = new Rect(rowX + 42f, y + 2f, 44f, 18f);
                Fill(badge, jerseyBg, 6f);
                Lbl(badge, num > 0 ? num.ToString("000") : "—", badgeStyle);

                string nm = Name(e.PlayerId) + (mine ? "  " + Loc.Get("res.you") : "");
                string tag = e.Survived ? Loc.Get("res.safe") : Loc.Get("res.out");
                string note = string.IsNullOrEmpty(e.Note) ? "" : $"  — {e.Note}";
                Lbl(new Rect(rowX + 96f, y, rowW - 96f, 24f),
                    $"{nm}   [{tag}]   ◍ {e.MarblesEarned}{note}",
                    new GUIStyle(_body) { fontStyle = mine ? FontStyle.Bold : FontStyle.Normal, normal = { textColor = mine ? Yellow : Ink } });
                y += 26;
            }
        }

        private void DrawSeriesResult(float w, float h)
        {
            Fill(new Rect(0, 0, w, h), Alpha(Bg0, 0.6f), 0f);
            var sr = _router.SeriesResult;
            Lbl(new Rect(0, h * 0.08f, w, 50), Loc.Get("ui.series_over"), _h1);

            // Panel that holds the standings. Rows are laid out *relative to this
            // rect* (not absolute screen fractions) so a full 12-player field always
            // fits inside the green panel instead of spilling past the bottom edge.
            var pr = new Rect(w * 0.5f - 340f, h * 0.16f, 680f, h * 0.58f);
            Fill(pr, Panel, 22f);
            Stroke(pr, Line, 2f, 22f);

            if (sr != null && sr.Standings.Count > 0)
            {
                const float pad = 18f;
                var inner = new Rect(pr.x + pad, pr.y + pad, pr.width - pad * 2f, pr.height - pad * 2f);
                int n = sr.Standings.Count;
                float rowH = Mathf.Clamp(inner.height / n, 20f, 30f);
                float startY = inner.y + Mathf.Max(0f, (inner.height - rowH * n) * 0.5f); // vertically centered
                int fs = Mathf.Clamp((int)(rowH - 12f), 11, 16);

                // Columns: rank | jersey badge | name | title (dim) | marbles (right).
                const float rankW = 38f, jerseyW = 52f, marW = 82f, titleW = 192f, gap = 8f;
                float rankX = inner.x;
                float jerseyX = rankX + rankW;
                float nameX = jerseyX + jerseyW + gap;
                float marX = inner.xMax - marW;
                float titleX = marX - titleW - gap;
                float nameW = titleX - nameX - gap;

                var rankStyle = new GUIStyle(_body) { fontSize = fs, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = InkDim } };
                var champRank = new GUIStyle(rankStyle) { normal = { textColor = Yellow } };
                var nameStyle = new GUIStyle(_body) { fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = Ink } };
                var champName = new GUIStyle(nameStyle) { fontStyle = FontStyle.Bold, normal = { textColor = Yellow } };
                var titleStyle = new GUIStyle(_body) { fontSize = Mathf.Max(10, fs - 3), alignment = TextAnchor.MiddleRight, normal = { textColor = InkDim } };
                var marStyle = new GUIStyle(_body) { fontSize = fs, alignment = TextAnchor.MiddleRight, normal = { textColor = Teal } };
                var badgeStyle = new GUIStyle(_body) { fontSize = Mathf.Max(10, fs - 3), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.086f, 0.125f, 0.114f) } };
                var jerseyBg = new Color(0.96f, 0.97f, 0.96f, 0.92f);

                for (int i = 0; i < n; i++)
                {
                    var st = sr.Standings[i];
                    float ry = startY + i * rowH;
                    bool champ = st.Placement == 1;
                    if (champ) Fill(new Rect(inner.x - 4f, ry, inner.width + 8f, rowH), Alpha(Yellow, 0.10f), 7f);

                    Lbl(new Rect(rankX, ry, rankW, rowH), $"#{st.Placement}", champ ? champRank : rankStyle);

                    int num = _router.NumberOf(st.PlayerId);
                    var badge = new Rect(jerseyX, ry + (rowH - 18f) * 0.5f, jerseyW - gap, 18f);
                    Fill(badge, jerseyBg, 6f);
                    Lbl(badge, num > 0 ? num.ToString("000") : "—", badgeStyle);

                    Lbl(new Rect(nameX, ry, nameW, rowH), Name(st.PlayerId), champ ? champName : nameStyle);
                    Lbl(new Rect(titleX, ry, titleW, rowH), $"“{st.Title}”", titleStyle);
                    Lbl(new Rect(marX, ry, marW, rowH), $"◍ {st.Marbles}", marStyle);
                }
            }

            float bw = 320;
            if (Btn(new Rect((w - bw) / 2f, h * 0.80f, bw, 52f), Loc.Get("ui.back_to_menu"), Pink, OnDark))
            {
                if (ReferenceEquals(_router.Active, _net)) CloseOnline();
                else _sim.EndSeries();
                _lastPhase = RoomPhase.Lobby;
            }
        }

        private void DrawCaption(float w, float h)
        {
            if (Time.time > _captionUntil || string.IsNullOrEmpty(_caption)) return;
            if (SaveService.Current != null && !SaveService.Current.settings.subtitles) return;
            var bg = GUI.color; GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.Box(new Rect(w * 0.1f, h - 80, w * 0.8f, 36), "");
            GUI.color = bg;
            Lbl(new Rect(w * 0.1f, h - 78, w * 0.8f, 32), _caption, new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter });
        }

        private void DrawToast(float w, float h)
        {
            if (Time.time > _toastUntil || string.IsNullOrEmpty(_toast)) return;
            // Fade out over the last 0.4s so it disappears gracefully.
            float a = Mathf.Clamp01((_toastUntil - Time.time) / 0.4f);
            var style = new GUIStyle(_ui) { alignment = TextAnchor.MiddleCenter, fontSize = 16, normal = { textColor = Alpha(OnDark, a) } };
            float pw = Mathf.Max(180f, style.CalcSize(new GUIContent(_toast)).x + 56f);
            var r = new Rect((w - pw) / 2f, 96f, pw, 46f);
            Fill(new Rect(r.x + 3, r.y + 8, r.width, r.height), Alpha(Color.black, 0.25f * a), 14f);
            Fill(r, Alpha(Teal, a), 14f);
            Stroke(r, Alpha(Ink, 0.18f * a), 1.5f, 14f);
            Lbl(r, "✓  " + _toast, style);
        }

        private void DrawSettings(float w, float h)
        {
            DrawThemeBackdrop(w, h);
            var s = SaveService.Current?.settings;
            if (s == null)
            {
                // Profile not loaded yet — draw the header + a way back instead of
                // dereferencing null every frame (was a per-frame NullReferenceException).
                Lbl(new Rect(0, h * 0.10f, w, 50), Loc.Get("ui.settings"), _h1);
                if (GhostBtn(new Rect(40, 36, 130, 40), Loc.Get("ui.back"))) _page = Page.Menu;
                return;
            }
            float panelW = 560f;
            // Fixed panel; the body scrolls and Save & Back is pinned to the floor, so the
            // content can never spill past the rounded border no matter how many rows there
            // are or how short the window (it used to overflow on ~720p and below).
            var panelR = new Rect(w * 0.5f - panelW * 0.5f, h * 0.135f, panelW, h * 0.84f);
            Fill(panelR, Panel, 24f);
            Stroke(panelR, Line, 2f, 24f);
            Lbl(new Rect(0, h * 0.05f, w, 48), Loc.Get("ui.settings"), _h1);

            // Quick-close X (pinned top-right) — saves and returns to the menu, same as
            // "Save & Back" (settings apply live, so closing always persists).
            if (Pill(new Rect(panelR.xMax - 46f, panelR.y + 12f, 34f, 34f), "✕"))
            {
                SaveService.Save();
                _page = Page.Menu;
                return;
            }

            float pad = 30f;
            // Save & Back pinned to the panel floor (always reachable); everything else
            // lives in the scroll view above it.
            var saveR = new Rect(panelR.x + pad, panelR.yMax - 60f, panelR.width - pad * 2f, 44f);
            float viewTop = panelR.y + 50f; // clear of the X
            var viewport = new Rect(panelR.x + pad, viewTop, panelR.width - pad * 2f, saveR.y - viewTop - 14f);

            float cx = 0f, cw = viewport.width - 16f; // local coords; leave room for the scrollbar
            float y = 0f;
            _setScroll = GUI.BeginScrollView(viewport, _setScroll, new Rect(0, 0, cw, _setContentH), false, false);

            var grp = new GUIStyle(_body) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = InkDim } };
            void Divider() { Fill(new Rect(cx, y, cw, 1f), Alpha(Line, 0.6f), 0f); y += 11f; }

            // ── Language ──
            Lbl(new Rect(cx, y, cw, 22), Loc.Get("set.language"), _body); y += 24;
            int li = System.Array.IndexOf(Loc.Locales, s.locale);
            if (li < 0) li = 0;
            int newLi = GUI.Toolbar(new Rect(cx, y, cw, 34), li, Loc.Locales);
            if (newLi != li) { s.locale = Loc.Locales[newLi]; Loc.SetLocale(s.locale); }
            y += 42; Divider();

            // ── Visual ──
            Lbl(new Rect(cx, y, cw, 16), Loc.Get("set.grp_visual"), grp); y += 18;
            Lbl(new Rect(cx, y, cw, 22), Loc.Get("set.colorblind"), _body); y += 24;
            var modes = (ColorblindMode[])System.Enum.GetValues(typeof(ColorblindMode));
            int sel = System.Array.IndexOf(modes, s.colorblind);
            int newSel = GUI.Toolbar(new Rect(cx, y, cw, 34), sel, modes.Select(m => Loc.Get("cb." + m)).ToArray());
            if (newSel != sel) { s.colorblind = modes[newSel]; Palette.Mode = s.colorblind; }
            y += 40;
            bool reduce = GUI.Toggle(new Rect(cx, y, cw, 24), s.reduceFlashAndShake, "  " + Loc.Get("set.reduce_motion"));
            if (reduce != s.reduceFlashAndShake) { s.reduceFlashAndShake = reduce; ScreenFx.ReduceMotion = reduce; }
            y += 30; Divider();

            // ── Audio ──
            // Master is the overall ceiling; Game Sound (SFX + announcer + in-game music)
            // and Background Music (menu/lobby loops) are capped at it for both the readout
            // and playback, so a channel can never read or sound louder than Master — no
            // more "100% sitting under 50%". Stored values are preserved (master is a live
            // ceiling), so raising Master back reveals a channel's original level.
            Lbl(new Rect(cx, y, cw, 16), Loc.Get("set.grp_audio"), grp); y += 18;
            Lbl(new Rect(cx, y, cw, 22), Loc.Get("set.master_volume", Mathf.RoundToInt(s.masterVolume * 100f)), _body); y += 22;
            float newMaster = GUI.HorizontalSlider(new Rect(cx, y, cw, 22), s.masterVolume, 0f, 1f);
            if (!Mathf.Approximately(newMaster, s.masterVolume)) { s.masterVolume = newMaster; AudioService.Instance?.ApplyVolumes(); }
            AudioListener.volume = s.masterVolume; y += 30;

            // All three sliders share the same 0..1 track so their knobs line up on one
            // scale: a 50% knob sits at the 50% mark on every row, and "20%" sits at the
            // real 20% mark. Game Sound / Background Music show min(channel, master), so
            // their knob can never sit right of Master's — dragging one past Master just
            // sticks it at Master's position.
            float shownSfx = Mathf.Min(s.sfxVolume, s.masterVolume);
            Lbl(new Rect(cx, y, cw, 22), Loc.Get("set.game_volume", Mathf.RoundToInt(shownSfx * 100f)), _body); y += 22;
            float rawSfx = GUI.HorizontalSlider(new Rect(cx, y, cw, 22), shownSfx, 0f, 1f);
            if (!Mathf.Approximately(rawSfx, shownSfx)) { s.sfxVolume = Mathf.Min(rawSfx, s.masterVolume); AudioService.Instance?.ApplyVolumes(); }
            y += 30;

            float shownMus = Mathf.Min(s.musicVolume, s.masterVolume);
            Lbl(new Rect(cx, y, cw, 22), Loc.Get("set.music_volume", Mathf.RoundToInt(shownMus * 100f)), _body); y += 22;
            float rawMus = GUI.HorizontalSlider(new Rect(cx, y, cw, 22), shownMus, 0f, 1f);
            if (!Mathf.Approximately(rawMus, shownMus)) { s.musicVolume = Mathf.Min(rawMus, s.masterVolume); AudioService.Instance?.ApplyVolumes(); }
            y += 30;

            bool musicOn = GUI.Toggle(new Rect(cx, y, cw, 24), s.musicEnabled, "  " + Loc.Get("set.music"));
            if (musicOn != s.musicEnabled) SetMusicEnabled(musicOn);
            y += 28;
            s.subtitles = GUI.Toggle(new Rect(cx, y, cw, 24), s.subtitles, "  " + Loc.Get("set.subtitles"));
            y += 32; Divider();

            // ── Remap + Credits (scroll with the body; Save is pinned below) ──
            // Credits opens a dedicated scrollable page (DrawCredits) — the CC-BY
            // attribution is too long for a clipped label here.
            float halfW = (cw - 12f) * 0.5f;
            if (GhostBtn(new Rect(cx, y, halfW, 40f), "🎮  " + Loc.Get("set.remap"))) { SaveService.Save(); _page = Page.Controls; }
            if (GhostBtn(new Rect(cx + halfW + 12f, y, halfW, 40f), "📜  " + Loc.Get("set.credits"))) { SaveService.Save(); _page = Page.Credits; }
            y += 44;

            _setContentH = y; // feed this frame's measured height into next frame's scroll bounds
            GUI.EndScrollView();

            if (Btn(saveR, Loc.Get("ui.save_and_back"), Pink, OnDark))
            {
                SaveService.Save();
                _page = Page.Menu;
            }
        }

        private string Name(string playerId) => _router.NameOf(playerId);

        // ============================ PLAY ONLINE ============================
        // Front-of-house page that drives the NetClient: connect → host/join → lobby.
        // Once the host starts, OnGUI's in-game routing takes over (Intro/Playing/…).

        private void OpenOnline()
        {
            _page = Page.Online;
            _router.Active = _net;
            var url = SaveService.Current?.settings?.serverUrl;
            _urlEdit = string.IsNullOrEmpty(url) ? "ws://localhost:8080/" : url;
            // Auto-connect on entry when idle so the host/join panel is ready when
            // the server is up; a down server lands on the Failed state with retry.
            if (_net.State == NetClient.LinkState.Idle) ConnectOnline();
        }

        private void ConnectOnline()
        {
            var prof = SaveService.Current;
            var url = string.IsNullOrWhiteSpace(_urlEdit) ? "ws://localhost:8080/" : _urlEdit.Trim();
            if (prof?.settings != null && prof.settings.serverUrl != url) { prof.settings.serverUrl = url; SaveService.Save(); }
            _net.Connect(url, prof?.name ?? "Player", prof?.characterId ?? "avo");
        }

        // Leave online and return to the main menu, dropping the connection.
        private void CloseOnline()
        {
            _net.Disconnect();
            _router.Active = _sim;
            _page = Page.Menu;
            _lastPhase = RoomPhase.Lobby;
        }

        private void DrawOnline(float w, float h)
        {
            DrawThemeBackdrop(w, h);
            if (GhostBtn(new Rect(40, 36, 130, 40), Loc.Get("ui.back"))) { CloseOnline(); return; }
            Lbl(new Rect(0, 40, w, 56), "🌐 " + Loc.Get("online.title"), new GUIStyle(_h1) { fontSize = 46 });
            Lbl(new Rect(0, 96, w, 26), Loc.Get("online.subtitle"),
                new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, fontSize = 16, normal = { textColor = InkDim } });

            float pw = 560f, px = (w - pw) / 2f, py = 158f;
            var panel = new Rect(px, py, pw, 380f);
            Fill(panel, Panel, 22f); Stroke(panel, Line, 2f, 22f);
            float cx = px + 30f, cw = pw - 60f, y = py + 28f;

            if (_net.InRoom) DrawLobby(cx, cw, y);
            else if (_net.State == NetClient.LinkState.Online) DrawHostJoin(cx, cw, y);
            else DrawConnect(cx, cw, y);
        }

        // Not yet connected: server url + connect / status.
        private void DrawConnect(float cx, float cw, float y)
        {
            Lbl(new Rect(cx, y, cw, 20), Loc.Get("online.server"), new GUIStyle(_ui) { fontSize = 12, normal = { textColor = InkDim } });
            _urlEdit = GUI.TextField(new Rect(cx, y + 24, cw, 38), _urlEdit ?? "", 128);
            y += 78f;

            bool connecting = _net.State == NetClient.LinkState.Connecting;
            if (Btn(new Rect(cx, y, cw, 50f), connecting ? Loc.Get("online.connecting") : Loc.Get("online.connect"), Green, OnDark, !connecting))
                ConnectOnline();
            y += 64f;

            if (_net.State == NetClient.LinkState.Failed)
                Lbl(new Rect(cx, y, cw, 44), Loc.Get("online.cant_reach") + "\n" + (_net.LastError ?? ""),
                    new GUIStyle(_body) { alignment = TextAnchor.UpperCenter, fontSize = 14, wordWrap = true, normal = { textColor = Red } });
            else
                Lbl(new Rect(cx, y, cw, 44), Loc.Get("online.dev_hint"),
                    new GUIStyle(_body) { alignment = TextAnchor.UpperCenter, fontSize = 13, wordWrap = true, normal = { textColor = InkDim } });
        }

        // Connected, no room yet: host (with mode) or join by code.
        private void DrawHostJoin(float cx, float cw, float y)
        {
            float half = (cw - 16f) / 2f;

            // Host column. Difficulty was chosen in the play wizard before connecting.
            Lbl(new Rect(cx, y, half, 22), Loc.Get("online.host_room"), new GUIStyle(_ui) { fontSize = 13, normal = { textColor = InkDim } });
            Lbl(new Rect(cx, y + 30, half, 24), Loc.Get("online.difficulty", _onlineHardcore ? Loc.Get("ui.hardcore") : Loc.Get("ui.casual")),
                new GUIStyle(_body) { fontSize = 14, normal = { textColor = _onlineHardcore ? Pink : Green } });
            if (Btn(new Rect(cx, y + 64, half, 50f), Loc.Get("online.create_room"), Pink, OnDark))
                _net.HostRoom(_onlineHardcore ? "hardcore" : "casual", 4);

            // Join column.
            float jx = cx + half + 16f;
            Lbl(new Rect(jx, y, half, 22), Loc.Get("online.join_by_code"), new GUIStyle(_ui) { fontSize = 13, normal = { textColor = InkDim } });
            _codeEdit = GUI.TextField(new Rect(jx, y + 28, half, 36), _codeEdit ?? "", 8).ToUpperInvariant();
            if (Btn(new Rect(jx, y + 64, half, 50f), Loc.Get("online.join"), Teal, OnDark, !string.IsNullOrWhiteSpace(_codeEdit)))
                _net.JoinByCode(_codeEdit.Trim());

            if (!string.IsNullOrEmpty(_net.LastError))
                Lbl(new Rect(cx, y + 130, cw, 24), _net.LastError,
                    new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = Red } });

            float by = y + 168f;
            if (GhostBtn(new Rect(cx, by, cw, 40f), Loc.Get("online.disconnect"))) CloseOnline();
        }

        // In a room, waiting in the lobby: code + roster + host controls.
        private void DrawLobby(float cx, float cw, float y)
        {
            Lbl(new Rect(cx, y, cw, 22), Loc.Get("online.room_code"), new GUIStyle(_ui) { fontSize = 12, normal = { textColor = InkDim } });
            Lbl(new Rect(cx, y + 18, cw, 44), _net.RoomCode ?? "····",
                new GUIStyle(_h1) { fontSize = 40, alignment = TextAnchor.MiddleLeft, normal = { textColor = Teal } });
            y += 74f;

            var roster = _net.Roster();
            Lbl(new Rect(cx, y, cw, 20), Loc.Get("online.players", roster.Length), new GUIStyle(_ui) { fontSize = 12, normal = { textColor = InkDim } });
            y += 24f;
            foreach (var p in roster)
            {
                string tag = p.Bot ? "  🤖 " + Loc.Get("online.bot") : (p.IsYou ? "  ⭐ " + Loc.Get("online.you") : "");
                Lbl(new Rect(cx, y, cw, 24), $"#{p.Number}  {p.Name}{tag}",
                    new GUIStyle(_body) { fontSize = 16, normal = { textColor = p.IsYou ? Ink : Alpha(Ink, 0.85f) } });
                y += 26f;
            }

            float by = 158f + 380f - 30f - 50f; // pin controls to the panel bottom
            float half = (cw - 16f) / 2f;
            if (_net.IsHost)
            {
                bool lobbyFull = _net.PlayerCount >= Eliminated.Sim.Core.Constants.MaxPlayers;
                if (Btn(new Rect(cx, by, half, 50f), Loc.Get("online.add_bot"), Yellow, OnGold, !lobbyFull)) _net.AddBot();
                bool canStart = _net.PlayerCount >= 2;
                if (Btn(new Rect(cx + half + 16f, by, half, 50f), Loc.Get("online.start_series"), Green, OnDark, canStart))
                    _net.StartSeries();
            }
            else
            {
                Lbl(new Rect(cx, by + 6, cw - half - 16f, 38), Loc.Get("online.waiting_host"),
                    new GUIStyle(_body) { fontSize = 15, normal = { textColor = InkDim } });
                if (Btn(new Rect(cx + half + 16f, by, half, 50f), Loc.Get("online.leave"), Red, OnDark)) _net.Leave();
            }
        }
    }
}
