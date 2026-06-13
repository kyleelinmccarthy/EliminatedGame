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
        private const int MaxPlayerTag = 456; // tag range with a baked num_<n> clip (mirrors GameRoom)

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

        /// <summary>Female elimination call, by the eliminated player's lobby tag: "Player five
        /// seven three has been eliminated." Each tag 1..456 is its OWN clip (read digit by digit
        /// as one utterance), so the number has natural intonation. Returns the spoken line's
        /// length in seconds (so the caller can hold off the next call until it finishes). A tag
        /// outside 1..<see cref="MaxPlayerTag"/> → generic "Player eliminated."</summary>
        public static float EliminatedByNumber(int number)
        {
            if (number < 1 || number > MaxPlayerTag) return AudioService.Instance?.Speak(new[] { Dir + "elim_player" }, ElimVolume) ?? 0f;
            var line = new List<string> { Dir + "num_player", Dir + "num_" + number, Dir + "num_elim" };
            return AudioService.Instance?.Speak(line, ElimVolume) ?? 0f;
        }

        /// <summary>Female call for a same-tick wipe: "Players five seven three, one two, four oh
        /// six … have been eliminated." One "Players" opener, then each tag as its own clip with a
        /// breath between — because each tag is a whole, coherently-intoned number (a natural fall
        /// at its end), the pause alone keeps them distinct, the way the Squid-Game PA reads a list
        /// (no need to repeat "Player"). Returns the spoken line's length in seconds. An unknown tag
        /// or an oversized wipe (&gt; <see cref="MaxNamed"/>) falls back to the generic plural.</summary>
        public static float EliminatedMultiple(IReadOnlyList<int> numbers)
        {
            if (numbers == null || numbers.Count == 0) return EliminatedMany();
            if (numbers.Count == 1) return EliminatedByNumber(numbers[0]);
            if (numbers.Count > MaxNamed) return EliminatedMany();
            var line = new List<string>(numbers.Count * 2 + 3) { Dir + "num_players" };
            for (int i = 0; i < numbers.Count; i++)
            {
                if (numbers[i] < 1 || numbers[i] > MaxPlayerTag) return EliminatedMany();
                if (i > 0) line.Add("@330");          // breath between tags (each is its own clip)
                line.Add(Dir + "num_" + numbers[i]);
            }
            line.Add("@350"); // a beat before the verdict
            line.Add(Dir + "num_elim_plural");
            return AudioService.Instance?.Speak(line, ElimVolume) ?? 0f;
        }

        /// <summary>Female call for a wipe too large to name: "Players eliminated."</summary>
        public static float EliminatedMany() => AudioService.Instance?.Speak(new[] { Dir + "elim_players" }, ElimVolume) ?? 0f;

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
