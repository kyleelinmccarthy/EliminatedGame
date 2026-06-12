using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    /// <summary>
    /// Regression cover for the "all the bots ran to the upper-left corner" bug.
    /// The non-arena games (no free movement) used to never write Actor.Pos, so
    /// the shared top-down view drew every player at logical (0,0). Each of these
    /// games now lays its actors out from BuildSnapshot; these tests assert the
    /// players end up spread across the arena instead of piled at the origin.
    /// </summary>
    public class ActorLayoutTests
    {
        private static List<Actor> MakeActors(int n)
        {
            // Start everyone at the origin — exactly the broken state the view saw.
            var actors = new List<Actor>();
            for (int i = 0; i < n; i++)
                actors.Add(new Actor { Id = "p" + i, Name = "P" + i, IsBot = i % 2 == 0, Pos = new Vec2(0, 0) });
            return actors;
        }

        public static IEnumerable<object[]> NonArenaGames()
        {
            yield return new object[] { "TugOfWar", (Func<GameContext, IMinigame>)(c => new TugOfWar(c)) };
            yield return new object[] { "RpsMinusOne", (Func<GameContext, IMinigame>)(c => new RpsMinusOne(c)) };
            yield return new object[] { "JumpRope", (Func<GameContext, IMinigame>)(c => new JumpRope(c)) };
            yield return new object[] { "GlassBridge", (Func<GameContext, IMinigame>)(c => new GlassBridge(c)) };
            yield return new object[] { "PresentSwap", (Func<GameContext, IMinigame>)(c => new PresentSwap(c)) };
            yield return new object[] { "SimonSays", (Func<GameContext, IMinigame>)(c => new SimonSays(c)) };
            yield return new object[] { "ChutesAndLadders", (Func<GameContext, IMinigame>)(c => new ChutesAndLadders(c)) };
        }

        [Theory]
        [MemberData(nameof(NonArenaGames))]
        public void Lays_actors_out_across_the_arena_not_at_the_origin(string name, Func<GameContext, IMinigame> make)
        {
            var actors = MakeActors(8);
            var ctx = new GameContext { Rng = new Rng(5), Actors = actors };
            var game = make(ctx);
            game.Start();

            // Sample the layout over the opening of the round (several frames).
            for (int frame = 0; frame < 12; frame++)
            {
                var snap = game.BuildSnapshot();
                Assert.NotNull(snap);

                var alive = actors.Where(a => a.Alive).ToList();
                Assert.NotEmpty(alive);

                foreach (var a in alive)
                {
                    Assert.False(a.Pos.X == 0f && a.Pos.Y == 0f,
                        $"{name}: actor {a.Id} still sits at the origin (upper-left corner).");
                    Assert.InRange(a.Pos.X, 0f, Constants.ArenaW);
                    Assert.InRange(a.Pos.Y, 0f, Constants.ArenaH);
                }

                // The whole field must not collapse onto a single point.
                if (alive.Count >= 2)
                {
                    int distinct = alive
                        .Select(a => ((int)Math.Round(a.Pos.X / 8f), (int)Math.Round(a.Pos.Y / 8f)))
                        .Distinct().Count();
                    Assert.True(distinct >= 2, $"{name}: every player landed on the same spot.");
                }

                if (game.IsDone) break;
                for (int t = 0; t < 6; t++) game.Tick(Constants.Dt);
            }
        }

        [Fact]
        public void TugOfWar_puts_the_two_teams_on_opposite_sides_of_the_rope()
        {
            var actors = MakeActors(8);
            var ctx = new GameContext { Rng = new Rng(2), Actors = actors };
            var g = new TugOfWar(ctx);
            g.Start();
            g.BuildSnapshot();

            var team0 = actors.Where(a => g.TeamOf(a.Id) == 0).Select(a => a.Pos.X).ToList();
            var team1 = actors.Where(a => g.TeamOf(a.Id) == 1).Select(a => a.Pos.X).ToList();
            Assert.NotEmpty(team0);
            Assert.NotEmpty(team1);

            // One team is entirely left of the rope, the other entirely right.
            bool zeroIsLeft = team0.Max() < team1.Min();
            bool oneIsLeft = team1.Max() < team0.Min();
            Assert.True(zeroIsLeft || oneIsLeft, "tug-of-war teams overlap instead of facing off across the rope.");
        }

        [Fact]
        public void ChutesAndLadders_climber_moves_up_the_board_as_its_square_rises()
        {
            // A single climber low on the board should render lower (greater y,
            // since logical y points down) than one near the goal.
            var lowActors = MakeActors(2);
            var ctx = new GameContext { Rng = new Rng(1), Actors = lowActors };
            var g = new ChutesAndLadders(ctx);
            g.Start();
            g.BuildSnapshot();

            // Drive a few rolls so climbers spread up the board, then confirm the
            // climber on the higher square renders higher up (smaller y).
            for (int i = 0; i < 30 && !g.IsDone; i++)
            {
                g.OnInput("p0", GameInput.Action("roll"));
                g.Tick(Constants.Dt);
            }
            g.BuildSnapshot();
            var a0 = lowActors.First(a => a.Id == "p0");
            var a1 = lowActors.First(a => a.Id == "p1");
            if (g.SquareOf("p0") > g.SquareOf("p1"))
                Assert.True(a0.Pos.Y <= a1.Pos.Y + 1f, "higher square should not render below a lower one.");
        }

        [Fact]
        public void TugOfWar_formation_slides_toward_the_winning_team()
        {
            // Team 0 stands on the LEFT, so when team 0 wins (ropePos > 0) the whole
            // rope formation must slide LEFT toward them — not right toward the loser.
            var actors = MakeActors(8);
            var ctx = new GameContext { Rng = new Rng(4), Actors = actors };
            var g = new TugOfWar(ctx);
            g.Start();
            g.BuildSnapshot();
            float baseMean = actors.Average(a => a.Pos.X); // at rest ≈ arena centre

            // Make team 0 win by mashing all of its pullers every tick.
            var team0 = actors.Where(a => g.TeamOf(a.Id) == 0).Select(a => a.Id).ToList();
            for (int i = 0; i < 80 && !g.IsDone && g.RopePos < 0.4f; i++)
            {
                foreach (var id in team0) g.OnInput(id, GameInput.Tap());
                g.Tick(Constants.Dt);
            }
            Assert.True(g.RopePos > 0.2f, $"team 0 should be clearly winning (ropePos was {g.RopePos:0.00}).");
            g.BuildSnapshot();
            float wonMean = actors.Average(a => a.Pos.X);

            Assert.True(wonMean < baseMean - 30f,
                $"formation should slide left toward the winning team 0 (base {baseMean:0}, now {wonMean:0}).");
        }

        [Fact]
        public void GlassBridge_active_walker_stands_on_its_frontier_pane()
        {
            // The walker's x must use the SAME basis as the view's panes
            // (Lerp(300,1030,(frontier+0.5)/rows)) so it isn't drawn floating off-pane.
            var actors = MakeActors(8);
            var ctx = new GameContext { Rng = new Rng(7), Actors = actors };
            var g = new GlassBridge(ctx);
            g.Start();
            g.BuildSnapshot();

            var active = actors.FirstOrDefault(a => a.Id == g.ActiveId);
            Assert.NotNull(active);
            float expectedX = 300f + (1030f - 300f) * ((g.Frontier + 0.5f) / g.Rows);
            Assert.True(Math.Abs(active.Pos.X - expectedX) < 1f,
                $"active walker x {active.Pos.X:0} should sit on its pane at {expectedX:0}.");
            Assert.True(Math.Abs(active.Pos.Y - 360f) < 1f, "active walker should be centred on the bridge (y≈360).");
        }
    }
}
