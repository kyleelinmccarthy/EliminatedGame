using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class PresentSwapTests
    {
        private static (PresentSwap game, List<Actor> actors) Make(int humans, int bots, int seed = 1, float intensity = 0.3f)
        {
            var actors = new List<Actor>();
            for (int i = 0; i < humans; i++) actors.Add(new Actor { Id = "h" + i });
            for (int i = 0; i < bots; i++) actors.Add(new Actor { Id = "b" + i, IsBot = true });
            var ctx = new GameContext { Rng = new Rng(seed), Actors = actors, Intensity = intensity };
            var g = new PresentSwap(ctx);
            g.Start();
            return (g, actors);
        }

        private static void TickUntilGuess(PresentSwap g)
        {
            for (int i = 0; i < 8 * 20 + 5 && g.Phase != "guess"; i++) g.Tick(Constants.Dt);
        }

        [Fact]
        public void Givers_and_receivers_are_disjoint()
        {
            var (g, _) = Make(8, 0, seed: 2);
            TickUntilGuess(g);
            Assert.Equal("guess", g.Phase);
            var givers = g.GiverIds.ToHashSet();
            var receivers = givers.Select(gid => g.ReceiverOf(gid)).ToHashSet();
            Assert.Empty(givers.Intersect(receivers));
            // every receiver is distinct (exactly one gift each)
            Assert.Equal(givers.Count, receivers.Count);
        }

        [Fact]
        public void A_correct_guess_catches_the_giver()
        {
            var (g, actors) = Make(8, 0, seed: 3);
            TickUntilGuess(g);
            string giver = g.GiverIds.First();
            string receiver = g.ReceiverOf(giver);
            g.OnInput(receiver, GameInput.Choose(giver)); // accuse the true giver
            for (int i = 0; i < 12 * 20 && g.Phase == "guess"; i++) g.Tick(Constants.Dt);

            Assert.False(actors.First(a => a.Id == giver).Alive);    // giver caught
            Assert.True(actors.First(a => a.Id == receiver).Alive);  // receiver safe
        }

        [Fact]
        public void A_wrong_guess_dooms_the_receiver()
        {
            var (g, actors) = Make(8, 0, seed: 5);
            TickUntilGuess(g);
            string giver = g.GiverIds.First();
            string receiver = g.ReceiverOf(giver);
            string wrong = g.CandidatesFor(receiver).First(c => c != giver);
            g.OnInput(receiver, GameInput.Choose(wrong));
            for (int i = 0; i < 12 * 20 && g.Phase == "guess"; i++) g.Tick(Constants.Dt);

            Assert.True(actors.First(a => a.Id == giver).Alive);     // giver got away
            Assert.False(actors.First(a => a.Id == receiver).Alive); // receiver took the fall
        }

        [Fact]
        public void Full_bot_game_completes_and_ranks_everyone()
        {
            var (g, _) = Make(0, 8, seed: 7, intensity: 0.8f);
            int ticks = 0;
            while (!g.IsDone && ticks < 60 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(8, g.Result().Ranking.Count);
        }
    }
}
