using Eliminated.Sim.Core;
using Xunit;

namespace Eliminated.Sim.Tests.Core
{
    /// <summary>
    /// Smoke tests proving the headless build pipeline works end to end:
    /// the canonical sim source under the Unity package is compiled by the .NET
    /// SDK and exercised by xUnit. Also pins the core simulation constants.
    /// </summary>
    public class ConstantsTests
    {
        [Fact]
        public void Tick_rate_is_20hz_with_matching_dt()
        {
            Assert.Equal(20, Constants.TickHz);
            Assert.Equal(0.05f, Constants.Dt, 5);
            Assert.Equal(50f, Constants.TickMs, 5);
        }

        [Fact]
        public void Arena_is_1280x720()
        {
            Assert.Equal(1280f, Constants.ArenaW);
            Assert.Equal(720f, Constants.ArenaH);
        }

        [Fact]
        public void Room_limits_match_reference()
        {
            Assert.Equal(4, Constants.RoomCodeLen);
            Assert.Equal(8, Constants.MaxPlayers);
            Assert.Equal(2, Constants.MinToStart);
            Assert.Equal(6, Constants.BotFillTarget);
        }
    }
}
