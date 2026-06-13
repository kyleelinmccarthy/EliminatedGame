using System.Collections.Generic;
using Eliminated.Sim.Model;

namespace Eliminated.Game.Audio
{
    /// <summary>
    /// The Game Master's robotic PA voice. Maps in-game moments to the pre-rendered
    /// announcer clip bank (Resources/Audio/voice, produced by tools/VoiceGen) and
    /// plays them through <see cref="AudioService.Speak"/>. Mirrors the web build's
    /// browser-TTS Game Master: a MALE announcer reveals each game and barks Simon
    /// Says orders; a FEMALE voice calls eliminations. Unity has no speech synth, so
    /// the clips are baked offline and stitched here (e.g. "Game three." + the game
    /// name). Every call is a no-op when no <see cref="AudioService"/> exists.
    /// </summary>
    public static class Announcer
    {
        private const string Dir = "voice/";
        // Her elimination calls ride a little louder than the male reveals (> unity boosts
        // the announcer source; see AudioService.Speak). Past this many out in one tick a
        // wipe is summarized instead of enumerated, so the readout never runs absurdly long.
        private const float ElimVolume = 1.2f;
        // The male game/room reveal rides a touch above unity too — "a little louder" —
        // but stays below ElimVolume so her elimination calls remain the loudest cue.
        private const float RevealVolume = 1.15f;
        private const int MaxNamed = 6;

        /// <summary>Male ceremonial reveal: "Attention, players. Game N. &lt;game name&gt;. The arena,
        /// &lt;room&gt;." (or "…The final game. …"). A Squid-Game-style PA: the "Attention, players."
        /// lead-in opens every reveal. roundNumber is 1-based; numbers past the baked range drop to
        /// just the name. <paramref name="room"/> is an <c>ArenaThemes</c> theme key (courtyard/neon/…);
        /// null/empty or a theme with no baked clip simply omits the arena line.</summary>
        public static void Game(int roundNumber, GameId game, bool finale, string room = null)
        {
            var line = new List<string>(4) { Dir + "attention" };
            if (finale) line.Add(Dir + "final_game");
            else if (roundNumber >= 1 && roundNumber <= 20) line.Add(Dir + "game_" + roundNumber.ToString("00"));
            line.Add(Dir + "name_" + GameKey(game));
            if (!string.IsNullOrEmpty(room)) line.Add(Dir + "room_" + room);
            AudioService.Instance?.Speak(line, RevealVolume);
        }

        /// <summary>Female elimination call, by the eliminated player's lobby number, read
        /// DIGIT BY DIGIT: "Player five seven three has been eliminated." Stitched at runtime
        /// from ten digit clips so any tag speaks without a phrase per number. Returns the
        /// spoken line's length in seconds (so the caller can hold off the next call until
        /// it finishes). <paramref name="number"/> ≤ 0 (unknown) → generic "Player eliminated."</summary>
        public static float EliminatedByNumber(int number)
        {
            if (number <= 0) return AudioService.Instance?.Speak(new[] { Dir + "elim_player" }, ElimVolume) ?? 0f;
            var line = new List<string>(8) { Dir + "num_player" };
            AppendNumberWords(line, number);
            line.Add(Dir + "num_elim");
            return AudioService.Instance?.Speak(line, ElimVolume) ?? 0f;
        }

        /// <summary>Female call for a same-tick wipe, enumerated by number:
        /// "Players five seven three, one two — &lt;beat&gt; — have been eliminated." A "@ms"
        /// beat separates each tag and a longer one sets up the verdict (see AudioService.Speak).
        /// Returns the spoken line's length in seconds. An unknown tag or an oversized wipe
        /// (&gt; <see cref="MaxNamed"/>) falls back to the generic plural.</summary>
        public static float EliminatedMultiple(IReadOnlyList<int> numbers)
        {
            if (numbers == null || numbers.Count == 0) return EliminatedMany();
            if (numbers.Count == 1) return EliminatedByNumber(numbers[0]);
            if (numbers.Count > MaxNamed) return EliminatedMany();
            var line = new List<string>(numbers.Count * 6 + 3) { Dir + "num_players" };
            for (int i = 0; i < numbers.Count; i++)
            {
                if (numbers[i] <= 0) return EliminatedMany();
                if (i > 0) line.Add("@250"); // comma beat between names
                AppendNumberWords(line, numbers[i]);
            }
            line.Add("@350"); // a beat before the verdict
            line.Add(Dir + "num_elim_plural");
            return AudioService.Instance?.Speak(line, ElimVolume) ?? 0f;
        }

        /// <summary>Female call for a wipe too large to name: "Players eliminated."</summary>
        public static float EliminatedMany() => AudioService.Instance?.Speak(new[] { Dir + "elim_players" }, ElimVolume) ?? 0f;

        // Spell the tag out digit by digit ("573" → num_5 @ num_7 @ num_3), with a short beat
        // between digits so they stay distinct ("five‑seven‑three"). n is ≥ 1 here, so the
        // decimal string never has a leading zero.
        private static void AppendNumberWords(List<string> line, int n)
        {
            string s = n.ToString();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) line.Add("@80");         // small beat between digits ("five‑seven‑three")
                line.Add(Dir + "num_" + s[i]);      // s[i] is '0'..'9' → num_0..num_9
            }
        }

        /// <summary>Male Simon Says order. <paramref name="command"/> is the sim key
        /// (head/nose/blink/flip/jump); freeze plays the bespoke "Freeze!" line.</summary>
        public static void Simon(string command, bool freeze)
        {
            if (freeze || command == "freeze") { AudioService.Instance?.Speak(new[] { Dir + "simon_freeze" }); return; }
            if (string.IsNullOrEmpty(command)) return;
            AudioService.Instance?.Speak(new[] { Dir + "simon_" + command });
        }

        // GameId → voice-clip suffix. Must match the keys baked by tools/VoiceGen.
        private static string GameKey(GameId id) => id switch
        {
            GameId.RedLight => "redlight",
            GameId.Tag => "tag",
            GameId.Mingle => "mingle",
            GameId.GlassBridge => "glassbridge",
            GameId.TugOfWar => "tugofwar",
            GameId.RpsMinusOne => "rps",
            GameId.JumpRope => "jumprope",
            GameId.Boomerang => "boomerang",
            GameId.Dodgeball => "dodgeball",
            GameId.MusicalChairs => "musicalchairs",
            GameId.PresentSwap => "present",
            GameId.PropHunt => "prophunt",
            GameId.ChutesAndLadders => "chutesladders",
            GameId.SimonSays => "simonsays",
            GameId.KeepyUppy => "keepyuppy",
            GameId.KingOfTheHill => "koth",
            _ => "redlight",
        };
    }
}
