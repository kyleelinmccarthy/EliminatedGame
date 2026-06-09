using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class BoomerangTests
    {
        private static GameContext Ctx(List<Actor> actors, int seed = 1,
            bool forceSingle = false, float intensity = 0.3f)
            => new GameContext
            {
                Rng = new Rng(seed),
                Actors = actors,
                Intensity = intensity,
                ForceSingleSurvivor = forceSingle,
                IsFinale = forceSingle
            };

        private static Actor Human(string id) => new Actor { Id = id, Name = id, IsBot = false };
        private static Actor Bot(string id) => new Actor { Id = id, Name = id, IsBot = true };

        [Fact]
        public void Throw_spawns_a_rang_capped_by_max_rangs()
        {
            var actors = new List<Actor> { Human("h0"), Human("h1") };
            var g = new Boomerang(Ctx(actors));
            g.Start();
            actors[0].Pos = new Vec2(200, 360);
            actors[1].Pos = new Vec2(900, 360);

            g.OnInput("h0", GameInput.Aim(0f));
            g.OnInput("h0", GameInput.Action("throw"));
            g.Tick(Constants.Dt);
            Assert.Equal(1, g.RangCount);

            // a second throw while one is already in flight is blocked (maxRangs = 1)
            g.OnInput("h0", GameInput.Action("throw"));
            g.Tick(Constants.Dt);
            Assert.Equal(1, g.RangCount);
        }

        [Fact]
        public void A_thrown_rang_kills_a_defenceless_enemy()
        {
            var thrower = Human("h0");
            var victim = Human("h1");
            var g = new Boomerang(Ctx(new List<Actor> { thrower, victim }));
            g.Start();
            thrower.Pos = new Vec2(200, 360);
            victim.Pos = new Vec2(260, 360);

            g.OnInput("h0", GameInput.Aim(0f)); // aim east, straight at the victim
            g.OnInput("h0", GameInput.Action("throw"));
            for (int i = 0; i < 6 && victim.Alive; i++) g.Tick(Constants.Dt);

            Assert.False(victim.Alive);
            Assert.Equal(1, g.KillsOf("h0"));
        }

        [Fact]
        public void A_shield_absorbs_one_hit()
        {
            var thrower = Human("h0");
            var victim = Human("h1");
            var g = new Boomerang(Ctx(new List<Actor> { thrower, victim }));
            g.Start();
            thrower.Pos = new Vec2(200, 360);
            victim.Pos = new Vec2(260, 360);
            g.AddPickup(Boomerang.BoomPower.Shield, victim.Pos.X, victim.Pos.Y);
            g.Tick(Constants.Dt); // victim grabs the shield
            Assert.True(victim.Shield);

            g.OnInput("h0", GameInput.Aim(0f));
            g.OnInput("h0", GameInput.Action("throw"));
            for (int i = 0; i < 6; i++) g.Tick(Constants.Dt);

            Assert.True(victim.Alive);   // shield ate the hit
            Assert.False(victim.Shield); // and was consumed
        }

        [Fact]
        public void Dash_grants_brief_invulnerability()
        {
            var a = Human("h0");
            var g = new Boomerang(Ctx(new List<Actor> { a, Human("h1") }));
            g.Start();
            a.InDx = 1;
            g.OnInput("h0", GameInput.Action("dash"));
            g.Tick(Constants.Dt);
            Assert.True(g.InvulnOf("h0") > 0f);
            Assert.True(a.Ghost);
        }

        [Fact]
        public void Multishot_pickup_raises_the_rang_cap_to_three()
        {
            var a = Human("h0");
            var g = new Boomerang(Ctx(new List<Actor> { a, Human("h1") }));
            g.Start();
            a.Pos = new Vec2(400, 360);
            g.AddPickup(Boomerang.BoomPower.Multishot, a.Pos.X, a.Pos.Y);
            g.Tick(Constants.Dt);
            Assert.Equal(3, g.MaxRangsOf("h0"));
        }

        [Fact]
        public void Full_bot_brawl_completes_within_the_time_limit()
        {
            var actors = Enumerable.Range(0, 6).Select(i => Bot("b" + i)).ToList();
            var g = new Boomerang(Ctx(actors, seed: 5));
            int ticks = 0;
            while (!g.IsDone && ticks < 51 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            var r = g.Result();
            Assert.Equal(6, r.Ranking.Count);
        }

        [Fact]
        public void Finale_crowns_exactly_one_survivor()
        {
            var actors = Enumerable.Range(0, 6).Select(i => Bot("b" + i)).ToList();
            var g = new Boomerang(Ctx(actors, seed: 8, forceSingle: true));
            int ticks = 0;
            while (!g.IsDone && ticks < 51 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            var r = g.Result();
            Assert.Single(r.SurvivorIds);
            Assert.Equal(1, r.Ranking.Count(e => e.Survived));
        }

        [Fact]
        public void Target_scales_down_with_intensity()
        {
            var actors = Enumerable.Range(0, 8).Select(i => Bot("b" + i)).ToList();
            var low = new Boomerang(Ctx(actors.Select(a => Bot(a.Id)).ToList(), intensity: 0f));
            low.Start();
            var high = new Boomerang(Ctx(actors.Select(a => Bot(a.Id)).ToList(), intensity: 1f));
            high.Start();
            Assert.True(high.Target < low.Target); // harsher series → fewer survivors
        }
    }
}
