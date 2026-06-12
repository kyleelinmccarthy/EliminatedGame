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
            { PowerupKind.Speed, PowerupKind.Shield, PowerupKind.Tiny, PowerupKind.Vision };
        private static readonly PowerupKind[] Bad =
            { PowerupKind.Reverse, PowerupKind.Slow, PowerupKind.Giant, PowerupKind.Dizzy };

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
            float x, y;

            if (_spawnRegions != null)
            {
                var regions = _spawnRegions();
                if (regions == null || regions.Count == 0) return null;
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

            return new Pickup { Id = _nextId++, Kind = kind, X = x, Y = y, Bob = _rng.NextFloat() * 6.2831853f };
        }

        /// <summary>Drop pickups the caller no longer wants (e.g. swallowed by lava).</summary>
        public void Cull(Func<Pickup, bool> keep) => _pickups.RemoveAll(p => !keep(p));

        /// <summary>If <paramref name="a"/> overlaps a pickup, apply it and return its kind.</summary>
        public PowerupKind? Collect(Actor a)
        {
            for (int i = _pickups.Count - 1; i >= 0; i--)
            {
                var pk = _pickups[i];
                float dx = a.Pos.X - pk.X, dy = a.Pos.Y - pk.Y;
                if (dx * dx + dy * dy < Sqr(Constants.PlayerRadius * a.Scale + 18f))
                {
                    PowerupEffects.Apply(a, pk.Kind);
                    _pickups.RemoveAt(i);
                    _emit?.Invoke(new Effect(EffectKind.Pickup, a.Pos.X, a.Pos.Y - 40f, 0f, pk.Kind.ToString()));
                    return pk.Kind;
                }
            }
            return null;
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
