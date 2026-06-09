using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class TagTests
    {
        private static (Tag game, List<Actor> actors) Make(int humans, int bots, int seed = 1)
        {
            var actors = new List<Actor>();
            for (int i = 0; i < humans; i++) actors.Add(new Actor { Id = "h" + i, Name = "H" + i });
            for (int i = 0; i < bots; i++) actors.Add(new Actor { Id = "b" + i, Name = "B" + i, IsBot = true });
            var ctx = new GameContext { Rng = new Rng(seed), Actors = actors, Intensity = 0.3f };
            var g = new Tag(ctx);
            g.Start();
            return (g, actors);
        }

        private const int Freezer = 0;
        private const int Runner = 1;

        [Fact]
        public void Teams_split_into_freezers_and_runners()
        {
            var (_, actors) = Make(0, 6);
            Assert.Equal(3, actors.Count(a => a.Team == Freezer));
            Assert.Equal(3, actors.Count(a => a.Team == Runner));
            Assert.All(actors.Where(a => a.Team == Freezer), a => Assert.True(a.It));
        }

        [Fact]
        public void A_freezer_touching_a_runner_freezes_them()
        {
            var (g, actors) = Make(4, 0, seed: 2); // humans → no autonomous movement
            var freezers = actors.Where(a => a.Team == Freezer).ToList();
            var runners = actors.Where(a => a.Team == Runner).ToList();
            freezers[0].Pos = new Vec2(300, 300);
            freezers[1].Pos = new Vec2(1100, 600); // far, out of the way
            runners[0].Pos = new Vec2(315, 300);   // within FREEZE_R of freezers[0]
            runners[1].Pos = new Vec2(200, 600);   // far → keeps the round alive

            g.Tick(Constants.Dt);
            Assert.True(runners[0].Frozen);
            Assert.True(runners[0].Alive);
        }

        [Fact]
        public void A_free_runner_thaws_a_frozen_teammate()
        {
            var (g, actors) = Make(4, 0, seed: 2);
            var freezers = actors.Where(a => a.Team == Freezer).ToList();
            var runners = actors.Where(a => a.Team == Runner).ToList();
            freezers[0].Pos = new Vec2(1100, 600); // far — won't freeze anyone
            freezers[1].Pos = new Vec2(1100, 100);
            runners[0].Frozen = true;
            runners[0].Pos = new Vec2(300, 300);
            runners[1].Pos = new Vec2(315, 300); // adjacent, free → thaws teammate

            g.Tick(Constants.Dt);
            Assert.False(runners[0].Frozen);
        }

        [Fact]
        public void Full_bot_game_completes_and_ranks_everyone()
        {
            var (g, actors) = Make(0, 8, seed: 9);
            int ticks = 0;
            while (!g.IsDone && ticks < 40 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            var r = g.Result();
            Assert.Equal(8, r.Ranking.Count);
            Assert.NotEmpty(r.SurvivorIds);
        }
    }
}
