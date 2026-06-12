using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Economy;
using Xunit;

namespace Eliminated.Sim.Tests.Economy
{
    public class CosmeticsTests
    {
        [Fact]
        public void Roster_has_45_characters_with_30_free()
        {
            Assert.Equal(45, Cosmetics.Characters.Length);
            Assert.Equal(30, Cosmetics.Characters.Count(c => c.UnlockCost == 0));
        }

        [Fact]
        public void Accessories_cover_all_four_slots()
        {
            var slots = Cosmetics.Accessories.Select(a => a.Slot).Distinct().OrderBy(s => s);
            Assert.Equal(new[] { "ear", "eyes", "head", "neck" }, slots);
        }

        [Theory]
        [InlineData("cosmic", 3000)]
        [InlineData("blueberry", 200)]
        [InlineData("koala", 0)]
        [InlineData("crown", 420)]
        [InlineData("banana", 80)]
        public void Cost_lookup_is_unified(string id, int cost)
        {
            Assert.Equal(cost, Cosmetics.Cost(id));
        }

        [Fact]
        public void Free_characters_are_owned_without_unlocking()
        {
            var unlocked = new HashSet<string>();
            Assert.True(Cosmetics.IsOwned("koala", unlocked));
            Assert.False(Cosmetics.IsOwned("cosmic", unlocked));
            Assert.False(Cosmetics.IsOwned("crown", unlocked)); // accessories always paid
        }

        [Fact]
        public void Purchasing_a_character_deducts_marbles_and_grants_it()
        {
            int marbles = 500;
            var unlocked = new HashSet<string>();
            var r = CosmeticsWallet.TryPurchase("blueberry", ref marbles, unlocked);
            Assert.Equal(PurchaseResult.Ok, r);
            Assert.Equal(300, marbles);
            Assert.True(Cosmetics.IsOwned("blueberry", unlocked));
        }

        [Fact]
        public void Cannot_buy_what_you_cannot_afford()
        {
            int marbles = 100;
            var unlocked = new HashSet<string>();
            Assert.Equal(PurchaseResult.CannotAfford, CosmeticsWallet.TryPurchase("cosmic", ref marbles, unlocked));
            Assert.Equal(100, marbles); // untouched
        }

        [Fact]
        public void Buying_twice_is_a_no_op()
        {
            int marbles = 1000;
            var unlocked = new HashSet<string>();
            CosmeticsWallet.TryPurchase("carrot", ref marbles, unlocked);
            int after = marbles;
            Assert.Equal(PurchaseResult.AlreadyOwned, CosmeticsWallet.TryPurchase("carrot", ref marbles, unlocked));
            Assert.Equal(after, marbles);
        }

        [Fact]
        public void Bot_characters_are_all_valid_roster_ids()
        {
            foreach (var id in Eliminated.Sim.Core.BotNames.Characters)
                Assert.True(Cosmetics.IsCharacter(id), $"{id} is not a known character");
        }
    }
}
