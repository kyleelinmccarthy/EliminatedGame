using UnityEngine;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Room;

namespace Eliminated.Game.SimBridge
{
    /// <summary>
    /// Drives the authoritative <see cref="GameRoom"/> in-process for solo and
    /// local play. Accumulates real time and ticks the room at the fixed 20 Hz
    /// simulation rate, then exposes the latest snapshot for the view. Online
    /// play (Phase 5) swaps this for a networked snapshot source behind the same
    /// read surface.
    /// </summary>
    public sealed class SimRunner : MonoBehaviour
    {
        public const string LocalPlayerId = "local";

        public GameRoom Room { get; private set; }
        public Snapshot Latest { get; private set; }

        public RoomPhase Phase => Room?.Phase ?? RoomPhase.Lobby;
        public bool HasSeries => Room != null;

        private float _accumulator;

        /// <summary>Start a solo-vs-bots series hosted locally.</summary>
        public void HostLocalSeries(SeriesMode mode, RoundsMode rounds,
            string playerName = "You", string characterId = "avo")
        {
            int seed = System.Environment.TickCount;
            Room = new GameRoom(RoomCode.Make(seed), seed);
            Room.UpdateConfig(new RoomConfig { Mode = mode, Rounds = rounds, BotFill = true });
            Room.AddPlayer(new Player(LocalPlayerId, playerName, characterId, isBot: false));
            Room.StartSeries();
            Latest = null;
            _accumulator = 0f;
        }

        public void EndSeries() => Room = null;

        /// <summary>Submit the local player's input for the active game.</summary>
        public void Submit(GameInput input) => Room?.HandleInput(LocalPlayerId, input);

        /// <summary>The local player's actor this round, if any.</summary>
        public Actor LocalActor
        {
            get
            {
                var snap = Latest;
                if (snap?.Actors == null) return null;
                for (int i = 0; i < snap.Actors.Count; i++)
                    if (snap.Actors[i].Id == LocalPlayerId) return snap.Actors[i];
                return null;
            }
        }

        private void Update()
        {
            if (Room == null) return;

            // Fixed-step the simulation; cap catch-up so a hitch can't spiral.
            _accumulator += Time.deltaTime;
            int guard = 0;
            while (_accumulator >= Constants.Dt && guard++ < 8)
            {
                _accumulator -= Constants.Dt;
                Room.Tick(Constants.Dt);
            }
            Latest = Room.BuildSnapshot();
        }
    }
}
