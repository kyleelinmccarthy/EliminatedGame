using System.Collections.Generic;
using UnityEngine;

namespace Eliminated.Game.Accessibility
{
    // Protanopia and deuteranopia are both red–green deficiencies and shared one
    // palette here, so they were merged into a single RedGreen mode; tritanopia is
    // the blue–yellow case. Legacy saves that stored the old 4-value enum are
    // migrated in SaveService.ApplySettings.
    public enum ColorblindMode { Normal, RedGreen, BlueYellow }

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
                case ColorblindMode.RedGreen:
                    return zero ? Hex("#1e88e5") : Hex("#ffb300"); // blue / amber
                case ColorblindMode.BlueYellow:
                    return zero ? Hex("#d81b60") : Hex("#00897b"); // magenta / teal
                default:
                    return zero ? Hex("#42a5f5") : Hex("#ff6fa5"); // blue / pink
            }
        }

        public static Color Danger => Mode == ColorblindMode.Normal ? Hex("#ff1744") : Hex("#ff6d00");
        public static Color Safe => Mode == ColorblindMode.BlueYellow ? Hex("#00897b") : Hex("#00e676");
        public static Color Powerup(bool good) => good ? Safe : Danger;

        // ---- UI / menu accents (colorblind-aware) ----
        // The brand palette is hot-pink + teal. Under a colorblind mode we shift the
        // accents to a higher-separation set so the *menus and lobby themselves*
        // reflect the active mode (not just in-match team/danger colors). Names are
        // semantic so call sites read intent: Primary (calls to action), Secondary
        // (online/info), Positive (Casual/safe), Warning, Negative (Hardcore/danger).
        public static Color UiPrimary => Mode switch
        {
            ColorblindMode.RedGreen => Hex("#f5a623"),    // amber
            ColorblindMode.BlueYellow => Hex("#e91e8c"),  // magenta
            _ => Hex("#ff2e88"),                                                          // brand hot-pink
        };
        public static Color UiSecondary => Mode switch
        {
            ColorblindMode.RedGreen => Hex("#2196f3"),    // blue
            ColorblindMode.BlueYellow => Hex("#009688"),  // teal
            _ => Hex("#19d3bd"),                                                          // brand teal
        };
        public static Color UiPositive => Mode switch
        {
            ColorblindMode.RedGreen => Hex("#00bcd4"),    // cyan (reads "go" without green)
            ColorblindMode.BlueYellow => Hex("#26a69a"),  // teal-green
            _ => Hex("#4cd9a0"),                                                          // brand green
        };
        public static Color UiWarning => Mode switch
        {
            ColorblindMode.RedGreen => Hex("#ffd740"),    // gold
            ColorblindMode.BlueYellow => Hex("#ff8a65"),  // coral
            _ => Hex("#ffce3a"),                                                          // brand yellow
        };
        public static Color UiNegative => Mode switch
        {
            ColorblindMode.RedGreen => Hex("#ff7043"),    // deep orange
            ColorblindMode.BlueYellow => Hex("#d81b60"),  // deep magenta-red
            _ => Hex("#ff5a4d"),                                                          // brand red
        };

        // Keyed by the cosmetic roster ids (see Eliminated.Sim.Economy.Cosmetics).
        private static readonly Dictionary<string, Color> Bodies = new Dictionary<string, Color>
        {
            { "koala", Hex("#b0bec5") }, { "aardvark", Hex("#bcaaa4") }, { "panther", Hex("#455a64") },
            { "fox", Hex("#ff8a65") }, { "capybara", Hex("#a1887f") }, { "wizard", Hex("#81c784") },
            { "rogue", Hex("#78909c") }, { "bunny", Hex("#f5f5f5") }, { "pig", Hex("#f48fb1") },
            { "cat", Hex("#ffcc80") }, { "mouse", Hex("#cfd8dc") }, { "hamster", Hex("#ffe0b2") },
            { "ghost", Hex("#e0e0e0") }, { "slime", Hex("#9ccc65") }, { "avo", Hex("#a8c64f") },
            { "egg", Hex("#fff6e6") }, { "berry", Hex("#ef5350") }, { "egg2", Hex("#7e57c2") },
            { "brocc", Hex("#66bb6a") }, { "donut", Hex("#ffb74d") }, { "pickle", Hex("#9ccc65") },
            { "tomato", Hex("#e53935") }, { "pine", Hex("#ffd54f") }, { "shroom", Hex("#bcaaa4") },
            { "sushi", Hex("#fff3e0") }, { "nana", Hex("#fff176") }, { "plum", Hex("#9575cd") },
            { "orange", Hex("#ffa726") }, { "blueberry", Hex("#5c6bc0") }, { "carrot", Hex("#ff7043") },
            { "dragonfruit", Hex("#ec407a") }, { "melon", Hex("#ef5350") }, { "goldegg", Hex("#ffca28") },
            { "onigiri", Hex("#eceff1") }, { "ninja", Hex("#37474f") }, { "sorcerer", Hex("#7e57c2") },
            { "ghostpepper", Hex("#d32f2f") }, { "cosmic", Hex("#311b92") },
            { "cow", Hex("#eceff1") }, { "owl", Hex("#a1887f") }, { "snowowl", Hex("#e0e0e0") },
            { "demon", Hex("#ef5350") }, { "devil", Hex("#8d6e63") }, { "sheep", Hex("#f5f0e6") },
        };

        public static Color Body(string characterId)
            => characterId != null && Bodies.TryGetValue(characterId, out var c) ? c : Hex("#90caf9");
    }

    /// <summary>
    /// Screen-level juice — camera shake + a brief full-screen flash — behind a
    /// single accessibility gate. When <see cref="ReduceMotion"/> is on (Settings →
    /// "Reduce flashing &amp; screen shake") every <see cref="Shake"/>/<see cref="Flash"/>
    /// becomes a no-op, so photosensitive / motion-sensitive players get a calm
    /// screen. Triggers are fire-and-forget: ArenaView samples the shake offset onto
    /// the camera each frame and HudUi paints the flash overlay in OnGUI; HudUi.Update
    /// drives <see cref="Tick"/> once per frame to decay both.
    /// </summary>
    public static class ScreenFx
    {
        /// <summary>Mirror of GameSettings.reduceFlashAndShake (set via SaveService.ApplySettings).</summary>
        public static bool ReduceMotion;

        private static float _shake;        // current intensity 0..~1
        private static Color _flashColor = Color.white;
        private static float _flash;        // current alpha 0..~0.6
        private const float ShakeDur = 0.45f; // seconds for a full-intensity shake to settle
        private const float FlashDur = 0.55f; // seconds for a flash to fade out

        /// <summary>Kick the camera. intensity ≈ 0.25 (tap) … 1.0 (heavy hit).</summary>
        public static void Shake(float intensity)
        {
            if (ReduceMotion || intensity <= 0f) return;
            _shake = Mathf.Min(1.2f, Mathf.Max(_shake, intensity));
        }

        /// <summary>Tint the whole screen briefly. alpha ≈ 0.12 (subtle) … 0.45 (strong).</summary>
        public static void Flash(Color color, float alpha)
        {
            if (ReduceMotion || alpha <= 0f) return;
            _flashColor = color;
            _flash = Mathf.Min(0.55f, Mathf.Max(_flash, alpha));
        }

        /// <summary>Decay both effects. Call once per frame (HudUi.Update).</summary>
        public static void Tick(float dt)
        {
            if (dt <= 0f) return;
            _shake = Mathf.MoveTowards(_shake, 0f, dt / ShakeDur);
            _flash = Mathf.MoveTowards(_flash, 0f, dt / FlashDur);
        }

        /// <summary>Current camera offset (unit-circle × intensity). ArenaView scales by zoom.</summary>
        public static Vector2 ShakeOffset()
            => _shake <= 0.0001f ? Vector2.zero : Random.insideUnitCircle * _shake;

        /// <summary>Current flash color with live alpha (a = 0 when idle).</summary>
        public static Color FlashColor()
        {
            var c = _flashColor;
            c.a = _flash;
            return c;
        }
    }
}
