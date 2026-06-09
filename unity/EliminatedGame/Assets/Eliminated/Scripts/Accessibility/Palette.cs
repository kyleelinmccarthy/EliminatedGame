using System.Collections.Generic;
using UnityEngine;

namespace Eliminated.Game.Accessibility
{
    public enum ColorblindMode { Normal, Protanopia, Deuteranopia, Tritanopia }

    /// <summary>
    /// Central color source with colorblind-safe variants. Gameplay colors are
    /// never the *only* signal (the view also uses shape/markers), but swapping
    /// to high-contrast, hue-separated palettes per colorblind type helps a lot.
    /// Teams, danger/safe, and powerup good/bad all route through here.
    /// </summary>
    public static class Palette
    {
        public static ColorblindMode Mode = ColorblindMode.Normal;

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        // Team colors (blue vs pink) with colorblind alternatives that stay
        // distinguishable (blue/orange separates well for the common types).
        public static Color Team(int team)
        {
            bool zero = team == 0;
            switch (Mode)
            {
                case ColorblindMode.Protanopia:
                case ColorblindMode.Deuteranopia:
                    return zero ? Hex("#1e88e5") : Hex("#ffb300"); // blue / amber
                case ColorblindMode.Tritanopia:
                    return zero ? Hex("#d81b60") : Hex("#00897b"); // magenta / teal
                default:
                    return zero ? Hex("#42a5f5") : Hex("#ff6fa5"); // blue / pink
            }
        }

        public static Color Danger => Mode == ColorblindMode.Normal ? Hex("#ff1744") : Hex("#ff6d00");
        public static Color Safe => Mode == ColorblindMode.Tritanopia ? Hex("#00897b") : Hex("#00e676");
        public static Color Powerup(bool good) => good ? Safe : Danger;

        private static readonly Dictionary<string, Color> Bodies = new Dictionary<string, Color>
        {
            { "avocado", Hex("#a8c64f") }, { "egg", Hex("#fff6e6") }, { "strawberry", Hex("#ef5350") },
            { "eggplant", Hex("#7e57c2") }, { "broccoli", Hex("#66bb6a") }, { "donut", Hex("#ffb74d") },
            { "pickle", Hex("#9ccc65") }, { "tomato", Hex("#e53935") }, { "koala", Hex("#b0bec5") },
            { "aardvark", Hex("#bcaaa4") }, { "panther", Hex("#455a64") }, { "fox", Hex("#ff8a65") },
            { "capybara", Hex("#a1887f") }, { "frogwizard", Hex("#81c784") }, { "rogue", Hex("#78909c") },
            { "bunny", Hex("#f5f5f5") },
        };

        public static Color Body(string characterId)
            => characterId != null && Bodies.TryGetValue(characterId, out var c) ? c : Hex("#90caf9");
    }
}
