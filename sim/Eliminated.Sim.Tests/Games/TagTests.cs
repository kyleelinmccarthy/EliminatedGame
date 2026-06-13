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
        public void Field_is_a_few_freezers_against_a_runner_majority()
        {
            // Asymmetric, NOT even teams: at most TWO "it" freezers vs a big runner crowd.
            var (_, actors) = Make(0, 12);
            int expected = Tag.FreezerCount(12); // capped at 2
            Assert.True(expected <= 2);
            Assert.Equal(expected, actors.Count(a => a.Team == Freezer));
            Assert.Equal(12 - expected, actors.Count(a => a.Team == Runner));
            Assert.True(actors.Count(a => a.Team == Runner) > actors.Count(a => a.Team == Freezer)); // runner majority
            Assert.All(actors.Where(a => a.Team == Freezer), a => Assert.True(a.It));
        }

        [Fact]
        public void Freezer_count_scales_but_always_leaves_a_runner_majority()
        {
            foreach (int n in new[] { 3, 4, 6, 8, 12, 16 })
            {
                int f = Tag.FreezerCount(n);
                Assert.True(f >= 1, $"n={n} must have at least one freezer");
                Assert.True(n - f > f, $"n={n}: runners ({n - f}) must outnumber freezers ({f})");
            }
        }

        [Fact]
        public void A_freezer_touching_a_runner_freezes_them()
        {
            var (g, actors) = Make(12, 0, seed: 2); // humans → no autonomous movement; 12 → 2 freezers
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
            var (g, actors) = Make(12, 0, seed: 2); // 12 → 2 freezers, 10 runners
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
        public void A_rescuer_thaws_even_while_a_chasing_freezer_catches_them()
        {
            // Running over a frozen ally must FREE them even if a freezer is right on the
            // rescuer's tail — thaw resolves before freeze, so the rescue lands and the
            // rescuer (not the saved ally) is the one who may get caught. Previously the
            // freeze pass ran first and froze the rescuer, so the thaw silently did nothing.
            var (g, actors) = Make(12, 0, seed: 2);
            var freezers = actors.Where(a => a.Team == Freezer).ToList();
            var runners = actors.Where(a => a.Team == Runner).ToList();
            freezers[1].Pos = new Vec2(1240, 700);          // park the second hunter far off
            var victim = runners[0]; victim.Frozen = true; victim.Pos = new Vec2(400, 300);
            var rescuer = runners[1]; rescuer.Pos = new Vec2(380, 300); // overlapping the victim
            var chaser = freezers[0]; chaser.Pos = new Vec2(360, 300);  // right on the rescuer
            for (int i = 2; i < runners.Count; i++) runners[i].Pos = new Vec2(100, 50 + i * 10);

            g.Tick(Constants.Dt);

            Assert.False(victim.Frozen);  // rescue landed despite the chaser
            Assert.True(rescuer.Frozen);  // ...and the brave rescuer paid for it
        }

        [Fact]
        public void A_runner_bot_proactively_thaws_a_frozen_teammate()
        {
            // The whole point of Freeze Tag — a free runner should GO thaw a frozen friend when the
            // coast is clear, not only when the rescue happens to be nearer than the threat.
            var (g, actors) = Make(0, 12, seed: 4);
            var freezers = actors.Where(a => a.Team == Freezer).ToList();
            var runners = actors.Where(a => a.Team == Runner).ToList();
            foreach (var f in freezers) f.Pos = new Vec2(1240, 700);   // park all hunters far away
            var victim = runners[0]; victim.Frozen = true; victim.Pos = new Vec2(220, 200);
            var rescuer = runners[1]; rescuer.Pos = new Vec2(330, 200); // a free runner ~110 away
            for (int i = 2; i < runners.Count; i++) runners[i].Pos = new Vec2(640, 690);
            for (int i = 0; i < 40 && victim.Frozen; i++) g.Tick(Constants.Dt); // up to 2s
            Assert.False(victim.Frozen); // a runner bot came over and thawed them
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
