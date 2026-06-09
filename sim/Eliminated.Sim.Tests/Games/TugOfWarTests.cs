using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class TugOfWarTests
    {
        private static (TugOfWar game, List<Actor> actors) Make(int humans, int bots, int seed = 1)
        {
            var actors = new List<Actor>();
            for (int i = 0; i < humans; i++)
                actors.Add(new Actor { Id = "h" + i, Name = "Human" + i, IsBot = false });
            for (int i = 0; i < bots; i++)
                actors.Add(new Actor { Id = "b" + i, Name = "Bot" + i, IsBot = true });
            var ctx = new GameContext { Rng = new Rng(seed), Actors = actors };
            var g = new TugOfWar(ctx);
            g.Start();
            return (g, actors);
        }

        [Fact]
        public void Teams_split_evenly()
        {
            var (g, actors) = Make(0, 4);
            int t0 = actors.Count(a => g.TeamOf(a.Id) == 0);
            int t1 = actors.Count(a => g.TeamOf(a.Id) == 1);
            Assert.Equal(2, t0);
            Assert.Equal(2, t1);
        }

        [Fact]
        public void Team_assignment_is_deterministic_per_seed()
        {
            var (g1, a1) = Make(2, 2, seed: 77);
            var (g2, a2) = Make(2, 2, seed: 77);
            foreach (var a in a1)
                Assert.Equal(g1.TeamOf(a.Id), g2.TeamOf(a.Id));
        }

        [Fact]
        public void Tap_rate_is_capped_per_second()
        {
            var (g, _) = Make(1, 1);
            string human = "h0";
            for (int i = 0; i < 100; i++) // all within the same second (no tick)
                g.OnInput(human, GameInput.Tap());
            Assert.Equal(14, g.TapsFor(human)); // MAX_TAPS_PER_SEC
        }

        [Fact]
        public void Pull_action_counts_like_a_tap()
        {
            var (g, _) = Make(1, 1);
            g.OnInput("h0", GameInput.Action("pull"));
            Assert.Equal(1, g.TapsFor("h0"));
        }

        [Fact]
        public void Furious_human_mashing_drags_the_other_team_into_the_pit()
        {
            var (g, _) = Make(1, 1, seed: 3);
            int humanTeam = g.TeamOf("h0");

            for (int i = 0; i < 30 * 20; i++) // up to 30s of ticks
            {
                g.OnInput("h0", GameInput.Tap()); // mash every tick (capped to 14/s)
                g.Tick(Constants.Dt);
                if (g.IsDone) break;
            }

            Assert.True(g.IsDone);
            Assert.NotEqual(humanTeam, g.LoserTeam);     // the human's team won
            var r = g.Result();
            Assert.Contains("h0", r.SurvivorIds);
        }

        [Fact]
        public void Bots_tap_on_their_own_over_time()
        {
            var (g, _) = Make(0, 2);
            for (int i = 0; i < 40; i++) g.Tick(Constants.Dt); // 2 seconds
            Assert.True(g.TapsFor("b0") > 0);
        }

        [Fact]
        public void Match_resolves_by_the_time_limit_at_the_latest()
        {
            var (g, _) = Make(0, 4, seed: 9);
            int ticks = 0;
            while (!g.IsDone && ticks < 31 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            var r = g.Result();
            Assert.NotEmpty(r.SurvivorIds);
            Assert.Equal(4, r.Ranking.Count);
        }

        [Fact]
        public void Forfeit_drops_a_puller_from_the_rope()
        {
            var (g, _) = Make(2, 2);
            g.Forfeit("h0");
            Assert.Equal(-1, g.TeamOf("h0")); // no longer on the rope
        }
    }
}
