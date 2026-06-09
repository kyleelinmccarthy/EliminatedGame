using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Eliminated.Sim.Room;
using Xunit;

namespace Eliminated.Sim.Tests.Net
{
    /// <summary>
    /// Verifies the online per-game Data contract: every game's
    /// <c>Snapshot.Data</c> serializes to JSON and deserializes back into the type
    /// <see cref="DataWire.TypeFor"/> declares — which is exactly what the online
    /// client does (the client uses JsonUtility; both serialize public fields by
    /// name with numeric enums, so the contract is the same). System.Text.Json is
    /// configured to match: include fields.
    /// </summary>
    public class DataWireTests
    {
        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { IncludeFields = true };

        public static IEnumerable<object[]> AllGames =>
            GameCatalog.Registered.Select(id => new object[] { id });

        [Theory]
        [MemberData(nameof(AllGames))]
        public void Every_games_snapshot_data_round_trips_as_json(GameId id)
        {
            var actors = Enumerable.Range(0, 8).Select(i => new Actor { Id = "b" + i, Name = "B" + i, IsBot = true }).ToList();
            var ctx = new GameContext { Rng = new Rng(2024), Actors = actors, Intensity = 0.5f };
            var g = GameCatalog.Create(id, ctx);
            g.Start();
            // tick a little so the data has interesting (non-default) contents
            for (int i = 0; i < 30 && !g.IsDone; i++) g.Tick(Constants.Dt);

            object data = g.BuildSnapshot().Data;
            Assert.NotNull(data); // every game publishes a per-tick data payload

            var type = DataWire.TypeFor(id);
            Assert.NotNull(type);
            Assert.Equal(type, data.GetType());

            string json = JsonSerializer.Serialize(data, type, Opts);
            Assert.False(string.IsNullOrEmpty(json));

            // The decode the online client performs (typed deserialize) must succeed.
            object back = JsonSerializer.Deserialize(json, type, Opts);
            Assert.NotNull(back);
            Assert.Equal(type, back.GetType());

            // and it must be stable (re-serialize equals)
            Assert.Equal(json, JsonSerializer.Serialize(back, type, Opts));
        }
    }
}
