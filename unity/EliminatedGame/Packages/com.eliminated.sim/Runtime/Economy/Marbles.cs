namespace Eliminated.Sim.Economy
{
    /// <summary>
    /// Marble (currency ◍) payouts and bragging-rights titles. Values copied
    /// verbatim from the reference game (lib/shared/constants.ts MARBLES/TITLES).
    /// </summary>
    public static class Marbles
    {
        // ── Per-round payouts ────────────────────────────────────────────
        public const int SurvivePerRound = 50;
        public const int RoundWinBonus = 40;     // the round's 1st place
        public const int ElimParticipation = 5;  // consolation for the eliminated

        // ── Series payouts ───────────────────────────────────────────────
        // Winning the series should feel good without dwarfing the field: the
        // champion already out-earns everyone via round wins + the top placement
        // bonus, so this top-up stays modest (≈ one extra round win).
        public const int ChampionBonus = 100;    // series winner

        /// <summary>Series placement bonus for 1st..5th. Beyond 5th = 0.</summary>
        public static readonly int[] PlacementCurve = { 200, 120, 80, 50, 30 };

        /// <summary>Series bonus for a 1-based final placement.</summary>
        public static int PlacementBonus(int placement)
        {
            int i = placement - 1;
            return (i >= 0 && i < PlacementCurve.Length) ? PlacementCurve[i] : 0;
        }

        /// <summary>Marbles for one round given survival and whether you placed 1st.</summary>
        public static int RoundPayout(bool survived, bool wonRound)
        {
            if (!survived) return ElimParticipation;
            return SurvivePerRound + (wonRound ? RoundWinBonus : 0);
        }

        // ── Titles (by 1-based placement) ────────────────────────────────
        public static readonly string[] Titles =
        {
            "The Last Player Standing",
            "First Loser",
            "Bronze Is Just Shiny Last",
            "Mid-Tier Menace",
            "Almost Clutch",
            "Solidly Average",
            "Participation Trophy",
            "Background Player",
            "Comic Relief",
            "Warm Body",
            "Speed Bump",
            "Crowd Filler",
            "Barely Showed Up",
            "Tutorial Difficulty",
            "Practice Dummy",
            "Cannon Fodder"
        };

        public static string PlacementTitle(int placement)
        {
            int i = placement - 1;
            if (i < 0) i = 0;
            if (i >= Titles.Length) i = Titles.Length - 1;
            return Titles[i];
        }
    }
}
