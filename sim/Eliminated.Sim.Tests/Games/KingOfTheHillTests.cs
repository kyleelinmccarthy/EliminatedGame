using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class KingOfTheHillTests
    {
        private static KingOfTheHill Make(List<Actor> actors, int seed, bool forceSingle = true)
            => Make(actors, seed, isFinale: true, forceSingle: forceSingle, intensity: 0.9f);

        private static KingOfTheHill Make(List<Actor> actors, int seed, bool isFinale, bool forceSingle, float intensity)
        {
            var ctx = new GameContext
            {
                Rng = new Rng(seed),
                Actors = actors,
                Intensity = intensity,
                IsFinale = isFinale,
                ForceSingleSurvivor = forceSingle
            };
            var g = new KingOfTheHill(ctx);
            g.Start();
            return g;
        }

        [Fact]
        public void Finale_completes_with_exactly_one_champion()
        {
            var actors = Enumerable.Range(0, 4).Select(i => new Actor { Id = "b" + i, IsBot = true }).ToList();
            var g = Make(actors, seed: 3);
            int ticks = 0;
            while (!g.IsDone && ticks < 61 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            var r = g.Result();
            Assert.Single(r.SurvivorIds);
            Assert.Equal(1, r.Ranking.Count(e => e.Survived));
            Assert.Equal(4, r.Ranking.Count);
        }

        [Fact]
        public void Regular_round_culls_toward_a_target_without_wiping_the_field()
        {
            // Not a finale: should leave several survivors, never collapse to one.
            var actors = Enumerable.Range(0, 12).Select(i => new Actor { Id = "b" + i, IsBot = true }).ToList();
            var g = Make(actors, seed: 7, isFinale: false, forceSingle: false, intensity: 0.3f);
            int ticks = 0;
            while (!g.IsDone && ticks < 61 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            var r = g.Result();
            Assert.True(r.SurvivorIds.Count > 1, $"expected multiple survivors, got {r.SurvivorIds.Count}");
            Assert.Equal(12, r.Ranking.Count);
        }

        [Fact]
        public void A_player_stranded_in_lava_burns_out()
        {
            var actors = Enumerable.Range(0, 4).Select(i => new Actor { Id = "h" + i, IsBot = false }).ToList();
            var g = Make(actors, seed: 1);
            var victim = actors[0];
            victim.Pos = new Vec2(40, 40); // corner — guaranteed lava

            // ~5s: past the 2.5s opening spawn-grace AND the 0.95s burn grace once it bites.
            for (int i = 0; i < 100 && victim.Alive; i++) g.Tick(Constants.Dt);
            Assert.False(victim.Alive);
        }

        [Fact]
        public void A_shove_launches_a_rival_away()
        {
            var actors = new List<Actor> { new Actor { Id = "a" }, new Actor { Id = "b" } };
            // 2 actors → would normally be last-player-protected; place them together.
            var g = Make(actors, seed: 1, forceSingle: false);
            var a = actors[0]; var b = actors[1];
            a.Pos = new Vec2(640, 360);
            b.Pos = new Vec2(685, 360); // just east, within shove reach

            g.OnInput("a", GameInput.Aim(0f));          // aim east
            g.OnInput("a", GameInput.Action("shove"));
            g.Tick(Constants.Dt);
            Assert.True(b.Pos.X > 685f); // knocked further east
        }
    }
}
