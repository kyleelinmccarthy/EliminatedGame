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
    /// Concrete <see cref="ArenaGame"/> used to exercise the shared movement,
    /// dash, powerup, and elimination machinery in isolation.
    /// </summary>
    internal sealed class ProbeArena : ArenaGame
    {
        public ProbeArena(GameContext ctx) : base(ctx) { }
        public override GameId Id => GameId.Boomerang;
        public override bool IsDone => AliveActors.Count() <= 1;
        protected override float DashIFrames => 0.26f;

        public override void Start() { Elapsed = 0f; }

        // Expose protected building blocks for testing.
        public void DoMove(Actor a, float dt) => MoveActor(a, dt);
        public void DoStatus(Actor a, float dt) => UpdateStatus(a, dt);
        public bool DoTryDash(Actor a) => TryDash(a);
        public void DoEliminate(Actor a, string note = null) => Eliminate(a, note);
        public void DoFinish(Actor a) => MarkFinished(a);
        public RoundResult DoResult() => Result();
        public void Advance(float seconds)
        {
            // simulate ticks of status updates (cooldown/timer decay) only
            int steps = (int)System.MathF.Round(seconds / Constants.Dt);
            for (int i = 0; i < steps; i++)
                foreach (var a in Actors) UpdateStatus(a, Constants.Dt);
        }
    }

    public class ArenaGameTests
    {
        private static (ProbeArena game, Actor a) OneActor(float x = 200, float y = 200)
        {
            var actor = new Actor { Id = "a", Pos = new Vec2(x, y) };
            var ctx = new GameContext { Rng = new Rng(1), Actors = new List<Actor> { actor } };
            var g = new ProbeArena(ctx);
            g.Start();
            return (g, actor);
        }

        [Fact]
        public void MoveActor_moves_in_input_direction_at_base_speed()
        {
            var (g, a) = OneActor();
            a.InDx = 1; a.InDy = 0;
            g.DoMove(a, Constants.Dt); // 240 * 0.05 = 12 units
            Assert.Equal(212f, a.Pos.X, 2);
            Assert.Equal(200f, a.Pos.Y, 2);
            Assert.Equal(AnimState.Run, a.Anim);
        }

        [Fact]
        public void MoveActor_normalizes_diagonal_so_speed_is_capped()
        {
            var (g, a) = OneActor();
            a.InDx = 1; a.InDy = 1; // magnitude sqrt2 → must be normalized
            var before = a.Pos;
            g.DoMove(a, Constants.Dt);
            float displaced = Vec2.Distance(before, a.Pos);
            Assert.Equal(12f, displaced, 2); // not 12*sqrt2
        }

        [Fact]
        public void MoveActor_clamps_to_arena_bounds()
        {
            var (g, a) = OneActor(x: Constants.ArenaW - 5, y: 360);
            a.InDx = 1; a.InDy = 0;
            for (int i = 0; i < 20; i++) g.DoMove(a, Constants.Dt);
            Assert.Equal(Constants.ArenaW - a.Radius, a.Pos.X, 2);
        }

        [Fact]
        public void Reverse_powerup_inverts_input()
        {
            var (g, a) = OneActor();
            PowerupEffects.Apply(a, PowerupKind.Reverse);
            a.InDx = 1; a.InDy = 0;
            g.DoMove(a, Constants.Dt);
            Assert.Equal(188f, a.Pos.X, 2); // moved −12, not +12
        }

        [Fact]
        public void Slow_and_speed_powerups_scale_velocity()
        {
            var (gs, slow) = OneActor();
            PowerupEffects.Apply(slow, PowerupKind.Slow);
            slow.InDx = 1; var b0 = slow.Pos;
            gs.DoMove(slow, Constants.Dt);
            Assert.Equal(6f, Vec2.Distance(b0, slow.Pos), 2); // 240*0.5*0.05

            var (gz, fast) = OneActor();
            PowerupEffects.Apply(fast, PowerupKind.Speed);
            fast.InDx = 1; var f0 = fast.Pos;
            gz.DoMove(fast, Constants.Dt);
            Assert.Equal(12f * 1.6f, Vec2.Distance(f0, fast.Pos), 2); // 19.2
        }

        [Fact]
        public void Tiny_shrinks_radius_and_adds_nimbleness()
        {
            var (g, a) = OneActor();
            PowerupEffects.Apply(a, PowerupKind.Tiny);
            g.DoStatus(a, Constants.Dt); // applies scale
            Assert.Equal(0.62f, a.Scale, 3);
            Assert.Equal(Constants.PlayerRadius * 0.62f, a.Radius, 3);
            a.InDx = 1; var p0 = a.Pos;
            g.DoMove(a, Constants.Dt);
            Assert.Equal(12f * 1.15f, Vec2.Distance(p0, a.Pos), 2); // nimble
        }

        [Fact]
        public void Giant_grows_radius_and_slows()
        {
            var (g, a) = OneActor();
            PowerupEffects.Apply(a, PowerupKind.Giant);
            g.DoStatus(a, Constants.Dt);
            Assert.Equal(1.5f, a.Scale, 3);
            a.InDx = 1; var p0 = a.Pos;
            g.DoMove(a, Constants.Dt);
            Assert.Equal(12f * 0.62f, Vec2.Distance(p0, a.Pos), 2);
        }

        [Fact]
        public void Powerup_timers_expire_and_scale_reverts()
        {
            var (g, a) = OneActor();
            a.PuTinyT = Constants.Dt; // one tick left
            g.DoStatus(a, Constants.Dt);
            Assert.Equal(0f, a.PuTinyT, 4);
            g.DoStatus(a, Constants.Dt); // next tick, no longer tiny
            Assert.Equal(1f, a.Scale, 3);
        }

        [Fact]
        public void Dash_bursts_faster_then_goes_on_cooldown()
        {
            var (g, a) = OneActor();
            a.InDx = 1;
            Assert.True(g.DoTryDash(a));
            Assert.True(a.IFrameT > 0f); // i-frames granted
            var p0 = a.Pos;
            g.DoMove(a, Constants.Dt); // dashing: 240*3.1*0.05 = 37.2
            Assert.Equal(37.2f, Vec2.Distance(p0, a.Pos), 1);

            // immediately trying again fails (on cooldown)
            Assert.False(g.DoTryDash(a));
        }

        [Fact]
        public void Dash_cooldown_clears_after_its_duration()
        {
            var (g, a) = OneActor();
            a.InDx = 1;
            Assert.True(g.DoTryDash(a));
            g.Advance(1.5f); // > 1.4s cooldown
            Assert.True(g.DoTryDash(a));
        }

        [Fact]
        public void Collision_overlap_uses_scaled_radii()
        {
            var a = new Actor { Pos = new Vec2(0, 0) };          // r=26
            var b = new Actor { Pos = new Vec2(40, 0) };          // r=26 → overlap (sum 52)
            Assert.True(Collision.Overlap(a, b));
            b.Pos = new Vec2(60, 0);                              // gap → no overlap
            Assert.False(Collision.Overlap(a, b));
        }

        [Fact]
        public void Result_ranks_survivors_then_eliminated_in_reverse_order()
        {
            var actors = new List<Actor>
            {
                new Actor { Id = "a" }, new Actor { Id = "b" }, new Actor { Id = "c" }
            };
            var ctx = new GameContext { Rng = new Rng(1), Actors = actors };
            var g = new ProbeArena(ctx);
            g.Start();

            g.DoEliminate(actors[0], "out first"); // a out (earliest)
            g.DoEliminate(actors[1], "out second"); // b out (later)
            var r = g.DoResult();

            Assert.Equal(new[] { "c" }, r.SurvivorIds.ToArray());
            Assert.Equal("c", r.Ranking[0].PlayerId); // survivor, placement 1
            Assert.True(r.Ranking[0].Survived);
            Assert.Equal("b", r.Ranking[1].PlayerId); // last eliminated ranks higher
            Assert.Equal("a", r.Ranking[2].PlayerId);
            Assert.Equal(1, r.Ranking[0].Placement);
            Assert.Equal(2, r.Ranking[1].Placement);
            Assert.Equal(3, r.Ranking[2].Placement);
        }
    }
}
