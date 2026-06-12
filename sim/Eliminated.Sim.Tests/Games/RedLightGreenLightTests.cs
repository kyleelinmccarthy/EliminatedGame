using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class RedLightGreenLightTests
    {
        private static (RedLightGreenLight game, List<Actor> actors) Make(
            int humans, int bots, int seed = 1)
        {
            var actors = new List<Actor>();
            for (int i = 0; i < humans; i++)
                actors.Add(new Actor { Id = "h" + i, Name = "H" + i, IsBot = false });
            for (int i = 0; i < bots; i++)
                actors.Add(new Actor { Id = "b" + i, Name = "B" + i, IsBot = true });
            var ctx = new GameContext { Rng = new Rng(seed), Actors = actors };
            var g = new RedLightGreenLight(ctx);
            g.Start();
            return (g, actors);
        }

        private static bool TickUntil(RedLightGreenLight g, System.Func<bool> cond, int maxTicks = 400)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (cond()) return true;
                g.Tick(Constants.Dt);
            }
            return cond();
        }

        [Fact]
        public void Starts_on_green()
        {
            var (g, _) = Make(1, 0);
            Assert.False(g.IsRed);
        }

        [Fact]
        public void Green_lets_a_player_advance_toward_the_finish()
        {
            var (g, actors) = Make(1, 0);
            var a = actors[0];
            float x0 = a.Pos.X;
            g.OnInput(a.Id, GameInput.Move(1, 0));
            g.Tick(Constants.Dt); // green, 150 * 0.05 = 7.5
            Assert.True(a.Pos.X > x0);
        }

        [Fact]
        public void Moving_on_red_after_the_grace_gets_you_caught()
        {
            var (g, actors) = Make(1, 0, seed: 4);
            var a = actors[0];

            Assert.True(TickUntil(g, () => g.IsRed));   // reach a red light
            // push past the grace window while mashing right
            for (int i = 0; i < 15 && a.Alive; i++)
            {
                g.OnInput(a.Id, GameInput.Move(1, 0));
                g.Tick(Constants.Dt);
            }
            Assert.False(a.Alive); // caught moving
        }

        [Fact]
        public void Standing_still_on_red_is_safe()
        {
            var (g, actors) = Make(1, 0, seed: 4);
            var a = actors[0];
            Assert.True(TickUntil(g, () => g.IsRed));
            // ride out the entire red phase with no input
            Assert.True(TickUntil(g, () => !g.IsRed, maxTicks: 120));
            Assert.True(a.Alive);
        }

        [Fact]
        public void Crossing_the_finish_line_makes_you_a_survivor()
        {
            var (g, actors) = Make(1, 0);
            var a = actors[0];
            a.Pos = new Vec2(RedLightGreenLight.FinishX - 5f, a.Pos.Y); // right at the line
            g.OnInput(a.Id, GameInput.Move(1, 0));                       // green at start
            g.Tick(Constants.Dt);
            var r = g.Result();
            Assert.Contains(a.Id, r.SurvivorIds);
            Assert.True(a.Alive);
        }

        [Fact]
        public void Full_bot_game_completes_and_ranks_everyone()
        {
            var (g, actors) = Make(0, 6, seed: 11);
            int ticks = 0;
            while (!g.IsDone && ticks < 71 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            var r = g.Result();
            Assert.Equal(6, r.Ranking.Count);
            // placements are a clean 1..6 with no gaps
            Assert.Equal(Enumerable.Range(1, 6), r.Ranking.Select(e => e.Placement).OrderBy(x => x));
        }

        [Fact]
        public void Survivors_rank_above_the_eliminated()
        {
            var (g, actors) = Make(0, 6, seed: 11);
            while (!g.IsDone) g.Tick(Constants.Dt);
            var r = g.Result();
            var survivors = r.Ranking.Where(e => e.Survived).ToList();
            var eliminated = r.Ranking.Where(e => !e.Survived).ToList();
            if (survivors.Any() && eliminated.Any())
                Assert.True(survivors.Max(e => e.Placement) < eliminated.Min(e => e.Placement));
        }
    }
}
