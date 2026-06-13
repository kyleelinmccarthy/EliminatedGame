using Eliminated.Sim.Model;

namespace Eliminated.Sim.Powerups
{
    /// <summary>
    /// The shared powerups. À la Boomerang Fu, every orb is a mystery on the
    /// ground (the view draws an identical "?"); the reveal happens the instant
    /// you grab one. BLESSINGS then stay with you for the rest of your life this
    /// round; CURSES tick down and wear off. (Boomerang adds its own combat set.)
    /// </summary>
    public enum PowerupKind
    {
        // ── Blessings (held — persist until you die / the round ends) ──
        Speed,    // ⚡ Zoomies
        Shield,   // 🛡️ Bubble
        Tiny,     // 🔻 Shrink
        Vision,   // 🔦 Lantern
        Caffeine, // ☕ Caffeine — dash with no cooldown
        Disguise, // 🥸 Disguise — look like another player to everyone else
        // ── Curses (timed — wear off) ──
        Reverse,  // 🌀 Bamboozled
        Slow,     // 🐌 Molasses
        Giant,    // 🎈 Embiggen
        Dizzy,    // 💫 Dizzy
        Slippery, // 🍌 Slippery — ice-skate drift
        // ── Chaos (instant — fires once on pickup, no lingering state) ──
        Jumble    // 🔀 Jumble — warps you to a random spot (might save, might doom)
    }

    /// <summary>
    /// Powerup lifetimes and how each applies to an actor. Curse durations come
    /// from docs/GAME_DESIGN.md (the reference lib/shared/powerups.ts). Blessings
    /// are "held": their timer is set to <see cref="Held"/> so they never expire
    /// within a round — they stay with you, Boomerang-Fu style.
    /// </summary>
    public static class PowerupEffects
    {
        /// <summary>An effectively-infinite timer: a blessing held until death /
        /// round end. Decrementing it each tick leaves it essentially unchanged,
        /// and the HUD treats any timer this large as "held" (no draining bar).
        /// One knob: lower it (e.g. to 7f) to make blessings time out instead.</summary>
        public const float Held = 1_000_000f;

        /// <summary>A timer is "held" (a steady blessing) rather than a draining
        /// curse when it's larger than any single round could ever run.</summary>
        public static bool IsHeldTimer(float t) => t > 120f;

        // Curse durations in seconds. Shield has no timer (consumed on next hit).
        public const float ReverseDuration = 5f;
        public const float SlowDuration = 6f;
        public const float GiantDuration = 8f;
        public const float DizzyDuration = 5f;
        public const float SlipperyDuration = 6f;

        public static bool IsGood(PowerupKind k)
        {
            switch (k)
            {
                case PowerupKind.Speed:
                case PowerupKind.Shield:
                case PowerupKind.Tiny:
                case PowerupKind.Vision:
                case PowerupKind.Caffeine:
                case PowerupKind.Disguise:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The full lifetime of a curse (for the HUD's draining bar).
        /// Blessings return <see cref="Held"/>; <see cref="PowerupKind.Jumble"/>
        /// is instant (0).</summary>
        public static float MaxDuration(PowerupKind k)
        {
            switch (k)
            {
                case PowerupKind.Reverse: return ReverseDuration;
                case PowerupKind.Slow: return SlowDuration;
                case PowerupKind.Giant: return GiantDuration;
                case PowerupKind.Dizzy: return DizzyDuration;
                case PowerupKind.Slippery: return SlipperyDuration;
                case PowerupKind.Jumble: return 0f;
                default: return Held; // blessings
            }
        }

        /// <summary>Applies a powerup pickup to an actor (sets the relevant
        /// timer/flag). Jumble's warp and Disguise's borrowed identity need the
        /// RNG and the rest of the field, so <see cref="PowerupField.Collect"/>
        /// finishes those after calling this.</summary>
        public static void Apply(Actor a, PowerupKind k)
        {
            switch (k)
            {
                case PowerupKind.Speed: a.PuSpeedT = Held; break;
                case PowerupKind.Shield: a.Shield = true; break;
                case PowerupKind.Tiny: a.PuTinyT = Held; a.PuGiantT = 0f; break;
                case PowerupKind.Vision: a.PuVisionT = Held; break;
                case PowerupKind.Caffeine: a.PuCaffeineT = Held; break;
                case PowerupKind.Disguise: a.PuDisguiseT = Held; break; // identity set in Collect
                case PowerupKind.Reverse: a.PuReverseT = ReverseDuration; break;
                case PowerupKind.Slow: a.PuSlowT = SlowDuration; break;
                case PowerupKind.Giant: a.PuGiantT = GiantDuration; a.PuTinyT = 0f; break;
                case PowerupKind.Dizzy: a.PuDizzyT = DizzyDuration; break;
                case PowerupKind.Slippery: a.PuSlipperyT = SlipperyDuration; break;
                case PowerupKind.Jumble: break; // random warp handled in Collect
            }
        }
    }
}
