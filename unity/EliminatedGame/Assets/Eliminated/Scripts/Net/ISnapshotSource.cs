using System.Collections.Generic;
using Eliminated.Sim.Model;

namespace Eliminated.Game.Net
{
    /// <summary>
    /// The read/write surface the view, HUD, and input layer use, independent of
    /// whether play is local (in-process <c>SimRunner</c>) or online (a
    /// <c>NetClient</c> consuming server snapshots). Implementing this on both lets
    /// the same rendering/input code drive either mode — the only thing that
    /// changes is who produces the snapshots.
    /// </summary>
    public interface ISnapshotSource
    {
        bool HasSeries { get; }
        RoomPhase Phase { get; }
        Snapshot Latest { get; }
        IReadOnlyList<string> LocalPlayerIds { get; }
        void SubmitFor(string playerId, GameInput input);
        Actor ActorFor(string playerId);

        // ── Room/session state the HUD renders, independent of backend ──
        // Local play sources these from the in-process GameRoom; online play from
        // the authoritative server's room message. Exposing them here lets the HUD,
        // intro, and results screens render identically for both.
        GameId? CurrentGame { get; }
        int RoundIndex { get; }
        /// <summary>True when the current round is the series finale (last scheduled round, or
        /// any Hardcore overtime round past it). Drives the finale music cue. Online play
        /// reports false — the server room message carries no total-round count.</summary>
        bool IsFinalGame { get; }
        bool PlayStarted { get; }
        string ChampionId { get; }
        RoundReport LastRoundReport { get; }
        SeriesResult SeriesResult { get; }
        string NameOf(string playerId);
        /// <summary>The player's jersey/roster number (the "#N" identity, not a placement),
        /// resolved from the room roster. 0 if unknown. Used by results screens so a player
        /// can find their own row between games.</summary>
        int NumberOf(string playerId);
    }
}
