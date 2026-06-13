using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Games
{
    public class MingleTests
    {
        private static (Mingle game, List<Actor> actors) Make(int humans, int bots, int seed = 1, float intensity = 0.3f)
        {
            var actors = new List<Actor>();
            for (int i = 0; i < humans; i++) actors.Add(new Actor { Id = "h" + i });
            for (int i = 0; i < bots; i++) actors.Add(new Actor { Id = "b" + i, IsBot = true });
            var ctx = new GameContext { Rng = new Rng(seed), Actors = actors, Intensity = intensity };
            var g = new Mingle(ctx);
            g.Start();
            return (g, actors);
        }

        [Fact]
        public void Everyone_starts_on_the_platform()
        {
            var (_, actors) = Make(0, 6);
            var center = new Vec2(Mingle.PlatformX, Mingle.PlatformY);
            Assert.All(actors, a => Assert.True(Vec2.Distance(a.Pos, center) <= Mingle.PlatformR));
        }

        [Fact]
        public void There_are_four_corner_rooms_set_well_off_the_platform()
        {
            var (g, _) = Make(0, 6);
            Assert.Equal(4, g.Rooms.Count);
            var center = new Vec2(Mingle.PlatformX, Mingle.PlatformY);
            foreach (var r in g.Rooms)
                Assert.True(Vec2.Distance(new Vec2(r.X, r.Y), center) > Mingle.PlatformR + 200f,
                    "each room should be a real sprint from the platform");
        }

        [Fact]
        public void During_the_music_riders_orbit_the_platform_instead_of_darting_around()
        {
            var (g, actors) = Make(0, 6, seed: 5);
            var center = new Vec2(Mingle.PlatformX, Mingle.PlatformY);
            var a = actors[0];
            float startAng = (float)System.Math.Atan2(a.Pos.Y - center.Y, a.Pos.X - center.X);
            float startRad = Vec2.Distance(a.Pos, center);
            for (int i = 0; i < 20 && g.CurrentPhase == Mingle.MinglePhase.Wander; i++) g.Tick(Constants.Dt);
            Assert.True(Vec2.Distance(a.Pos, center) <= Mingle.PlatformR);                 // still on the platform
            Assert.True(System.Math.Abs(Vec2.Distance(a.Pos, center) - startRad) < 12f);   // same radius (riding, not wandering)
            float endAng = (float)System.Math.Atan2(a.Pos.Y - center.Y, a.Pos.X - center.X);
            Assert.True(System.Math.Abs(endAng - startAng) > 0.2f);                        // but the angle advanced with the spin
        }

        [Fact]
        public void Players_are_confined_to_the_platform_during_the_music()
        {
            var (g, actors) = Make(1, 5, seed: 2);
            var human = actors[0];
            var center = new Vec2(Mingle.PlatformX, Mingle.PlatformY);
            // try to walk off the platform during wander
            for (int i = 0; i < 30 && g.CurrentPhase == Mingle.MinglePhase.Wander; i++)
            {
                g.OnInput(human.Id, GameInput.Move(1, 0));
                g.Tick(Constants.Dt);
            }
            if (g.CurrentPhase == Mingle.MinglePhase.Wander)
                Assert.True(Vec2.Distance(human.Pos, center) <= Mingle.PlatformR + 0.5f);
        }

        [Fact]
        public void Correct_group_survives_and_a_lonely_player_is_eliminated()
        {
            var (g, actors) = Make(6, 0, seed: 3); // humans → only move where we place them
            // advance to the mingle call
            for (int i = 0; i < 200 && g.CurrentPhase != Mingle.MinglePhase.Mingle; i++) g.Tick(Constants.Dt);
            Assert.Equal(Mingle.MinglePhase.Mingle, g.CurrentPhase);

            int n = g.CallN;
            var room0 = g.Rooms[0];
            var room1 = g.Rooms[1];
            // exactly N players form the correct group in room 0
            for (int i = 0; i < n; i++) actors[i].Pos = new Vec2(room0.X, room0.Y);
            // one lonely player in room 1 (too few)
            actors[n].Pos = new Vec2(room1.X, room1.Y);
            // any remainder stay on the platform (also doomed)
            for (int i = n + 1; i < actors.Count; i++) actors[i].Pos = new Vec2(Mingle.PlatformX, Mingle.PlatformY);

            for (int i = 0; i < 200 && g.CurrentPhase == Mingle.MinglePhase.Mingle; i++) g.Tick(Constants.Dt);

            for (int i = 0; i < n; i++) Assert.True(actors[i].Alive, $"group member {i} should survive");
            Assert.False(actors[n].Alive); // the lonely player is out
        }

        [Fact]
        public void Full_bot_game_completes_and_ranks_everyone()
        {
            var (g, _) = Make(0, 8, seed: 9, intensity: 0.8f);
            int ticks = 0;
            while (!g.IsDone && ticks < 80 * 20) { g.Tick(Constants.Dt); ticks++; }
            Assert.True(g.IsDone);
            Assert.Equal(8, g.Result().Ranking.Count);
        }
    }
}
