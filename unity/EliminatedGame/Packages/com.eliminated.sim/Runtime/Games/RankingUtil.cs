using System.Collections.Generic;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Shared ranking for non-arena (discrete) games: survivors first in the
    /// given best-first order, then the eliminated in reverse elimination order
    /// (last out ranks highest among the dead). Mirrors the reference
    /// buildRanking().
    /// </summary>
    public static class RankingUtil
    {
        public static RoundResult Build(GameId game, IReadOnlyList<string> survivorIdsBestFirst,
            IReadOnlyList<(string id, string note)> elimOrderEarliestFirst)
        {
            var res = new RoundResult { Game = game };
            int place = 1;
            foreach (var id in survivorIdsBestFirst)
            {
                res.Ranking.Add(new RankEntry(id, place++, true));
                res.SurvivorIds.Add(id);
            }
            for (int i = elimOrderEarliestFirst.Count - 1; i >= 0; i--)
                res.Ranking.Add(new RankEntry(elimOrderEarliestFirst[i].id, place++, false, elimOrderEarliestFirst[i].note));
            return res;
        }

        /// <summary>
        /// Like <see cref="Build"/>, but in a finale (<paramref name="forceSingle"/>)
        /// crowns only the best survivor and demotes co-survivors to just-eliminated.
        /// For finale-capable discrete games (Jump Rope, RPS, Glass Bridge).
        /// </summary>
        public static RoundResult Crown(GameId game, IReadOnlyList<string> survivorsBestFirst,
            IReadOnlyList<(string id, string note)> elimOrderEarliestFirst, bool forceSingle, string demoteNote)
        {
            var res = new RoundResult { Game = game };
            int place = 1;
            if (forceSingle && survivorsBestFirst.Count > 1)
            {
                res.Ranking.Add(new RankEntry(survivorsBestFirst[0], place++, true));
                res.SurvivorIds.Add(survivorsBestFirst[0]);
                for (int i = 1; i < survivorsBestFirst.Count; i++)
                    res.Ranking.Add(new RankEntry(survivorsBestFirst[i], place++, false, demoteNote));
            }
            else
            {
                foreach (var id in survivorsBestFirst)
                {
                    res.Ranking.Add(new RankEntry(id, place++, true));
                    res.SurvivorIds.Add(id);
                }
            }
            for (int i = elimOrderEarliestFirst.Count - 1; i >= 0; i--)
                res.Ranking.Add(new RankEntry(elimOrderEarliestFirst[i].id, place++, false, elimOrderEarliestFirst[i].note));
            return res;
        }
    }
}
