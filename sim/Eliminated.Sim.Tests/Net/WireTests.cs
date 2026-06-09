using System.Collections.Generic;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Net;
using Xunit;

namespace Eliminated.Sim.Tests.Net
{
    public class WireTests
    {
        [Fact]
        public void Move_input_round_trips()
        {
            var dec = Wire.DecodeInput(Wire.EncodeInput(GameInput.Move(0.5f, -0.25f, seq: 7)));
            Assert.Equal(InputKind.Move, dec.Kind);
            Assert.Equal(0.5f, dec.Dx);
            Assert.Equal(-0.25f, dec.Dy);
            Assert.Equal(7, dec.Seq);
        }

        [Fact]
        public void Action_input_round_trips()
        {
            var dec = Wire.DecodeInput(Wire.EncodeInput(GameInput.Action("throw", on: true)));
            Assert.Equal(InputKind.Action, dec.Kind);
            Assert.Equal("throw", dec.Name);
            Assert.True(dec.On);
        }

        [Fact]
        public void Aim_choose_tap_inputs_round_trip()
        {
            var aim = Wire.DecodeInput(Wire.EncodeInput(GameInput.Aim(1.2345f)));
            Assert.Equal(1.2345f, aim.Angle, 4);

            var choose = Wire.DecodeInput(Wire.EncodeInput(GameInput.Choose("R")));
            Assert.Equal("R", choose.Value);

            var tap = Wire.DecodeInput(Wire.EncodeInput(GameInput.Tap()));
            Assert.Equal(InputKind.Tap, tap.Kind);
        }

        [Fact]
        public void Snapshot_frame_round_trips_actors_and_effects()
        {
            var actors = new List<Actor>
            {
                new Actor { Id = "local", Pos = new Vec2(123.5f, 456.25f), Facing = 0.7f, Scale = 0.62f,
                            Team = 1, Number = 67, Anim = AnimState.Run, Alive = true, It = true, Shield = true, Progress = 0.5f },
                new Actor { Id = "b1", Pos = new Vec2(10f, 20f), Alive = false, Anim = AnimState.Dead, Team = -1 }
            };
            var fx = new List<Effect> { new Effect(EffectKind.Death, 5f, 6f), new Effect(EffectKind.Pickup, 1f, 2f, 0f, "Speed") };

            var bytes = Wire.EncodeFrame(GameId.Boomerang, 1234.0, 250.0, actors, fx, "{\"alive\":2}");
            var f = Wire.DecodeFrame(bytes);

            Assert.Equal(GameId.Boomerang, f.Game);
            Assert.Equal(1234.0, f.T);
            Assert.True(f.HasStartAt);
            Assert.Equal(250.0, f.StartAt);
            Assert.Equal("{\"alive\":2}", f.DataJson);

            Assert.Equal(2, f.Actors.Count);
            var a0 = f.Actors[0];
            Assert.Equal("local", a0.Id);
            Assert.Equal(123.5f, a0.X);
            Assert.Equal(456.25f, a0.Y);
            Assert.Equal(0.62f, a0.Scale, 3);
            Assert.Equal((sbyte)1, a0.Team);
            Assert.Equal(67, a0.Number);
            Assert.True(a0.Has(NetActor.Alive));
            Assert.True(a0.Has(NetActor.It));
            Assert.True(a0.Has(NetActor.Shield));
            Assert.False(a0.Has(NetActor.Frozen));

            var a1 = f.Actors[1];
            Assert.False(a1.Has(NetActor.Alive));
            Assert.Equal((sbyte)-1, a1.Team);

            Assert.Equal(2, f.Fx.Count);
            Assert.Equal(EffectKind.Pickup, f.Fx[1].Kind);
            Assert.Equal("Speed", f.Fx[1].Tag);
        }

        [Fact]
        public void Frame_without_startAt_round_trips()
        {
            var f = Wire.DecodeFrame(Wire.EncodeFrame(GameId.TugOfWar, 50.0, null, new List<Actor>(), new List<Effect>(), null));
            Assert.False(f.HasStartAt);
            Assert.Null(f.DataJson);
            Assert.Empty(f.Actors);
        }
    }
}
