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
    }
}
