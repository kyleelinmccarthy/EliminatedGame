// Tunable constants shared across the simulation and the client.
// Ported verbatim from the original web game (lib/shared/constants.ts) so values
// stay identical to the reference implementation. Pure C# — no UnityEngine.

namespace Eliminated.Sim.Core
{
    /// <summary>
    /// Global simulation constants. Values are authoritative and must match the
    /// reference game. See docs/GAME_DESIGN.md for provenance.
    /// </summary>
    public static class Constants
    {
        // ── Simulation rate ──────────────────────────────────────────────
        /// <summary>Server simulation + snapshot rate (Hz).</summary>
        public const int TickHz = 20;

        /// <summary>Milliseconds per tick (1000 / TickHz).</summary>
        public const float TickMs = 1000f / TickHz;

        /// <summary>Fixed timestep in seconds (1 / TickHz). The sim never reads
        /// wall-clock for gameplay; every timer advances by this.</summary>
        public const float Dt = 1f / TickHz;

        // ── Arena (logical units, server space) ──────────────────────────
        public const float ArenaW = 1280f;
        public const float ArenaH = 720f;

        // ── Players ──────────────────────────────────────────────────────
        public const float PlayerRadius = 26f;

        /// <summary>Base movement speed in units / second.</summary>
        public const float PlayerSpeed = 240f;

        // ── Rooms ────────────────────────────────────────────────────────
        public const int RoomCodeLen = 4;
        /// <summary>Hard cap on competitors (humans + bots) per room. Raised from
        /// the reference game's 8 to support 12-player lobbies.</summary>
        public const int MaxPlayers = 12;
        public const int MinToStart = 2;
        /// <summary>How many competitors bot-fill tops a room up to. Solo runs and
        /// short-handed casual rooms fill to a full field (== MaxPlayers).</summary>
        public const int BotFillTarget = 12;

        // ── Phase timings (milliseconds) ─────────────────────────────────
        public const int IntroMs = 5400;
        public const int GoMs = 3200;
        public const int ResultMs = 6000;
        public const int SeriesResultMs = 30000;
        public const int EmptyGraceMs = 30000;
        public const int HeartbeatMs = 30000;

        // ── Night mode vision (units) ────────────────────────────────────
        public const float NightBaseVision = 250f;
        public const float NightLanternBonus = 320f;

        // ── Currency ─────────────────────────────────────────────────────
        public const string Currency = "Marbles";
        public const string CurrencyIcon = "◍";
    }
}
