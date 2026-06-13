using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Eliminated.Sim.Powerups;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    /// <summary>
    /// The Boomerang-Fu-flavoured powerup additions: blessings that stay with you,
    /// curses that wear off, and the new Caffeine / Slippery / Jumble / Disguise
    /// behaviours. Reuses <see cref="ProbeArena"/> (in ArenaGameTests) for the
    /// movement/dash machinery.
    /// </summary>
    public class PowerupExtrasTests
    {
        private static (ProbeArena game, Actor a) OneActor(float x = 200, float y = 200)
        {
            var actor = new Actor { Id = "a", Pos = new Vec2(x, y) };
            var ctx = new GameContext { Rng = new Rng(1), Actors = new List<Actor> { actor } };
            var g = new ProbeArena(ctx);
            g.Start();
            return (g, actor);
        }

        // ── Lifecycle: blessings persist, curses expire ──────────────────

        [Fact]
        public void Blessings_are_held_and_do_not_expire_within_a_round()
        {
            var (g, a) = OneActor();
            PowerupEffects.Apply(a, PowerupKind.Speed);
            Assert.True(PowerupEffects.IsHeldTimer(a.PuSpeedT));
            g.Advance(90f); // a long round
            Assert.True(a.PuSpeedT > 0f);                       // still zooming
            Assert.True(PowerupEffects.IsHeldTimer(a.PuSpeedT)); // and still reads as "held"
        }

        [Fact]
        public void Curses_tick_down_and_wear_off()
        {
            var (g, a) = OneActor();
            PowerupEffects.Apply(a, PowerupKind.Slippery);
            Assert.True(a.PuSlipperyT > 0f);
            Assert.False(PowerupEffects.IsHeldTimer(a.PuSlipperyT)); // a draining curse, not held
            g.Advance(PowerupEffects.SlipperyDuration + 0.5f);
            Assert.Equal(0f, a.PuSlipperyT, 3);
        }

        [Fact]
        public void New_powerups_are_classified_good_or_bad()
        {
            Assert.True(PowerupEffects.IsGood(PowerupKind.Caffeine));
            Assert.True(PowerupEffects.IsGood(PowerupKind.Disguise));
            Assert.False(PowerupEffects.IsGood(PowerupKind.Slippery));
            Assert.False(PowerupEffects.IsGood(PowerupKind.Jumble));
        }

        // ── ☕ Caffeine: dash with no cooldown ───────────────────────────

        [Fact]
        public void Caffeine_lets_you_dash_with_no_cooldown()
        {
            var (g, a) = OneActor();
            a.InDx = 1f;
            Assert.True(g.DoTryDash(a));   // dash #1
            g.Advance(0.2f);               // burst ends; normal 1.4s cooldown still running
            Assert.False(g.DoTryDash(a));  // blocked by cooldown
            PowerupEffects.Apply(a, PowerupKind.Caffeine);
            Assert.True(g.DoTryDash(a));   // caffeine ignores the cooldown
            Assert.Equal(0f, a.DashCdT, 3); // and never arms one
        }

        // ── 🍌 Slippery: ice-skate momentum ─────────────────────────────

        [Fact]
        public void Slippery_eases_into_motion_instead_of_snapping()
        {
            var (g, a) = OneActor();
            PowerupEffects.Apply(a, PowerupKind.Slippery);
            a.InDx = 1f; a.Vel = Vec2.Zero;
            var p0 = a.Pos;
            g.DoMove(a, Constants.Dt);
            float moved = Vec2.Distance(p0, a.Pos);
            Assert.True(moved > 0f && moved < 12f); // sliding up to speed, not the instant 12 units
        }

        [Fact]
        public void Slippery_keeps_drifting_after_you_let_go()
        {
            var (g, a) = OneActor();
            PowerupEffects.Apply(a, PowerupKind.Slippery);
            a.InDx = 1f;
            for (int i = 0; i < 20; i++) g.DoMove(a, Constants.Dt); // build up speed
            a.InDx = 0f;
            var p = a.Pos;
            g.DoMove(a, Constants.Dt); // no input — but momentum carries you on
            Assert.True(a.Pos.X > p.X + 0.1f);
        }

        [Fact]
        public void Normal_movement_is_unchanged_without_slippery()
        {
            var (g, a) = OneActor();
            a.InDx = 1f; var p0 = a.Pos;
            g.DoMove(a, Constants.Dt);
            Assert.Equal(12f, Vec2.Distance(p0, a.Pos), 2); // instant 240*0.05, exactly as before
        }

        // ── 🔀 Jumble: random warp ──────────────────────────────────────

        [Fact]
        public void Jumble_warps_the_collector_somewhere_else()
        {
            var field = new PowerupField(new Rng(3));
            var a = new Actor { Id = "a", Pos = new Vec2(100, 100) };
            field.AddPickup(PowerupKind.Jumble, 100f, 100f); // sitting on the actor
            var before = a.Pos;
            var got = field.Collect(a, new List<Actor> { a });
            Assert.Equal(PowerupKind.Jumble, got);
            Assert.True(Vec2.Distance(before, a.Pos) > 1f); // teleported
        }

        // ── 🥸 Disguise: borrow another player's identity ───────────────

        [Fact]
        public void Disguise_borrows_another_living_players_identity()
        {
            var field = new PowerupField(new Rng(5));
            var me = new Actor { Id = "me", CharacterId = "cat", Number = 1, Pos = new Vec2(100, 100) };
            var other = new Actor { Id = "o", CharacterId = "dog", Number = 42, Pos = new Vec2(500, 500) };
            field.AddPickup(PowerupKind.Disguise, 100f, 100f);
            var got = field.Collect(me, new List<Actor> { me, other });
            Assert.Equal(PowerupKind.Disguise, got);
            Assert.Equal("dog", me.DisguiseCharId);
            Assert.Equal(42, me.DisguiseNumber);
            Assert.True(me.PuDisguiseT > 0f);
        }

        [Fact]
        public void Disguise_fizzles_when_there_is_no_one_to_mimic()
        {
            var field = new PowerupField(new Rng(5));
            var me = new Actor { Id = "me", CharacterId = "cat", Number = 1, Pos = new Vec2(100, 100) };
            field.AddPickup(PowerupKind.Disguise, 100f, 100f);
            field.Collect(me, new List<Actor> { me }); // nobody else
            Assert.Null(me.DisguiseCharId);
            Assert.Equal(0f, me.PuDisguiseT, 3);
        }

        // ── Catalog: every powerup has a reveal ─────────────────────────

        [Fact]
        public void Catalog_describes_every_shared_powerup()
        {
            foreach (PowerupKind k in Enum.GetValues(typeof(PowerupKind)))
                Assert.True(PowerupCatalog.TryGet(k.ToString(), out _), $"missing catalog entry for {k}");
        }

        [Fact]
        public void Catalog_describes_every_boomerang_power()
        {
            foreach (Boomerang.BoomPower p in Enum.GetValues(typeof(Boomerang.BoomPower)))
                Assert.True(PowerupCatalog.TryGet(p.ToString(), out _), $"missing catalog entry for boom {p}");
        }

        [Fact]
        public void Reveal_text_pairs_icon_with_name()
        {
            Assert.Equal("🌀 Bamboozled!", PowerupCatalog.RevealText("Reverse"));
            Assert.Equal("🥸 Disguise!", PowerupCatalog.RevealText("Disguise"));
        }
    }
}
