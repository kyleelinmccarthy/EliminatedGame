using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Eliminated.Sim.Room;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    /// <summary>
    /// Sweeps every registered minigame, runs it to completion with a full field
    /// of bots, and asserts a clean result. A regression guard that no game can be
    /// added to the catalog without a finite, well-formed completion path. This is
    /// the headless analogue of the reference game's `npm run smoke:all`.
    /// </summary>
    public class AllGamesSmokeTests
    {
        private static List<Actor> Field(int n) =>
            Enumerable.Range(0, n).Select(i => new Actor { Id = "b" + i, Name = "Bot" + i, IsBot = true }).ToList();

        public static IEnumerable<object[]> AllGames =>
            GameCatalog.Registered.Select(id => new object[] { id });

        [Theory]
        [MemberData(nameof(AllGames))]
        public void Every_game_completes_with_a_full_field(GameId id)
        {
            const int n = 8;
            var actors = Field(n);
            var ctx = new GameContext { Rng = new Rng(1234), Actors = actors, Intensity = 0.5f };
            var g = GameCatalog.Create(id, ctx);
            g.Start();

            int ticks = 0;
            while (!g.IsDone && ticks < 200 * 20) { g.Tick(Constants.Dt); ticks++; }

            Assert.True(g.IsDone, $"{id} did not finish");
            var r = g.Result();
            Assert.Equal(n, r.Ranking.Count);
            Assert.NotEmpty(r.SurvivorIds);
            // placements are a clean permutation of 1..n
            Assert.Equal(Enumerable.Range(1, n), r.Ranking.Select(e => e.Placement).OrderBy(x => x));
            // every actor appears exactly once
            Assert.Equal(n, r.Ranking.Select(e => e.PlayerId).Distinct().Count());
        }

        [Theory]
        [MemberData(nameof(AllGames))]
        public void Finale_capable_games_crown_exactly_one(GameId id)
        {
            var meta = GameCatalog.Of(id);
            if (!meta.Finale && !meta.FinaleCapable) return; // only finale-capable games

            const int n = 8;
            var actors = Field(n);
            var ctx = new GameContext
            {
                Rng = new Rng(99), Actors = actors, Intensity = 0.9f,
                IsFinale = true, ForceSingleSurvivor = true
            };
            var g = GameCatalog.Create(id, ctx);
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 200 * 20) { g.Tick(Constants.Dt); ticks++; }

            Assert.True(g.IsDone, $"{id} finale did not finish");
            Assert.Single(g.Result().SurvivorIds);
        }
    }
}
