using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Xunit;

namespace Eliminated.Sim.Tests.Core
{
    /// <summary>
    /// The simulation must be deterministic from a seed (reproducible tests,
    /// replays, and netcode reconciliation). These tests pin that contract.
    /// </summary>
    public class RngTests
    {
        [Fact]
        public void Same_seed_produces_same_sequence()
        {
            var a = new Rng(12345);
            var b = new Rng(12345);
            for (int i = 0; i < 1000; i++)
                Assert.Equal(a.NextDouble(), b.NextDouble());
        }

        [Fact]
        public void Different_seeds_diverge()
        {
            var a = new Rng(1);
            var b = new Rng(2);
            var sa = Enumerable.Range(0, 50).Select(_ => a.NextDouble()).ToList();
            var sb = Enumerable.Range(0, 50).Select(_ => b.NextDouble()).ToList();
            Assert.NotEqual(sa, sb);
        }

        [Fact]
        public void NextDouble_is_in_unit_interval()
        {
            var r = new Rng(99);
            for (int i = 0; i < 10000; i++)
            {
                double v = r.NextDouble();
                Assert.InRange(v, 0.0, 0.99999999);
            }
        }

        [Fact]
        public void NextInt_respects_half_open_bounds()
        {
            var r = new Rng(7);
            var seen = new HashSet<int>();
            for (int i = 0; i < 10000; i++)
            {
                int v = r.NextInt(3, 8); // [3, 8)
                Assert.InRange(v, 3, 7);
                seen.Add(v);
            }
            Assert.Equal(new[] { 3, 4, 5, 6, 7 }, seen.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Range_maps_into_min_max()
        {
            var r = new Rng(42);
            for (int i = 0; i < 10000; i++)
            {
                double v = r.Range(-2.0, 5.0);
                Assert.InRange(v, -2.0, 5.0);
            }
        }

        [Fact]
        public void Pick_returns_an_element_and_is_deterministic()
        {
            var items = new[] { "a", "b", "c", "d" };
            var r1 = new Rng(5);
            var r2 = new Rng(5);
            for (int i = 0; i < 20; i++)
            {
                var p = r1.Pick(items);
                Assert.Contains(p, items);
                Assert.Equal(p, r2.Pick(items));
            }
        }

        [Fact]
        public void Shuffle_is_a_permutation_and_deterministic()
        {
            var src = Enumerable.Range(0, 12).ToList();
            var r1 = new Rng(2024);
            var r2 = new Rng(2024);
            var s1 = r1.Shuffle(src);
            var s2 = r2.Shuffle(src);
            Assert.Equal(s1, s2);                       // deterministic
            Assert.Equal(src.OrderBy(x => x), s1.OrderBy(x => x)); // same multiset
            Assert.Equal(12, s1.Count);
        }

        [Fact]
        public void Chance_is_bounded_and_deterministic()
        {
            var r = new Rng(1);
            Assert.False(r.Chance(0.0)); // never
            var r2 = new Rng(1);
            Assert.True(r2.Chance(1.0));  // always
        }
    }
}
