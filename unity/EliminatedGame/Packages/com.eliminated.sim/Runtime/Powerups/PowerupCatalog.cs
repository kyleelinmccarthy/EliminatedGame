using System.Collections.Generic;

namespace Eliminated.Sim.Powerups
{
    /// <summary>Blessing / curse / wildcard — drives the reveal colour.</summary>
    public enum PowerupTier { Good, Bad, Chaos }

    /// <summary>Presentation for one powerup: the icon + name that float over your
    /// blob when you grab it, the one-line blurb (how-to-play), and the tier that
    /// colours the reveal green / red / purple.</summary>
    public struct PowerupMeta
    {
        public string Key;     // the PowerupKind / BoomPower name this describes
        public string Icon;    // emoji shown on the reveal + HUD chip
        public string Label;   // short display name ("Zoomies")
        public string Short;   // plain "what it DOES" line ("Move faster!") for the reveal
        public string Blurb;   // one-liner for how-to-play
        public PowerupTier Tier;
        public string ColorHex; // reveal text colour (#rrggbb)
    }

    /// <summary>
    /// The presentation catalog shared by the pickup reveal, the active-effect
    /// HUD, and how-to-play — mirroring the web's lib/shared/powerups.ts. Keyed by
    /// the enum name (PowerupKind or Boomerang.BoomPower), so a single lookup
    /// serves both the shared orbs and Boomerang's combat drops. The reveal is the
    /// whole point: orbs are an identical mystery on the ground, and THIS is the
    /// "find out what you grabbed" payoff that floats over you on pickup.
    /// </summary>
    public static class PowerupCatalog
    {
        public const string GoodColor = "#7dffa0";  // blessing — green
        public const string BadColor = "#ff7a7a";   // curse — red
        public const string ChaosColor = "#c792ff"; // wildcard — purple

        private static string ColorFor(PowerupTier t)
            => t == PowerupTier.Good ? GoodColor : t == PowerupTier.Bad ? BadColor : ChaosColor;

        private static readonly Dictionary<string, PowerupMeta> _byKey = new Dictionary<string, PowerupMeta>();

        static PowerupCatalog()
        {
            // ── Shared powerups (PowerupKind) ──
            Add("Speed", "⚡", "Zoomies", "Move faster!", PowerupTier.Good, "Move way faster, briefly, like your will to live.");
            Add("Shield", "🛡️", "Bubble", "Blocks one hit!", PowerupTier.Good, "Blocks one hit, freeze, or lava splash. Singular. Spend it wisely.");
            Add("Tiny", "🔻", "Shrink", "Tiny & nimble!", PowerupTier.Good, "Become a tiny, nimble, harder-to-murder blob.");
            Add("Vision", "🔦", "Lantern", "See in the dark!", PowerupTier.Good, "See in the dark. See exactly how doomed you are.");
            Add("Caffeine", "☕", "Caffeine", "Dash — no cooldown!", PowerupTier.Good, "Dash forever — no cooldown. Vibrate toward victory.");
            Add("Disguise", "🥸", "Disguise", "Look like someone else!", PowerupTier.Good, "Wear someone else's face. Everyone sees an impostor — except you.");
            Add("Reverse", "🌀", "Bamboozled", "Controls REVERSED!", PowerupTier.Bad, "Your controls are REVERSED. Sincerely, the management.");
            Add("Slow", "🐌", "Molasses", "Slowed down!", PowerupTier.Bad, "Sluggish, syrupy, an easy target with extra steps.");
            Add("Giant", "🎈", "Embiggen", "Huge target!", PowerupTier.Bad, "Puff up huge — a bigger, prouder target.");
            Add("Dizzy", "💫", "Dizzy", "Wobbly steering!", PowerupTier.Bad, "Wibble-wobble. Your steering develops opinions of its own.");
            Add("Slippery", "🍌", "Slippery", "Sliding on ice!", PowerupTier.Bad, "The floor is now ice. Steering is merely a suggestion.");
            Add("Jumble", "🔀", "Jumble", "Teleported!", PowerupTier.Chaos, "Blink to a random spot. Could be safety. Could be lava.");

            // ── Boomerang's own combat drops (Boomerang.BoomPower) ──
            // Speed / Shield / Tiny reuse the shared entries above (same key).
            Add("BigRang", "💥", "Big Rang", "Bigger boomerang!", PowerupTier.Good, "A chonky boomerang with a much bigger bite.");
            Add("Multishot", "3️⃣", "Multishot", "Throw 3 at once!", PowerupTier.Good, "Hurl three rangs at once. More chaos, more kills.");
            Add("Magnet", "🧲", "Magnet", "Homing boomerang!", PowerupTier.Good, "Your boomerang hunts the nearest enemy.");
        }

        private static void Add(string key, string icon, string label, string shortDesc, PowerupTier tier, string blurb)
            => _byKey[key] = new PowerupMeta { Key = key, Icon = icon, Label = label, Short = shortDesc, Tier = tier, Blurb = blurb, ColorHex = ColorFor(tier) };

        /// <summary>Look up by enum name (PowerupKind or BoomPower ToString()).</summary>
        public static bool TryGet(string key, out PowerupMeta meta) => _byKey.TryGetValue(key ?? "", out meta);

        public static PowerupMeta Get(PowerupKind k) => _byKey[k.ToString()];

        /// <summary>"⚡ Zoomies!" — the small text floated over a blob on pickup.</summary>
        public static string RevealText(string key)
            => _byKey.TryGetValue(key ?? "", out var m) ? $"{m.Icon} {m.Label}!" : "+";

        /// <summary>"⚡ ZOOMIES — Move faster!" — the bold banner shown to YOU when you
        /// grab one, spelling out what it actually does (not just its cute name).</summary>
        public static string RevealBanner(string key)
            => _byKey.TryGetValue(key ?? "", out var m) ? $"{m.Icon} {m.Label.ToUpperInvariant()} — {m.Short}" : "Powerup!";

        public static IEnumerable<PowerupMeta> All => _byKey.Values;
    }
}
