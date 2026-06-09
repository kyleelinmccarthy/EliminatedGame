using System.Collections.Generic;

namespace Eliminated.Sim.Economy
{
    /// <summary>Result of attempting to buy a cosmetic.</summary>
    public enum PurchaseResult { Ok, AlreadyOwned, Unknown, CannotAfford }

    /// <summary>
    /// Pure unlock economy: deduct marbles and grant a cosmetic. Operates on a
    /// caller-supplied wallet (marbles + unlocked set) so the client's saved
    /// profile and the sim share identical purchase rules — and the rules are
    /// unit-tested.
    /// </summary>
    public static class CosmeticsWallet
    {
        /// <summary>Attempt to buy <paramref name="id"/>. On success, mutates
        /// <paramref name="marbles"/> and <paramref name="unlocked"/>.</summary>
        public static PurchaseResult TryPurchase(string id, ref int marbles, ISet<string> unlocked)
        {
            int cost = Cosmetics.Cost(id);
            if (cost < 0) return PurchaseResult.Unknown;
            if (Cosmetics.IsOwned(id, unlocked)) return PurchaseResult.AlreadyOwned;
            if (marbles < cost) return PurchaseResult.CannotAfford;
            marbles -= cost;
            unlocked.Add(id);
            return PurchaseResult.Ok;
        }

        public static bool CanAfford(string id, int marbles)
        {
            int cost = Cosmetics.Cost(id);
            return cost >= 0 && marbles >= cost;
        }
    }
}
