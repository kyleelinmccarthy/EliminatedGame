using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class DiscreteGamesTests
    {
        private static GameContext Ctx(List<Actor> actors, int seed, bool forceSingle = false, float intensity = 0.3f)
            => new GameContext { Rng = new Rng(seed), Actors = actors, Intensity = intensity, ForceSingleSurvivor = forceSingle, IsFinale = forceSingle };

        private static List<Actor> Humans(int n) => Enumerable.Range(0, n).Select(i => new Actor { Id = "h" + i }).ToList();
        private static List<Actor> Bots(int n) => Enumerable.Range(0, n).Select(i => new Actor { Id = "b" + i, IsBot = true }).ToList();

        // ── Glass Bridge ─────────────────────────────────────────────────
        [Fact]
        public void GlassBridge_correct_guess_advances_the_frontier()
        {
            var actors = Humans(3);
            var g = new GlassBridge(Ctx(actors, 1));
            g.Start();
            Assert.Equal(0, g.Frontier);
            string active = g.ActiveId;
            string safe = g.SafeSide(0) == 1 ? "R" : "L";
            g.OnInput(active, GameInput.Choose(safe));
            Assert.Equal(1, g.Frontier);
            Assert.True(actors.First(a => a.Id == active).Alive);
        }

        [Fact]
        public void GlassBridge_human_turn_waits_for_input_and_is_not_auto_played()
        {
            // Regression: a HUMAN's turn used to be auto-resolved (a random guess "the computer
            // went for me"). A human must hold the turn open — only ticking past the long
            // last-resort timeout (or actually inputting) resolves it.
            var actors = Humans(1);
            var g = new GlassBridge(Ctx(actors, 1));
            g.Start();
            Assert.Equal("choose", g.CurrentPhase);
            Assert.Equal("h0", g.ActiveId);
            for (int i = 0; i < 60; i++) g.Tick(Constants.Dt); // ~3s, well under the 9s timeout
            Assert.Equal("choose", g.CurrentPhase);            // still the human's turn — NOT auto-played
            Assert.Equal(0, g.Frontier);
            string safe = g.SafeSide(0) == 1 ? "R" : "L";
            g.OnInput("h0", GameInput.Choose(safe));           // the human finally picks
            Assert.Equal("resolve", g.CurrentPhase);
            Assert.Equal(1, g.Frontier);
        }

        [Fact]
        public void GlassBridge_wrong_guess_shatters_the_glass()
        {
            var actors = Humans(3);
            var g = new GlassBridge(Ctx(actors, 1));
            g.Start();
            string active = g.ActiveId;
            string wrong = g.SafeSide(0) == 1 ? "L" : "R";
            g.OnInput(active, GameInput.Choose(wrong));
            Assert.False(actors.First(a => a.Id == active).Alive);
        }

        [Fact]
        public void GlassBridge_full_bot_game_completes()
        {
            var actors = Bots(6);
            var g = new GlassBridge(Ctx(actors, 4));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 120 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(6, g.Result().Ranking.Count);
            Assert.NotEmpty(g.Result().SurvivorIds);
        }

        // ── Jump Rope ────────────────────────────────────────────────────
        [Fact]
        public void JumpRope_a_jumper_who_never_jumps_is_swept_off()
        {
            var actors = new List<Actor> { new Actor { Id = "h0" } };
            actors.AddRange(Bots(5));
            var g = new JumpRope(Ctx(actors, 2, intensity: 0.9f));
            g.Start();
            for (int i = 0; i < 120 && actors[0].Alive; i++) g.Tick(Constants.Dt);
            Assert.False(actors[0].Alive);
        }

        [Fact]
        public void JumpRope_plays_out_a_full_crossing_not_ending_after_one_fall()
        {
            // Regression: an early-round survivor target of ~11/12 used to END the round the instant
            // ONE player was swept off — nobody crossed, "the game ended itself". Now it runs a real
            // race: the rope swings many times and jumpers actually reach the far side.
            var actors = Bots(12);
            var g = new JumpRope(Ctx(actors, 3, intensity: 0.3f));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 60 * 40) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.True(g.Swing >= 5, $"round ended after only {g.Swing} swing(s) — it bailed too early");
            Assert.Contains(actors, a => g.Crossed(a.Id)); // somebody made it across
        }

        [Fact]
        public void JumpRope_finale_crowns_a_single_survivor()
        {
            var actors = Bots(4);
            var g = new JumpRope(Ctx(actors, 5, forceSingle: true));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 60 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Single(g.Result().SurvivorIds);
        }

        // ── RPS Minus One ────────────────────────────────────────────────
        [Fact]
        public void Rps_kept_rock_beats_kept_scissors()
        {
            var actors = Humans(2);
            var g = new RpsMinusOne(Ctx(actors, 1));
            g.Start();
            g.OnInput("h0", GameInput.Choose("RP")); // h0 will keep R
            g.OnInput("h1", GameInput.Choose("SP")); // h1 will keep S
            g.Tick(Constants.Dt);                    // pick → drop
            Assert.Equal("drop", g.CurrentPhase);
            g.OnInput("h0", GameInput.Choose("R"));
            g.OnInput("h1", GameInput.Choose("S"));
            g.Tick(Constants.Dt);                    // drop → resolve (settles)
            Assert.False(actors[1].Alive);           // scissors loses to rock
            Assert.True(actors[0].Alive);
        }

        [Fact]
        public void Rps_finale_resolves_to_one_champion()
        {
            var actors = Bots(4);
            var g = new RpsMinusOne(Ctx(actors, 8, forceSingle: true));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 60 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Single(g.Result().SurvivorIds);
        }

        [Fact]
        public void Rps_normal_round_eliminates_half_the_field()
        {
            var actors = Bots(6);
            var g = new RpsMinusOne(Ctx(actors, 3));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 60 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(6, g.Result().Ranking.Count);
            Assert.Equal(3, g.Result().SurvivorIds.Count); // one winner per duel
        }
    }
}
