using UnityEngine;
using Eliminated.Sim.Model;
using Eliminated.Sim.Localization;

namespace Eliminated.Game.View
{
    /// <summary>
    /// The arena "rooms": the shared catalog of floor themes, the deterministic
    /// per-round pick, and their announce-able display names. Shared so the renderer
    /// (<see cref="ArenaView"/>) and the HUD intro that announces the room always agree
    /// on where a round is played, with no cross-component ordering dependency — both
    /// derive the room from the same (round, game) inputs.
    /// </summary>
    public static class ArenaThemes
    {
        /// <summary>Theme keys; each has a <c>floor_&lt;key&gt;.tga</c> + matching wall
        /// palette in <see cref="ArenaView"/>.</summary>
        public static readonly string[] All = { "courtyard", "neon", "candy", "toxic", "beach", "haunt" };

        /// <summary>The room a round is played in. Deterministic (so every client in an online
        /// match shares one arena) and collision-free for consecutive rounds: round*7 ≡ +1 each
        /// round (mod 6), while the game salt only ever shifts the index by 0 or 3 (mod 6) — never
        /// the 5 that would cancel the +1 — so no two rounds in a row land on the same room.</summary>
        public static string ForRound(int round, GameId? game)
        {
            int salt = game.HasValue ? (int)game.Value : 0;
            int idx = Mathf.Abs(round * 7 + salt * 3) % All.Length;
            return All[idx];
        }

        /// <summary>The flavour name shown when a room is announced ("Candy Kingdom", …).
        /// Localised via the <c>room.&lt;theme&gt;</c> key, with English fallback built in.</summary>
        public static string DisplayName(string theme) => Loc.Get("room." + theme);
    }
}
