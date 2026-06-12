using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Xunit;

namespace Eliminated.Sim.Tests.Model
{
    public class ModelTests
    {
        [Fact]
        public void RoomConfig_defaults_match_reference()
        {
            var c = new RoomConfig();
            Assert.Equal(SeriesMode.Casual, c.Mode);
            Assert.True(c.Rounds.Mystery);
            Assert.Empty(c.AllowedGames);
            Assert.True(c.BotFill);
            Assert.Equal(12, c.MaxPlayers);
            Assert.True(c.FriendlyFire);
            Assert.False(c.NightMode);
        }

        [Fact]
        public void RoomConfig_clone_is_independent()
        {
            var c = new RoomConfig();
            var d = c.Clone();
            d.AllowedGames.Add(GameId.Boomerang);
            d.NightMode = true;
            Assert.Empty(c.AllowedGames);     // original untouched
            Assert.False(c.NightMode);
        }

        [Fact]
        public void RoundsMode_fixed_clamps_to_at_least_one()
        {
            Assert.True(RoundsMode.AsMystery().Mystery);
            Assert.Equal(5, RoundsMode.Fixed(5).Count);
            Assert.Equal(1, RoundsMode.Fixed(0).Count);
            Assert.False(RoundsMode.Fixed(3).Mystery);
        }

        [Theory]
        [InlineData(1f, Constants.PlayerRadius)]
        [InlineData(0.62f, Constants.PlayerRadius * 0.62f)]
        [InlineData(1.5f, Constants.PlayerRadius * 1.5f)]
        public void Actor_radius_scales_with_size(float scale, float expected)
        {
            var a = new Actor { Scale = scale };
            Assert.Equal(expected, a.Radius, 3);
        }

        [Fact]
        public void Actor_data_bag_is_lazy_and_usable()
        {
            var a = new Actor();
            Assert.Equal(7f, a.Get("missing", 7f));
            a.Set("phase", 2f);
            Assert.Equal(2f, a.Get("phase"));
        }

        [Fact]
        public void GameInput_factories_set_kind_and_payload()
        {
            var m = GameInput.Move(0.5f, -1f, seq: 3);
            Assert.Equal(InputKind.Move, m.Kind);
            Assert.Equal(0.5f, m.Dx);
            Assert.Equal(3, m.Seq);

            var act = GameInput.Action("throw");
            Assert.Equal(InputKind.Action, act.Kind);
            Assert.Equal("throw", act.Name);
            Assert.True(act.On);

            Assert.Equal(InputKind.Aim, GameInput.Aim(1.2f).Kind);
            Assert.Equal("left", GameInput.Choose("left").Value);
            Assert.Equal(InputKind.Tap, GameInput.Tap().Kind);
        }

        [Fact]
        public void Player_bot_ctor_marks_ready_and_disconnected()
        {
            var bot = new Player("bot_1", "Wiggly Beans", "avocado", isBot: true);
            Assert.True(bot.IsBot);
            Assert.True(bot.Ready);
            Assert.False(bot.Connected);
            Assert.True(bot.AliveInSeries);
        }
    }
}
