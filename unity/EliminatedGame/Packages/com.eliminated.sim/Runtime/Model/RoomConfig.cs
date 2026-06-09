using System.Collections.Generic;

namespace Eliminated.Sim.Model
{
    /// <summary>Series length: a mystery sequence, or a fixed number of rounds.</summary>
    public readonly struct RoundsMode
    {
        public readonly bool Mystery;
        public readonly int Count; // meaningful only when !Mystery

        private RoundsMode(bool mystery, int count)
        {
            Mystery = mystery;
            Count = count;
        }

        public static RoundsMode AsMystery() => new RoundsMode(true, 0);
        public static RoundsMode Fixed(int count) => new RoundsMode(false, count < 1 ? 1 : count);

        public override string ToString() => Mystery ? "mystery" : Count.ToString();
    }

    /// <summary>
    /// Host-configurable room settings. Defaults match the reference
    /// DEFAULT_CONFIG (lib/shared/constants.ts).
    /// </summary>
    public sealed class RoomConfig
    {
        public SeriesMode Mode = SeriesMode.Casual;
        public RoundsMode Rounds = RoundsMode.AsMystery();

        /// <summary>Empty = all games allowed.</summary>
        public List<GameId> AllowedGames = new List<GameId>();

        public bool BotFill = true;
        public int MaxPlayers = Core.Constants.MaxPlayers;
        public bool FriendlyFire = true;

        /// <summary>Hardcore-only: random dark rounds with flashlights.</summary>
        public bool NightMode = false;

        public RoomConfig Clone() => new RoomConfig
        {
            Mode = Mode,
            Rounds = Rounds,
            AllowedGames = new List<GameId>(AllowedGames),
            BotFill = BotFill,
            MaxPlayers = MaxPlayers,
            FriendlyFire = FriendlyFire,
            NightMode = NightMode
        };
    }
}
