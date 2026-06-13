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

        /// <summary>Female elimination call, by the eliminated player's lobby number:
        /// "Player N has been eliminated." Stitched at runtime from the baked number-word
        /// bank (num_player + digit words + num_elim) so any tag 1..456 speaks without a
        /// whole phrase per number — the number you see over a head is the one you hear.
        /// <paramref name="number"/> ≤ 0 (unknown) falls back to a generic "Player
        /// eliminated."</summary>
        public static void EliminatedByNumber(int number)
        {
            if (number <= 0) { AudioService.Instance?.Speak(new[] { Dir + "elim_player" }, ElimVolume); return; }
            var line = new List<string>(8) { Dir + "num_player" };
            AppendNumberWords(line, number);
            line.Add(Dir + "num_elim");
            AudioService.Instance?.Speak(line, ElimVolume);
        }

        /// <summary>Female call for a same-tick wipe, enumerated by number:
        /// "Players 1, 2, 3, 4 — &lt;beat&gt; — have been eliminated." A "@ms" beat separates
        /// each tag and a longer one sets up the verdict (see AudioService.Speak). An unknown
        /// tag or an oversized wipe (&gt; <see cref="MaxNamed"/>) falls back to the generic plural.</summary>
        public static void EliminatedMultiple(IReadOnlyList<int> numbers)
        {
            if (numbers == null || numbers.Count == 0) { EliminatedMany(); return; }
            if (numbers.Count == 1) { EliminatedByNumber(numbers[0]); return; }
            if (numbers.Count > MaxNamed) { EliminatedMany(); return; }
            var line = new List<string>(numbers.Count * 5 + 3) { Dir + "num_players" };
            for (int i = 0; i < numbers.Count; i++)
            {
                if (numbers[i] <= 0) { EliminatedMany(); return; }
                if (i > 0) line.Add("@200"); // comma beat between names
                AppendNumberWords(line, numbers[i]);
            }
            line.Add("@350"); // a beat before the verdict
            line.Add(Dir + "num_elim_plural");
            AudioService.Instance?.Speak(line, ElimVolume);
        }

        /// <summary>Female call for a wipe too large to name: "Players eliminated."</summary>
        public static void EliminatedMany() => AudioService.Instance?.Speak(new[] { Dir + "elim_players" }, ElimVolume);

        // Append the spoken-word clip keys for 1..999 (covers the 1..456 lobby-tag range):
        // hundreds digit + "hundred", then the remainder as a teen (1..19) or tens (+ ones).
        // e.g. 387 → num_3, num_hundred, num_80, num_7.
        private static void AppendNumberWords(List<string> line, int n)
        {
            int hundreds = n / 100, rem = n % 100;
            if (hundreds > 0) { line.Add(Dir + "num_" + hundreds); line.Add(Dir + "num_hundred"); }
            if (rem == 0) return;
            if (rem < 20) { line.Add(Dir + "num_" + rem); return; }
            line.Add(Dir + "num_" + (rem / 10 * 10));
            if (rem % 10 != 0) line.Add(Dir + "num_" + (rem % 10));
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
