using Eliminated.Sim.Economy;
using Xunit;

namespace Eliminated.Sim.Tests.Economy
{
    public class MarblesTests
    {
        [Fact]
        public void Round_payout_rewards_survival_and_the_win()
        {
            Assert.Equal(50, Marbles.RoundPayout(survived: true, wonRound: false));
            Assert.Equal(90, Marbles.RoundPayout(survived: true, wonRound: true));
            Assert.Equal(5, Marbles.RoundPayout(survived: false, wonRound: false));
        }

        [Theory]
        [InlineData(1, 200)]
        [InlineData(2, 120)]
        [InlineData(3, 80)]
        [InlineData(4, 50)]
        [InlineData(5, 30)]
        [InlineData(6, 0)]
        [InlineData(8, 0)]
        public void Placement_bonus_follows_curve(int placement, int expected)
        {
            Assert.Equal(expected, Marbles.PlacementBonus(placement));
        }

        [Theory]
        [InlineData(1, "The Last Player Standing")]
        [InlineData(2, "First Loser")]
        [InlineData(3, "Bronze Is Just Shiny Last")]
        [InlineData(4, "Mid-Tier Menace")]
        [InlineData(5, "Almost Clutch")]
        [InlineData(9, "Comic Relief")]
        [InlineData(16, "Cannon Fodder")]
        [InlineData(20, "Cannon Fodder")] // beyond the list clamps to last
        public void Titles_by_placement(int placement, string expected)
        {
            Assert.Equal(expected, Marbles.PlacementTitle(placement));
        }

        [Fact]
        public void Champion_bonus_is_a_modest_top_up()
        {
            // Kept small on purpose: the champion already out-earns the field via
            // round wins + the top placement bonus, so the series win shouldn't
            // also drop a huge flat bonus on top.
            Assert.Equal(100, Marbles.ChampionBonus);
        }
    }
}
