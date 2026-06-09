using System.Collections.Generic;
using Eliminated.Sim.Core;

namespace Eliminated.Sim.Model
{
    /// <summary>
    /// A blob participating in the active minigame. Holds transform, input intent,
    /// gameplay state, powerup status timers, and a per-game scratch bag. Ported
    /// from the reference Actor (lib/server/games/Minigame.ts).
    /// </summary>
    public sealed class Actor
    {
        public string Id;
        public string Name;
        public string CharacterId;
        public int Number;          // Squid-game style tag 1..456 (view only)
        public bool IsBot;

        // ── Transform ────────────────────────────────────────────────────
        public Vec2 Pos;
        public Vec2 Vel;
        public float Facing;        // radians

        // ── Input intent (normalized −1..1), set each tick ───────────────
        public float InDx;
        public float InDy;
        public float AimAngle;
        public bool HasAim;

        // ── Gameplay state ───────────────────────────────────────────────
        public bool Alive = true;
        public int Team = -1;       // −1 = no team
        public bool It;             // tag hunter / role flag
        public string Carrying;     // null, "ball", "boomerang"…
        public float Scale = 1f;
        public bool Ghost;          // dash blur visual
        public bool Shield;         // bubble powerup; absorbs one hit
        public bool Frozen;         // freeze tag
        public bool Burning;        // king-of-the-hill lava
        public float Flash;         // hurt flash 0..1 (view)
        public float Vision;        // night-mode vision radius (0 = use default)
        public AnimState Anim = AnimState.Idle;
        public float Progress;      // race/jump progress 0..1 (view)

        // ── Dash ─────────────────────────────────────────────────────────
        public float DashT;         // remaining burst time (s)
        public float DashCdT;       // remaining cooldown (s)
        public float IFrameT;       // invulnerability frames (s)

        // ── Powerup status timers (seconds remaining) ────────────────────
        public float PuSpeedT;      // ⚡ Zoomies
        public float PuSlowT;       // 🐌 Molasses
        public float PuReverseT;    // 🌀 Bamboozled
        public float PuDizzyT;      // 💫 Dizzy
        public float PuVisionT;     // 🔦 Lantern
        public float PuTinyT;       // 🔻 Shrink
        public float PuGiantT;      // 🎈 Embiggen

        // ── Per-game scratch (DRY: shared games avoid bespoke subclasses) ─
        private Dictionary<string, float> _data;
        public Dictionary<string, float> Data => _data ??= new Dictionary<string, float>();

        public float Get(string key, float fallback = 0f)
            => _data != null && _data.TryGetValue(key, out var v) ? v : fallback;

        public void Set(string key, float value) => Data[key] = value;

        /// <summary>Effective collision radius, scaled by powerups (shrink/giant).</summary>
        public float Radius => Constants.PlayerRadius * Scale;

        /// <summary>True while the actor cannot be hit (dash i-frames).</summary>
        public bool Invulnerable => IFrameT > 0f;
    }
}
