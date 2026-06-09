namespace Eliminated.Sim.Core
{
    /// <summary>
    /// Whimsical bot name + character pools. Ported from the reference
    /// lib/shared/util.ts (BOT_FIRST/BOT_LAST) and characters.ts.
    /// </summary>
    public static class BotNames
    {
        private static readonly string[] First =
        {
            "Wiggly", "Squishy", "Chompy", "Bouncy", "Mr.", "Lil", "Big", "Captain",
            "Sir", "Lady", "Doctor", "Sneaky", "Sleepy", "Wobbly", "Zesty", "Crunchy",
            "Gloopy", "Spicy"
        };

        private static readonly string[] Last =
        {
            "Beans", "Nugget", "Pickles", "Wobbles", "Munch", "Snackington", "Crumbs",
            "Noodle", "Biscuit", "Tofu", "Gravy", "Sprout", "Dumpling", "Waffles",
            "Pretzel", "Mochi"
        };

        /// <summary>Free starter character ids (subset of the cosmetic roster).</summary>
        public static readonly string[] Characters =
        {
            "koala", "fox", "panther", "bunny", "cat", "avo", "egg", "berry",
            "donut", "pickle", "tomato", "sushi", "nana", "slime", "ghost", "capybara"
        };

        public static string Random(Rng rng) => $"{rng.Pick(First)} {rng.Pick(Last)}";
        public static string RandomCharacter(Rng rng) => rng.Pick(Characters);
    }
}
