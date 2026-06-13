using System;
using System.Collections.Generic;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Powerups
{
    /// <summary>A circular region powerups may spawn inside (e.g. a lava island).</summary>
    public struct SpawnRegion
    {
        public float X, Y, R;
        public SpawnRegion(float x, float y, float r) { X = x; Y = y; R = r; }
    }

    /// <summary>One powerup orb on the field.</summary>
    public sealed class Pickup
    {
        public int Id;
        public PowerupKind Kind;
        public float X, Y;
        public float Bob;
    }

    /// <summary>Lightweight DTO for snapshots.</summary>
    public struct PickupView
    {
        public int Id;
        public string Kind;
        public float X, Y, Bob;
        public bool Good; // beneficial (green) vs curse (red) — set at build time so the view
                          // needn't parse Kind (which fails for game-specific powerups like
                          // Boomerang's BigRang/Multishot/Magnet, wrongly drawing them red)
    }

    /// <summary>
    /// Reusable powerup field for the arena games. Spawns orbs on a cadence;
    /// walking over one applies it via the shared status timers. Optionally
    /// constrains spawns to dynamic regions (the shrinking lava islands). Ported
    /// from lib/server/games/Powerups.ts.
    /// </summary>
    public sealed class PowerupField
    {
        private static readonly PowerupKind[] Good =
            { PowerupKind.Speed, PowerupKind.Shield, PowerupKind.Tiny, PowerupKind.Vision,
              PowerupKind.Caffeine, PowerupKind.Disguise };
        // Curses + the Jumble wildcard share the "not good" roll — grabbing an orb
        // is always a gamble.
        private static readonly PowerupKind[] Bad =
            { PowerupKind.Reverse, PowerupKind.Slow, PowerupKind.Giant, PowerupKind.Dizzy,
              PowerupKind.Slippery, PowerupKind.Jumble };

        private readonly Rng _rng;
        private readonly float _every;
        private readonly int _max;
        private readonly float _goodWeight;
        private readonly float _margin;
        private readonly Func<List<SpawnRegion>> _spawnRegions;
        private readonly Action<Effect> _emit;

        private readonly List<Pickup> _pickups = new List<Pickup>();
        private float _spawnTimer;
        private int _nextId = 1;

        public IReadOnlyList<Pickup> Pickups => _pickups;

        public PowerupField(Rng rng, float every = 3.5f, int max = 4, float goodWeight = 0.58f,
            float margin = 150f, float firstDelay = 1.5f,
            Func<List<SpawnRegion>> spawnRegions = null, Action<Effect> emit = null)
        {
            _rng = rng;
            _every = every;
            _max = max;
            _goodWeight = goodWeight;
            _margin = margin;
            _spawnRegions = spawnRegions;
            _emit = emit;
            _spawnTimer = firstDelay;
        }

        public void Tick(float dt)
        {
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f && _pickups.Count < _max)
            {
                var pk = Spawn();
                if (pk != null)
                {
                    _spawnTimer = _every + _rng.NextFloat() * _every;
                    _pickups.Add(pk);
                }
                else
                {
                    _spawnTimer = 0.6f; // nowhere safe yet — retry soon
                }
            }
            foreach (var p in _pickups) p.Bob += dt * 4f;
        }

        private Pickup Spawn()
        {
            var pool = _rng.NextFloat() < _goodWeight ? Good : Bad;
            var kind = pool[_rng.NextInt(pool.Length)];
            if (!RandomPoint(out float x, out float y)) return null; // nowhere safe yet
            return new Pickup { Id = _nextId++, Kind = kind, X = x, Y = y, Bob = _rng.NextFloat() * 6.2831853f };
        }

        /// <summary>A uniform random point inside the spawnable area (a random
        /// usable region if constrained, else the arena minus the margin). Shared
        /// by orb spawning and the Jumble warp. False = no usable region yet.</summary>
        private bool RandomPoint(out float x, out float y)
        {
            x = y = 0f;
            if (_spawnRegions != null)
            {
                var regions = _spawnRegions();
                if (regions == null || regions.Count == 0) return false;
                // weight by usable area, pick a region, then a uniform point in it
                float total = 0f;
                var usable = new float[regions.Count];
                for (int i = 0; i < regions.Count; i++)
                {
                    float u = Math.Max(0f, regions[i].R - _margin);
                    usable[i] = u;
                    total += u * u;
                }
                var pick = regions[0];
                float pickR = usable[0];
                if (total > 0f)
                {
                    float r = _rng.NextFloat() * total;
                    for (int i = 0; i < regions.Count; i++)
                    {
                        r -= usable[i] * usable[i];
                        if (r <= 0f) { pick = regions[i]; pickR = usable[i]; break; }
                    }
                }
                float ang = _rng.NextFloat() * 6.2831853f;
                float rad = (float)Math.Sqrt(_rng.NextFloat()) * pickR;
                x = pick.X + (float)Math.Cos(ang) * rad;
                y = pick.Y + (float)Math.Sin(ang) * rad;
            }
            else
            {
                x = _margin + _rng.NextFloat() * (Constants.ArenaW - 2f * _margin);
                y = _margin + _rng.NextFloat() * (Constants.ArenaH - 2f * _margin);
            }
            return true;
        }

        /// <summary>Drop pickups the caller no longer wants (e.g. swallowed by lava).</summary>
        public void Cull(Func<Pickup, bool> keep) => _pickups.RemoveAll(p => !keep(p));

        /// <summary>Spawn a specific pickup at a position (scripted drops / tests).</summary>
        public void AddPickup(PowerupKind kind, float x, float y)
            => _pickups.Add(new Pickup { Id = _nextId++, Kind = kind, X = x, Y = y, Bob = 0f });

        /// <summary>If <paramref name="a"/> overlaps a pickup, apply it and return
        /// its kind. <paramref name="all"/> (the round's actors) lets the wildcards
        /// reach the rest of the field: Disguise borrows another player's identity,
        /// Jumble warps you elsewhere.</summary>
        public PowerupKind? Collect(Actor a, IReadOnlyList<Actor> all = null)
        {
            for (int i = _pickups.Count - 1; i >= 0; i--)
            {
                var pk = _pickups[i];
                float dx = a.Pos.X - pk.X, dy = a.Pos.Y - pk.Y;
                if (dx * dx + dy * dy < Sqr(Constants.PlayerRadius * a.Scale + 18f))
                {
                    PowerupEffects.Apply(a, pk.Kind);
                    _pickups.RemoveAt(i);
                    // The reveal floats where you grabbed it (icon + name, green/red/purple).
                    _emit?.Invoke(new Effect(EffectKind.Pickup, a.Pos.X, a.Pos.Y - 40f, 0f, pk.Kind.ToString()));
                    if (pk.Kind == PowerupKind.Disguise) ApplyDisguise(a, all);
                    else if (pk.Kind == PowerupKind.Jumble) ApplyJumble(a);
                    return pk.Kind;
                }
            }
            return null;
        }

        /// <summary>Disguise: wear a random OTHER living player's face. Fizzles
        /// (no effect) if there's nobody to impersonate.</summary>
        private void ApplyDisguise(Actor a, IReadOnlyList<Actor> all)
        {
            if (all == null) { a.PuDisguiseT = 0f; return; }
            Actor pick = null; int seen = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == a || !o.Alive || string.IsNullOrEmpty(o.CharacterId)) continue;
                seen++;
                if (_rng.NextInt(seen) == 0) pick = o; // reservoir sample → uniform pick
            }
            if (pick != null) { a.DisguiseCharId = pick.CharacterId; a.DisguiseNumber = pick.Number; }
            else { a.PuDisguiseT = 0f; a.DisguiseCharId = null; a.DisguiseNumber = 0; } // nobody to mimic — fizzle cleanly
        }

        /// <summary>Jumble: blink to a random spot. Deliberately the WHOLE arena (not
        /// the safe spawn regions), so in King of the Lava Islands it really can drop
        /// you in the magma — you get the burn grace / a Bubble to scramble out.
        /// "Could be safety. Could be lava."</summary>
        private void ApplyJumble(Actor a)
        {
            const float m = 70f;
            float nx = m + _rng.NextFloat() * (Constants.ArenaW - 2f * m);
            float ny = m + _rng.NextFloat() * (Constants.ArenaH - 2f * m);
            _emit?.Invoke(new Effect(EffectKind.Spark, a.Pos.X, a.Pos.Y));
            a.Pos = new Vec2(nx, ny);
            a.Vel = Vec2.Zero;
            _emit?.Invoke(new Effect(EffectKind.Spark, nx, ny));
        }

        public List<PickupView> Snapshot()
        {
            var list = new List<PickupView>(_pickups.Count);
            foreach (var p in _pickups)
                list.Add(new PickupView { Id = p.Id, Kind = p.Kind.ToString(), X = p.X, Y = p.Y, Bob = p.Bob, Good = PowerupEffects.IsGood(p.Kind) });
            return list;
        }

        private static float Sqr(float v) => v * v;
    }
}
