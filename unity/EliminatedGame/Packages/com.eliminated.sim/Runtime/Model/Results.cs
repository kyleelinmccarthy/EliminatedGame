using System.Collections.Generic;

namespace Eliminated.Sim.Model
{
    /// <summary>One player's placement in a single round.</summary>
    public sealed class RankEntry
    {
        public string PlayerId;
        public int Placement;       // 1 = best this round
        public bool Survived;
        public int MarblesEarned;
        public string Note;         // "Caught moving!", "Frozen at buzzer", …

        public RankEntry() { }

        public RankEntry(string playerId, int placement, bool survived, string note = null)
        {
            PlayerId = playerId;
            Placement = placement;
            Survived = survived;
            Note = note;
        }
    }

    /// <summary>Outcome of a single minigame round.</summary>
    public sealed class RoundResult
    {
        public GameId Game;
        public List<string> SurvivorIds = new List<string>();
        public List<RankEntry> Ranking = new List<RankEntry>();
    }

    /// <summary>One player's final position in the series.</summary>
    public sealed class SeriesStanding
    {
        public string PlayerId;
        public int Placement;       // 1 = champion
        public int Marbles;         // total earned this series
        public int RoundsSurvived;
        public string Title;
    }

    /// <summary>Final standings for a completed series.</summary>
    public sealed class SeriesResult
    {
        public List<SeriesStanding> Standings = new List<SeriesStanding>();
        public string ChampionId;
    }
}
