using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Room;
using Xunit;

namespace Eliminated.Sim.Tests.Room
{
    public class RoomManagerTests
    {
        private static Player Human(string id) => new Player(id, id, "avo", isBot: false);

        [Fact]
        public void Created_rooms_get_unique_codes()
        {
            var m = new RoomManager(seed: 1);
            var codes = Enumerable.Range(0, 60).Select(_ => m.CreateRoom().Code).ToList();
            Assert.Equal(60, m.RoomCount);
            Assert.Equal(codes.Count, codes.Distinct().Count());
            Assert.All(codes, c => Assert.Equal(Constants.RoomCodeLen, c.Length));
        }

        [Fact]
        public void Rooms_are_found_by_code_case_insensitively()
        {
            var m = new RoomManager();
            var room = m.CreateRoom();
            Assert.Same(room, m.GetRoom(room.Code));
            Assert.Same(room, m.GetRoom(room.Code.ToLowerInvariant()));
            Assert.Null(m.GetRoom("ZZZZ"));
        }

        [Fact]
        public void Join_by_code_adds_a_player_or_fails_when_unknown()
        {
            var m = new RoomManager();
            var room = m.CreateRoom();
            Assert.True(m.JoinRoom(room.Code, Human("p1")));
            Assert.Single(room.Players);
            Assert.False(m.JoinRoom("NOPE", Human("p2")));
        }

        [Fact]
        public void Join_respects_max_players()
        {
            var m = new RoomManager();
            var room = m.CreateRoom(new RoomConfig { MaxPlayers = 3 });
            Assert.True(m.JoinRoom(room.Code, Human("a")));
            Assert.True(m.JoinRoom(room.Code, Human("b")));
            Assert.True(m.JoinRoom(room.Code, Human("c")));
            Assert.False(m.JoinRoom(room.Code, Human("d"))); // full
        }

        [Fact]
        public void Tick_advances_each_room()
        {
            var m = new RoomManager(seed: 5);
            var room = m.CreateRoom(new RoomConfig { Rounds = RoundsMode.Fixed(3) });
            m.JoinRoom(room.Code, Human("host"));
            room.StartSeries(); // bot-fills, → Intro
            Assert.Equal(RoomPhase.Intro, room.Phase);

            for (int i = 0; i < 7 * 20; i++) m.Tick(Constants.Dt); // > intro hold
            Assert.NotEqual(RoomPhase.Intro, room.Phase); // the manager ticked it forward
        }

        [Fact]
        public void Empty_rooms_are_reaped_after_the_grace_window()
        {
            var m = new RoomManager();
            var ghost = m.CreateRoom();              // nobody joins
            var live = m.CreateRoom();
            m.JoinRoom(live.Code, Human("stayer"));
            Assert.Equal(2, m.RoomCount);

            for (int i = 0; i < 31 * 20; i++) m.Tick(Constants.Dt); // > 30s grace

            Assert.False(m.HasRoom(ghost.Code)); // reaped
            Assert.True(m.HasRoom(live.Code));   // kept (has a human)
        }
    }
}
