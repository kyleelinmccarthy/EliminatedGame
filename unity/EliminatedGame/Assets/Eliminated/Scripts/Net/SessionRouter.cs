using System.Collections.Generic;
using Eliminated.Sim.Model;

namespace Eliminated.Game.Net
{
    /// <summary>
    /// A thin <see cref="ISnapshotSource"/> that forwards to whichever backend is
    /// currently <see cref="Active"/> — the in-process <c>SimRunner</c> for solo /
    /// local co-op, or the <c>NetClient</c> for online play. The view, input, and
    /// HUD hold a reference to this router, so switching modes is a single
    /// assignment to <see cref="Active"/> with no re-wiring. When no backend is
    /// active (out at the menu before either starts) it reads as an empty,
    /// series-less source so callers never null-check the mode.
    /// </summary>
    public sealed class SessionRouter : ISnapshotSource
    {
        private static readonly IReadOnlyList<string> Empty = new string[0];

        /// <summary>The live backend the router currently mirrors.</summary>
        public ISnapshotSource Active { get; set; }

        public bool HasSeries => Active?.HasSeries ?? false;
        public RoomPhase Phase => Active?.Phase ?? RoomPhase.Lobby;
        public Snapshot Latest => Active?.Latest;
        public IReadOnlyList<string> LocalPlayerIds => Active?.LocalPlayerIds ?? Empty;
        public void SubmitFor(string playerId, GameInput input) => Active?.SubmitFor(playerId, input);
        public Actor ActorFor(string playerId) => Active?.ActorFor(playerId);

        public GameId? CurrentGame => Active?.CurrentGame;
        public int RoundIndex => Active?.RoundIndex ?? 0;
        public bool IsFinalGame => Active?.IsFinalGame ?? false;
        public bool PlayStarted => Active?.PlayStarted ?? false;
        public string ChampionId => Active?.ChampionId;
        public RoundReport LastRoundReport => Active?.LastRoundReport;
        public SeriesResult SeriesResult => Active?.SeriesResult;
        public string NameOf(string playerId) => Active?.NameOf(playerId) ?? playerId;
        public int NumberOf(string playerId) => Active?.NumberOf(playerId) ?? 0;
    }
}
