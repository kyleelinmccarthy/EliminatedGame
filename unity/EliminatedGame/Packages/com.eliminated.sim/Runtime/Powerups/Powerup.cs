using Eliminated.Sim.Model;

namespace Eliminated.Sim.Powerups
{
    /// <summary>The 8 shared powerups (Boomerang adds its own combat extras).</summary>
    public enum PowerupKind
    {
        Speed,   // ⚡ Zoomies   (good)
        Shield,  // 🛡️ Bubble    (good)
        Tiny,    // 🔻 Shrink    (good)
        Vision,  // 🔦 Lantern   (good)
        Reverse, // 🌀 Bamboozled (bad)
        Slow,    // 🐌 Molasses  (bad)
        Giant,   // 🎈 Embiggen  (bad)
        Dizzy    // 💫 Dizzy     (bad)
    }

    /// <summary>
    /// Powerup durations and how each applies to an actor. Durations are copied
    /// from docs/GAME_DESIGN.md (the reference lib/shared/powerups.ts).
    /// </summary>
    public static class PowerupEffects
    {
        // Durations in seconds. Shield has no timer (consumed on the next hit).
        public const float SpeedDuration = 7f;
        public const float TinyDuration = 9f;
        public const float VisionDuration = 10f;
        public const float ReverseDuration = 5f;
        public const float SlowDuration = 6f;
        public const float GiantDuration = 8f;
        public const float DizzyDuration = 5f;

        public static bool IsGood(PowerupKind k)
        {
            switch (k)
            {
                case PowerupKind.Speed:
                case PowerupKind.Shield:
                case PowerupKind.Tiny:
                case PowerupKind.Vision:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Applies a powerup pickup to an actor (sets the relevant timer/flag).</summary>
        public static void Apply(Actor a, PowerupKind k)
        {
            switch (k)
            {
                case PowerupKind.Speed: a.PuSpeedT = SpeedDuration; break;
                case PowerupKind.Shield: a.Shield = true; break;
                case PowerupKind.Tiny: a.PuTinyT = TinyDuration; a.PuGiantT = 0f; break;
                case PowerupKind.Vision: a.PuVisionT = VisionDuration; break;
                case PowerupKind.Reverse: a.PuReverseT = ReverseDuration; break;
                case PowerupKind.Slow: a.PuSlowT = SlowDuration; break;
                case PowerupKind.Giant: a.PuGiantT = GiantDuration; a.PuTinyT = 0f; break;
                case PowerupKind.Dizzy: a.PuDizzyT = DizzyDuration; break;
            }
        }
    }
}
