using System.Collections.Generic;

namespace Eliminated.Sim.Core
{
    /// <summary>
    /// Deterministic seedable PRNG. Port of the reference game's Mulberry32
    /// (lib/shared/util.ts) so sequences are reproducible — essential for
    /// test reproducibility, replays, and netcode reconciliation. Pure C#.
    /// </summary>
    public sealed class Rng
    {
        private uint _state;

        public Rng(uint seed)
        {
            _state = seed;
        }

        public Rng(int seed) : this(unchecked((uint)seed)) { }

        /// <summary>Next value in [0, 1). Mulberry32.</summary>
        public double NextDouble()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;
                uint t = _state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return ((t ^ (t >> 14)) & 0xFFFFFFFFu) / 4294967296.0;
            }
        }

        /// <summary>Next value in [0, 1) as a float.</summary>
        public float NextFloat() => (float)NextDouble();

        /// <summary>Uniform double in [min, max).</summary>
        public double Range(double min, double max) => min + NextDouble() * (max - min);

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + NextFloat() * (max - min);

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            int span = maxExclusive - minInclusive;
            return minInclusive + (int)(NextDouble() * span);
        }

        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        public int NextInt(int maxExclusive) => NextInt(0, maxExclusive);

        /// <summary>True with probability <paramref name="p"/> (clamped to [0,1]).</summary>
        public bool Chance(double p)
        {
            if (p <= 0.0) return false;
            if (p >= 1.0) return true;
            return NextDouble() < p;
        }

        /// <summary>Signed unit-ish jitter in [-1, 1).</summary>
        public float Signed() => (float)(NextDouble() * 2.0 - 1.0);

        public T Pick<T>(IReadOnlyList<T> items)
        {
            return items[NextInt(items.Count)];
        }

        /// <summary>Returns a new shuffled copy (Fisher–Yates); input untouched.</summary>
        public List<T> Shuffle<T>(IReadOnlyList<T> items)
        {
            var a = new List<T>(items);
            for (int i = a.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
            return a;
        }

        /// <summary>Shuffles a list in place (Fisher–Yates).</summary>
        public void ShuffleInPlace<T>(IList<T> a)
        {
            for (int i = a.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }
    }
}
