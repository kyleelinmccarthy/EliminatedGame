using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class ExtraGamesTests
    {
        private static GameContext Ctx(List<Actor> actors, int seed, bool forceSingle = false, float intensity = 0.3f)
            => new GameContext { Rng = new Rng(seed), Actors = actors, Intensity = intensity, ForceSingleSurvivor = forceSingle, IsFinale = forceSingle };
        private static List<Actor> Humans(int n) => Enumerable.Range(0, n).Select(i => new Actor { Id = "h" + i }).ToList();
        private static List<Actor> Bots(int n) => Enumerable.Range(0, n).Select(i => new Actor { Id = "b" + i, IsBot = true }).ToList();

        // ── Simon Says ───────────────────────────────────────────────────
        [Fact]
        public void Simon_obeying_the_order_keeps_you_in()
        {
            var actors = Humans(4);
            var g = new SimonSays(Ctx(actors, 1));
            g.Start();
            for (int i = 0; i < 200 && g.Phase != "call"; i++) g.Tick(Constants.Dt);
            Assert.Equal("call", g.Phase);
            Assert.False(g.IsFreeze); // beat 1 is never a freeze
            g.OnInput("h0", GameInput.Choose(g.CommandKey)); // obey
            for (int i = 0; i < 120 && !g.IsDone && g.Phase != "ready"; i++) g.Tick(Constants.Dt);
            Assert.True(actors[0].Alive);  // obeyed → safe
            Assert.False(actors[1].Alive); // did nothing → out
        }

        [Fact]
        public void Simon_full_bot_game_completes()
        {
            var actors = Bots(8);
            var g = new SimonSays(Ctx(actors, 3, intensity: 0.8f));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 60 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(8, g.Result().Ranking.Count);
        }

        // ── Chutes & Ladders ─────────────────────────────────────────────
        [Fact]
        public void Chutes_full_bot_game_completes()
        {
            var actors = Bots(6);
            var g = new ChutesAndLadders(Ctx(actors, 4));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 45 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(6, g.Result().Ranking.Count);
        }

        [Fact]
        public void Chutes_culls_a_meaningful_fraction_not_everyone_wins()
        {
            // Regression for "too easy — everyone wins". Average eliminated across seeds
            // must be clearly above zero at high intensity (the tightened clock + cull).
            int totalElim = 0, runs = 12;
            for (int seed = 0; seed < runs; seed++)
            {
                var actors = Bots(12);
                var g = new ChutesAndLadders(Ctx(actors, seed, intensity: 0.9f));
                g.Start();
                int ticks = 0;
                while (!g.IsDone && ticks < 60 * 20) { g.Tick(Constants.Dt); ticks++; }
                totalElim += actors.Count(a => !a.Alive);
            }
            Assert.True(totalElim / (float)runs >= 3f, $"avg eliminated {totalElim / (float)runs:0.0} — cull too weak");
        }

        [Fact]
        public void Chutes_finale_crowns_a_single_survivor()
        {
            var actors = Bots(8);
            var g = new ChutesAndLadders(Ctx(actors, 7, forceSingle: true));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 60 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Single(g.Result().SurvivorIds);
        }

        // ── Keepy Uppy ───────────────────────────────────────────────────
        [Fact]
        public void Keepy_balloon_hitting_the_floor_eliminates_its_owner()
        {
            var actors = Humans(2);
            var g = new KeepyUppy(Ctx(actors, 1));
            g.Start();
            g.DebugSetBalloon("h0", 640f, Constants.ArenaH); // slam it to the floor
            g.Tick(Constants.Dt);
            Assert.False(actors[0].Alive);
        }

        [Fact]
        public void Keepy_spike_pops_a_rivals_balloon()
        {
            var actors = Humans(2);
            var g = new KeepyUppy(Ctx(actors, 1));
            g.Start();
            var h0 = actors[0];
            g.DebugSetBalloon("h1", h0.Pos.X, h0.Pos.Y); // rival balloon right on me
            g.OnInput("h0", GameInput.Tap());            // SPIKE
            g.Tick(Constants.Dt);
            Assert.False(actors[1].Alive);
        }

        [Fact]
        public void Keepy_full_bot_game_completes()
        {
            var actors = Bots(6);
            var g = new KeepyUppy(Ctx(actors, 5));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 40 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(6, g.Result().Ranking.Count);
        }

        // ── Prop Hunt ────────────────────────────────────────────────────
        [Fact]
        public void Prop_seeker_swing_finds_a_hider()
        {
            var actors = Humans(4);
            var g = new PropHunt(Ctx(actors, 2));
            g.Start();
            for (int i = 0; i < 8 * 20 + 5 && g.Phase != "hunt"; i++) g.Tick(Constants.Dt);
            Assert.Equal("hunt", g.Phase);

            var seeker = actors.First(a => a.Id == g.SeekerId);
            var hider = actors.First(a => a.Id != g.SeekerId && a.Alive);
            seeker.Pos = hider.Pos; // stand right on a disguised hider
            g.OnInput(seeker.Id, GameInput.Action("swing"));
            Assert.True(g.Found >= 1);
            Assert.False(hider.Alive);
        }

        [Fact]
        public void Prop_full_bot_game_completes()
        {
            var actors = Bots(6);
            var g = new PropHunt(Ctx(actors, 7));
            g.Start();
            int ticks = 0;
            while (!g.IsDone && ticks < 60 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(6, g.Result().Ranking.Count);
        }
    }
}
