using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class DodgeballTests
    {
        private static (Dodgeball game, List<Actor> actors) Make(int humans, int bots, int seed = 1)
        {
            var actors = new List<Actor>();
            for (int i = 0; i < humans; i++) actors.Add(new Actor { Id = "h" + i });
            for (int i = 0; i < bots; i++) actors.Add(new Actor { Id = "b" + i, IsBot = true });
            var ctx = new GameContext { Rng = new Rng(seed), Actors = actors };
            var g = new Dodgeball(ctx);
            g.Start();
            return (g, actors);
        }

        private const float Mid = 640f;

        [Fact]
        public void Teams_split_left_and_right()
        {
            var (_, actors) = Make(0, 6);
            Assert.Equal(3, actors.Count(a => a.Team == 0));
            Assert.Equal(3, actors.Count(a => a.Team == 1));
            Assert.All(actors.Where(a => a.Team == 0), a => Assert.True(a.Pos.X < Mid));
            Assert.All(actors.Where(a => a.Team == 1), a => Assert.True(a.Pos.X > Mid));
        }

        [Fact]
        public void A_player_cannot_cross_the_center_line()
        {
            var (g, actors) = Make(2, 0, seed: 3);
            var left = actors.First(a => a.Team == 0);
            for (int i = 0; i < 60; i++) { g.OnInput(left.Id, GameInput.Move(1, 0)); g.Tick(Constants.Dt); if (g.IsDone) break; }
            Assert.True(left.Pos.X <= Mid - 8f - Constants.PlayerRadius + 0.5f);
        }

        [Fact]
        public void A_thrown_ball_pegs_an_enemy()
        {
            var (g, actors) = Make(2, 0, seed: 2);
            var thrower = actors.First(a => a.Team == 0);
            var victim = actors.First(a => a.Team == 1);
            thrower.Pos = new Vec2(600, 360);  // beside the centerline ball at (640,360)
            victim.Pos = new Vec2(700, 360);

            g.Tick(Constants.Dt); // picks up the ball
            Assert.Equal("ball", thrower.Carrying);

            g.OnInput(thrower.Id, GameInput.Aim(0f)); // aim east at the victim
            g.OnInput(thrower.Id, GameInput.Action("throw"));
            for (int i = 0; i < 12 && victim.Alive; i++) g.Tick(Constants.Dt);
            Assert.False(victim.Alive);
        }

        [Fact]
        public void Full_bot_game_completes_and_ranks_everyone()
        {
            var (g, actors) = Make(0, 6, seed: 7);
            int ticks = 0;
            while (!g.IsDone && ticks < 46 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(6, g.Result().Ranking.Count);
        }
    }
}
