using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Eliminated.Sim.Model;
using Eliminated.Sim.Games;
using Eliminated.Sim.Room;
using Eliminated.Game.SimBridge;
using Eliminated.Game.Save;
using Eliminated.Game.Accessibility;
using Eliminated.Game.Audio;
using Eliminated.Game.Platform;
using Eliminated.Sim.Localization;

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
        private SimRunner _sim;
        private bool _showSettings;
        private RoomPhase _lastPhase = RoomPhase.Lobby;
        private bool _seriesBanked;
        private string _caption = "";
        private float _captionUntil;

        private GUIStyle _h1, _h2, _body, _pill;

        public void Init(SimRunner sim) => _sim = sim;

        private void EnsureStyles()
        {
            if (_h1 != null) return;
            _h1 = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _h2 = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = new Color(0.9f, 0.9f, 0.92f) } };
            _pill = new GUIStyle(GUI.skin.box) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        }

        private void Update()
        {
            var phase = _sim != null && _sim.HasSeries ? _sim.Phase : RoomPhase.Lobby;
            if (phase != _lastPhase)
            {
                OnPhaseChanged(_lastPhase, phase);
                _lastPhase = phase;
            }
        }

        private void OnPhaseChanged(RoomPhase from, RoomPhase to)
        {
            var room = _sim.Room;
            switch (to)
            {
                case RoomPhase.Intro:
                    if (room?.CurrentGame != null)
                        Caption(Loc.Get("gm.game_intro", room.RoundIndex + 1, GameName(room.CurrentGame.Value)), 5f);
                    break;
                case RoomPhase.SeriesResult:
                    BankMarbles();
                    var champ = room?.SeriesResult?.ChampionId;
                    var champP = room?.Players.FirstOrDefault(p => p.Id == champ);
                    if (champP != null) Caption(Loc.Get("gm.champion", champP.Name), 8f);
                    AudioService.Instance?.Play("win");
                    if (champ == SimRunner.LocalPlayerId) // the local player won the series
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

        private void BankMarbles()
        {
            if (_seriesBanked) return;
            var local = _sim.Room?.Players.FirstOrDefault(p => p.Id == SimRunner.LocalPlayerId);
            if (local != null && SaveService.Current != null)
            {
                SaveService.Current.marbles += local.MarblesEarned;
                if (_sim.Room.SeriesResult?.ChampionId == local.Id) SaveService.Current.seriesWon += 1;
                SaveService.Current.roundsSurvived += local.RoundsSurvived;
                SaveService.Save();
            }
            _seriesBanked = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            float s = Mathf.Max(1f, Screen.height / 900f);
            GUIUtility.ScaleAroundPivot(new Vector2(s, s), Vector2.zero);

            float w = Screen.width / s, h = Screen.height / s;

            if (_showSettings) { DrawSettings(w, h); return; }
            if (_sim == null || !_sim.HasSeries) { DrawMenu(w, h); return; }

            switch (_sim.Phase)
            {
                case RoomPhase.Intro: DrawIntro(w, h); break;
                case RoomPhase.Playing: DrawHud(w, h); break;
                case RoomPhase.RoundResult: DrawRoundResult(w, h); break;
                case RoomPhase.SeriesResult: DrawSeriesResult(w, h); break;
            }
            DrawCaption(w, h);
        }

        private static string GameName(GameId id) => Loc.Get("game." + id);

        private void DrawMenu(float w, float h)
        {
            GUI.Label(new Rect(0, h * 0.18f, w, 60), "◖◗ " + Loc.Get("ui.title"), _h1);
            GUI.Label(new Rect(0, h * 0.26f, w, 30), Loc.Get("ui.tagline"), new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter });

            float bw = 360, bx = (w - bw) / 2f, by = h * 0.40f;
            int marbles = SaveService.Current?.marbles ?? 0;
            GUI.Label(new Rect(bx, by - 40, bw, 30), $"◍ {Loc.Get("ui.marbles")}: {marbles}", new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter });

            if (GUI.Button(new Rect(bx, by, bw, 50), Loc.Get("ui.play_solo_casual")))
                StartSolo(SeriesMode.Casual);
            if (GUI.Button(new Rect(bx, by + 58, bw, 50), Loc.Get("ui.play_solo_hardcore")))
                StartSolo(SeriesMode.Hardcore);

            int pads = Gamepad.all.Count;
            string coopLabel = pads > 0
                ? $"Local Co-op — P1 keyboard + {pads} gamepad{(pads > 1 ? "s" : "")}"
                : "Local Co-op — connect a gamepad to add players";
            GUI.enabled = pads > 0;
            if (GUI.Button(new Rect(bx, by + 116, bw, 50), coopLabel))
                StartCoop(SeriesMode.Casual);
            GUI.enabled = true;

            if (GUI.Button(new Rect(bx, by + 174, bw, 40), Loc.Get("ui.settings")))
                _showSettings = true;
            if (GUI.Button(new Rect(bx, by + 220, bw, 40), Loc.Get("ui.quit")))
                Application.Quit();
        }

        private void StartSolo(SeriesMode mode)
        {
            _seriesBanked = false;
            var prof = SaveService.Current;
            _sim.HostLocalSeries(mode, RoundsMode.Fixed(4), prof?.name ?? "You", prof?.characterId ?? "avo");
        }

        private void StartCoop(SeriesMode mode)
        {
            _seriesBanked = false;
            var prof = SaveService.Current;
            // P1 = keyboard (the saved profile); each connected gamepad = another player.
            int count = Mathf.Clamp(1 + Gamepad.all.Count, 1, 4); // shared screen: cap at 4
            var roster = new List<string> { "fox", "panther", "bunny", "cat" };
            var locals = new List<LocalPlayerInfo>();
            for (int i = 0; i < count; i++)
            {
                string name = i == 0 ? (prof?.name ?? "P1") : $"P{i + 1}";
                string ch = i == 0 ? (prof?.characterId ?? "avo") : roster[i % roster.Count];
                locals.Add(new LocalPlayerInfo("local" + i, name, ch));
            }
            _sim.HostLocalCoop(mode, RoundsMode.Fixed(4), locals);
        }

        private void DrawIntro(float w, float h)
        {
            var room = _sim.Room;
            string name = room?.CurrentGame != null ? GameName(room.CurrentGame.Value) : "";
            string icon = room?.CurrentGame != null ? GameCatalog.Of(room.CurrentGame.Value).Icon : "";
            GUI.Label(new Rect(0, h * 0.35f, w, 60), Loc.Get("ui.round", (room?.RoundIndex ?? 0) + 1), _h2WithCenter());
            GUI.Label(new Rect(0, h * 0.42f, w, 70), $"{icon} {name}", _h1);
            GUI.Label(new Rect(0, h * 0.52f, w, 40), Loc.Get("ui.get_ready"), new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter });
        }

        private GUIStyle _h2WithCenter() => new GUIStyle(_h2) { alignment = TextAnchor.MiddleCenter };

        private void DrawHud(float w, float h)
        {
            var room = _sim.Room;
            var snap = _sim.Latest;
            string game = room?.CurrentGame != null ? GameName(room.CurrentGame.Value) : "";
            int alive = snap?.Actors?.Count(a => a.Alive) ?? 0;

            GUI.Box(new Rect(10, 10, 360, 64), "");
            GUI.Label(new Rect(20, 14, 360, 26), $"Round {(room?.RoundIndex ?? 0) + 1} · {game}", _h2);
            GUI.Label(new Rect(20, 42, 360, 24), $"Blobs alive: {alive}", _body);

            if (!_sim.HasSeries) return;
            if (!_sim.Room.PlayStarted)
                GUI.Label(new Rect(0, h * 0.45f, w, 60), "3 · 2 · 1 · GO!", _h1);

            DrawControlsHint(room.CurrentGame, w, h);
            DrawGameSpecific(snap, w, h);
        }

        private void DrawControlsHint(GameId? game, float w, float h)
        {
            string hint = "WASD/Arrows to move.";
            if (game.HasValue)
            {
                switch (game.Value)
                {
                    case GameId.RedLight: hint = "Move on GREEN (WASD/Arrows). FREEZE on RED."; break;
                    case GameId.TugOfWar: hint = "MASH Space / Click to pull your team to victory!"; break;
                    case GameId.Boomerang: hint = "Move (WASD) · Aim (mouse) · Throw (Click/Space) · Dash (Shift)"; break;
                    case GameId.Tag:
                    case GameId.Dodgeball:
                    case GameId.Mingle:
                    case GameId.MusicalChairs:
                    case GameId.KingOfTheHill:
                    case GameId.PropHunt:
                    case GameId.KeepyUppy: hint = "WASD/Arrows move · Space action · Shift dash"; break;
                    case GameId.GlassBridge: hint = "← / → (or A/D) to step LEFT or RIGHT on your turn."; break;
                    case GameId.ChutesAndLadders: hint = "SPACE to roll · ← / → to pick a fork."; break;
                    case GameId.JumpRope: hint = "SPACE / Click to JUMP the rope."; break;
                    case GameId.SimonSays: hint = "Obey: W=head A=nose S=blink D=flip Space=jump · FREEZE = touch nothing."; break;
                    case GameId.RpsMinusOne: hint = "RPS duel (bot-assisted in this build)."; break;
                    case GameId.PresentSwap: hint = "Secret Santa — guessing is bot-assisted in this build."; break;
                }
            }
            GUI.Label(new Rect(0, h - 36, w, 24), hint, new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter });
        }

        private void DrawGameSpecific(Snapshot snap, float w, float h)
        {
            switch (snap?.Data)
            {
                case RedLightGreenLight.RlglData rl:
                    var c = rl.Red ? (rl.Lethal ? Color.red : new Color(1f, 0.5f, 0f)) : Color.green;
                    var prev = GUI.color; GUI.color = c;
                    GUI.Box(new Rect(w - 180, 10, 170, 40), rl.Red ? (rl.Lethal ? "RED — FREEZE!" : "RED…") : "GREEN — GO!", _pill);
                    GUI.color = prev;
                    break;
                case TugOfWar.TugData tug:
                    float mid = w / 2f;
                    GUI.Box(new Rect(mid - 200, 60, 400, 24), "");
                    float knob = mid + Mathf.Clamp(tug.RopePos, -1f, 1f) * 190f;
                    GUI.Box(new Rect(knob - 6, 60, 12, 24), "");
                    GUI.Label(new Rect(mid - 200, 86, 400, 20), $"Time: {tug.TimeLeft:0.0}s", new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter });
                    break;
                case Boomerang.BoomData boom:
                    Info(w, $"Survive: {boom.Alive}/{boom.Target}", $"{boom.TimeLeft:0}s left");
                    break;
                case Tag.TagData tag:
                    Info(w, $"🔵 {tag.FreezersAlive}  🩷 {tag.RunnersAlive}", tag.DeepFreeze ? "DEEP FREEZE!" : $"{tag.TimeLeft:0}s");
                    break;
                case Dodgeball.DodgeData dodge:
                    Info(w, $"Team A {dodge.Team0Alive} — {dodge.Team1Alive} Team B", $"{dodge.TimeLeft:0}s");
                    break;
                case KingOfTheHill.KothData koth:
                    Info(w, $"🌋 Alive: {koth.Alive}", $"{koth.TimeLeft:0}s — the floor is lava!");
                    break;
                case MusicalChairs.McData mc:
                    Info(w, $"🪑 {mc.Phase.ToUpper()} (round {mc.Round})", mc.Fake ? "…PSYCH! keep moving" : $"{mc.TimeLeft:0.0}s");
                    break;
                case Mingle.MingleData mingle:
                    Info(w, mingle.Phase == "Mingle" ? $"GROUP OF {mingle.N}!" : "🫂 Mingle…", $"round {mingle.Round} · {mingle.TimeLeft:0.0}s");
                    break;
                case GlassBridge.GlassData glass:
                    Info(w, $"🪟 Row {glass.Frontier + 1}/{glass.Rows}", glass.Phase == "choose" ? $"{Name(glass.ActiveId)} picks… {glass.TurnTimeLeft:0.0}s" : glass.Phase);
                    break;
                case JumpRope.RopeData rope:
                    Info(w, $"🤸 Swing {rope.Swing}", $"bridge {rope.BridgeLen} planks");
                    break;
                case RpsMinusOne.RpsData rps:
                    Info(w, $"✊ {rps.Phase.ToUpper()}", $"round {rps.Round} · {rps.TimeLeft:0.0}s");
                    break;
                case SimonSays.SimonData simon:
                    if (simon.Command != null)
                    {
                        var pc = GUI.color; GUI.color = simon.Freeze ? Color.cyan : Color.yellow;
                        GUI.Box(new Rect(w / 2f - 200, h * 0.18f, 400, 46),
                            simon.Freeze ? "🧊 FREEZE — touch nothing!" : $"SIMON SAYS: {simon.Command.ToUpper()}", _pill);
                        GUI.color = pc;
                    }
                    Info(w, $"🎵 Beat {simon.Beat}/{simon.MaxBeats}", "press W/A/S/D/Space");
                    break;
                case ChutesAndLadders.ChutesData chutes:
                    Info(w, "🎲 Race to the top!", $"{chutes.TimeLeft:0}s — Space to roll");
                    break;
                case PresentSwap.PresentData present:
                    Info(w, present.Phase == "gift" ? "🌑 Lights out…" : (present.Phase == "guess" ? "💡 Guess your giver!" : "🎁 Reveal"), $"round {present.Round} · {present.TimeLeft:0.0}s");
                    break;
                case PropHunt.PropData prop:
                    Info(w, prop.Phase == "hide" ? "🫥 Disguise yourself!" : $"🗡️ Found {prop.Found}/{prop.Quota}", $"{prop.HidersLeft} hiding · {prop.TimeLeft:0}s");
                    break;
                case KeepyUppy.KeepyData keepy:
                    Info(w, $"🎈 Alive: {keepy.Alive}", $"{keepy.TimeLeft:0}s — don't let it drop!");
                    break;
            }
        }

        /// <summary>Top-right two-line status box.</summary>
        private void Info(float w, string line1, string line2)
        {
            GUI.Box(new Rect(w - 270, 48, 260, 50), "");
            GUI.Label(new Rect(w - 262, 50, 250, 24), line1, _body);
            GUI.Label(new Rect(w - 262, 72, 250, 22), line2, new GUIStyle(_body) { fontSize = 14 });
        }

        private void DrawRoundResult(float w, float h)
        {
            var report = _sim.Room?.LastRoundReport;
            GUI.Label(new Rect(0, h * 0.10f, w, 50), Loc.Get("ui.reckoning"), _h1);
            if (report == null) return;
            float y = h * 0.22f;
            foreach (var e in report.Entries)
            {
                string nm = Name(e.PlayerId);
                string tag = e.Survived ? "SAFE" : "OUT";
                string note = string.IsNullOrEmpty(e.Note) ? "" : $"  — {e.Note}";
                GUI.Label(new Rect(w * 0.5f - 280, y, 560, 24),
                    $"#{e.Placement}  {nm}   [{tag}]   ◍ {e.MarblesEarned}{note}", _body);
                y += 26;
            }
        }

        private void DrawSeriesResult(float w, float h)
        {
            var sr = _sim.Room?.SeriesResult;
            GUI.Label(new Rect(0, h * 0.10f, w, 50), "👑 " + Loc.Get("ui.series_over"), _h1);
            if (sr != null)
            {
                float y = h * 0.22f;
                foreach (var st in sr.Standings)
                {
                    GUI.Label(new Rect(w * 0.5f - 280, y, 560, 24),
                        $"#{st.Placement}  {Name(st.PlayerId)}   “{st.Title}”   ◍ {st.Marbles}", _body);
                    y += 26;
                }
            }
            float bw = 320;
            if (GUI.Button(new Rect((w - bw) / 2f, h * 0.82f, bw, 50), Loc.Get("ui.back_to_menu")))
            {
                _sim.EndSeries();
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
            GUI.Label(new Rect(w * 0.1f, h - 78, w * 0.8f, 32), _caption, new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter });
        }

        private void DrawSettings(float w, float h)
        {
            var s = SaveService.Current.settings;
            GUI.Label(new Rect(0, h * 0.10f, w, 50), Loc.Get("ui.settings"), _h1);
            float x = w * 0.5f - 220, y = h * 0.22f;

            GUI.Label(new Rect(x, y, 440, 26), "Language:", _body); y += 30;
            int li = System.Array.IndexOf(Loc.Locales, s.locale);
            if (li < 0) li = 0;
            int newLi = GUI.Toolbar(new Rect(x, y, 440, 36), li, Loc.Locales);
            if (newLi != li) { s.locale = Loc.Locales[newLi]; Loc.SetLocale(s.locale); }
            y += 50;

            GUI.Label(new Rect(x, y, 440, 26), "Colorblind mode:", _body); y += 30;
            var modes = (ColorblindMode[])System.Enum.GetValues(typeof(ColorblindMode));
            int sel = System.Array.IndexOf(modes, s.colorblind);
            int newSel = GUI.Toolbar(new Rect(x, y, 440, 36), sel, modes.Select(m => m.ToString()).ToArray());
            if (newSel != sel) { s.colorblind = modes[newSel]; Palette.Mode = s.colorblind; }
            y += 50;

            s.subtitles = GUI.Toggle(new Rect(x, y, 440, 26), s.subtitles, "  Subtitles / captions"); y += 30;
            s.reduceFlashAndShake = GUI.Toggle(new Rect(x, y, 440, 26), s.reduceFlashAndShake, "  Reduce flashing & screen shake"); y += 38;

            GUI.Label(new Rect(x, y, 440, 24), $"Master volume: {(s.masterVolume * 100):0}%", _body); y += 26;
            s.masterVolume = GUI.HorizontalSlider(new Rect(x, y, 440, 24), s.masterVolume, 0f, 1f);
            AudioListener.volume = s.masterVolume; y += 50;

            GUI.Label(new Rect(x, y, 440, 24), "Controls are remappable in a later build (accessibility).", new GUIStyle(_body) { fontSize = 14 }); y += 28;
            GUI.Label(new Rect(x, y, 440, 36),
                "Credits: SFX by rubberduck (CC0) · models by Kenney (CC0) · music\n\"Casual 8-Bit\" by Kat (CC-BY 4.0) — all via OpenGameArt.",
                new GUIStyle(_body) { fontSize = 13 }); y += 44;

            if (GUI.Button(new Rect(x, y, 440, 44), Loc.Get("ui.save_and_back")))
            {
                SaveService.Save();
                _showSettings = false;
            }
        }

        private string Name(string playerId)
            => _sim.Room?.Players.FirstOrDefault(p => p.Id == playerId)?.Name ?? playerId;
    }
}
