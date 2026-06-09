using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class MusicalChairsTests
    {
        private static (MusicalChairs game, List<Actor> actors) Make(int humans, int bots, int seed = 1, float intensity = 0.3f)
        {
            var actors = new List<Actor>();
            for (int i = 0; i < humans; i++) actors.Add(new Actor { Id = "h" + i });
            for (int i = 0; i < bots; i++) actors.Add(new Actor { Id = "b" + i, IsBot = true });
            var ctx = new GameContext { Rng = new Rng(seed), Actors = actors, Intensity = intensity };
            var g = new MusicalChairs(ctx);
            g.Start();
            return (g, actors);
        }

        [Fact]
        public void Standing_still_during_the_music_gets_you_eliminated()
        {
            var (g, actors) = Make(1, 5, seed: 4);
            var slacker = actors[0]; // human, no input → never moves
            for (int i = 0; i < 50; i++) g.Tick(Constants.Dt); // ~2.5s, still within the music
            Assert.False(slacker.Alive);
        }

        [Fact]
        public void Chairs_only_appear_once_the_music_stops()
        {
            var (g, _) = Make(0, 6, seed: 5);
            Assert.Equal(MusicalChairs.McPhase.Music, g.CurrentPhase);
            Assert.Equal(0, g.ChairCount);

            int ticks = 0;
            while (g.CurrentPhase == MusicalChairs.McPhase.Music && ticks < 200) { g.Tick(Constants.Dt); ticks++; }
            Assert.Equal(MusicalChairs.McPhase.Scramble, g.CurrentPhase);
            Assert.True(g.ChairCount > 0);
        }

        [Fact]
        public void Full_bot_game_completes_and_ranks_everyone()
        {
            var (g, _) = Make(0, 8, seed: 6, intensity: 0.8f);
            int ticks = 0;
            while (!g.IsDone && ticks < 60 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(8, g.Result().Ranking.Count);
        }
    }
}
