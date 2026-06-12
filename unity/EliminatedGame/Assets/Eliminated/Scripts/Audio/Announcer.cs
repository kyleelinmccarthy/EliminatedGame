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

        /// <summary>Male reveal: "Game N. &lt;game name&gt;. Welcome to &lt;room&gt;." (or "The final
        /// game. …"). roundNumber is 1-based; numbers past the baked range drop to just the name.
        /// <paramref name="room"/> is an <c>ArenaThemes</c> theme key (courtyard/neon/…); null/empty
        /// or a theme with no baked clip simply omits the room line.</summary>
        public static void Game(int roundNumber, GameId game, bool finale, string room = null)
        {
            var line = new List<string>(3);
            if (finale) line.Add(Dir + "final_game");
            else if (roundNumber >= 1 && roundNumber <= 20) line.Add(Dir + "game_" + roundNumber.ToString("00"));
            line.Add(Dir + "name_" + GameKey(game));
            if (!string.IsNullOrEmpty(room)) line.Add(Dir + "room_" + room);
            AudioService.Instance?.Speak(line);
        }

        /// <summary>Female elimination call, by the eliminated player's lobby number:
        /// "Player N has been eliminated." Stitched at runtime from the baked number-word
        /// bank (num_player + digit words + num_elim) so any tag 1..456 speaks without a
        /// whole phrase per number — the number you see over a head is the one you hear.
        /// <paramref name="number"/> ≤ 0 (unknown) falls back to a generic "Player
        /// eliminated."</summary>
        public static void EliminatedByNumber(int number)
        {
            if (number <= 0) { AudioService.Instance?.Speak(new[] { Dir + "elim_player" }); return; }
            var line = new List<string>(8) { Dir + "num_player" };
            AppendNumberWords(line, number);
            line.Add(Dir + "num_elim");
            AudioService.Instance?.Speak(line);
        }

        /// <summary>Female call for a simultaneous wipe — too many out at once to name,
        /// so the count is summarized: "Players eliminated."</summary>
        public static void EliminatedMany() => AudioService.Instance?.Speak(new[] { Dir + "elim_players" });

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
