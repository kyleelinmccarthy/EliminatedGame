using System.Collections.Generic;

namespace Eliminated.Sim.Model
{
    /// <summary>
    /// A room participant that persists across rounds (human or bot). Distinct
    /// from <see cref="Actor"/>, which is the per-round in-arena body. Ported from
    /// lib/server/Player.ts.
    /// </summary>
    public sealed class Player
    {
        public string Id;
        public string ClientId;
        public string Name;
        public string CharacterId;
        public List<string> Accessories = new List<string>();

        public bool IsBot;
        public bool Connected = true;
        public bool Ready;
        public bool Spectator;
        public int Number;          // unique 1..456 tag

        // ── Series-scoped state ──────────────────────────────────────────
        /// <summary>Hardcore permadeath flag; false once eliminated for good.</summary>
        public bool AliveInSeries = true;
        public int Score;           // casual points across rounds
        public int RoundsSurvived;
        public int MarblesEarned;   // accumulated this series

        public Player() { }

        public Player(string id, string name, string characterId, bool isBot = false)
        {
            Id = id;
            Name = name;
            CharacterId = characterId;
            IsBot = isBot;
            Connected = !isBot;
            Ready = isBot;
        }
    }
}
