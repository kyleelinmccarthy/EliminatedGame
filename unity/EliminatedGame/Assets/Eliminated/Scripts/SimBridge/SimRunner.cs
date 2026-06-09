using System.Collections.Generic;
using UnityEngine;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Room;
using Eliminated.Game.Net;

namespace Eliminated.Game.SimBridge
{
    /// <summary>Identity for one local human (solo = one, co-op = several).</summary>
    public struct LocalPlayerInfo
    {
        public string Id;
        public string Name;
        public string CharacterId;
        public LocalPlayerInfo(string id, string name, string characterId)
        { Id = id; Name = name; CharacterId = characterId; }
    }

    /// <summary>
    /// Drives the authoritative <see cref="GameRoom"/> in-process for solo and
    /// local co-op. Accumulates real time and ticks the room at the fixed 20 Hz
    /// simulation rate, then exposes the latest snapshot for the view. Online play
    /// (Phase 5) swaps this for a networked snapshot source behind the same read
    /// surface (see docs/IMPLEMENTATION_GUIDE.md).
    /// </summary>
    public sealed class SimRunner : MonoBehaviour, ISnapshotSource
    {
        public const string LocalPlayerId = "local0";

        public GameRoom Room { get; private set; }
        public Snapshot Latest { get; private set; }
        public RoomPhase Phase => Room?.Phase ?? RoomPhase.Lobby;
        public bool HasSeries => Room != null;

        /// <summary>The local human player ids in join order (1+ for co-op).</summary>
        public IReadOnlyList<string> LocalPlayerIds => _localIds;
        private readonly List<string> _localIds = new List<string>();

        private float _accumulator;

        /// <summary>Start a solo-vs-bots series hosted locally.</summary>
        public void HostLocalSeries(SeriesMode mode, RoundsMode rounds,
            string playerName = "You", string characterId = "avo")
        {
            HostLocalCoop(mode, rounds, new List<LocalPlayerInfo>
            {
                new LocalPlayerInfo(LocalPlayerId, playerName, characterId)
            });
        }

        /// <summary>Start a local series with one or more local humans (co-op); bots fill the rest.</summary>
        public void HostLocalCoop(SeriesMode mode, RoundsMode rounds, List<LocalPlayerInfo> locals)
        {
            int seed = System.Environment.TickCount;
            Room = new GameRoom(RoomCode.Make(seed), seed);
            Room.UpdateConfig(new RoomConfig { Mode = mode, Rounds = rounds, BotFill = true });
            _localIds.Clear();
            foreach (var p in locals)
            {
                Room.AddPlayer(new Player(p.Id, p.Name, p.CharacterId, isBot: false));
                _localIds.Add(p.Id);
            }
            Room.StartSeries();
            Latest = null;
            _accumulator = 0f;
        }

        public void EndSeries() { Room = null; _localIds.Clear(); }

        /// <summary>Submit the (solo) local player's input for the active game.</summary>
        public void Submit(GameInput input) => Room?.HandleInput(LocalPlayerId, input);

        /// <summary>Submit a specific local player's input (co-op).</summary>
        public void SubmitFor(string playerId, GameInput input) => Room?.HandleInput(playerId, input);

        /// <summary>The solo local player's actor this round, if any.</summary>
        public Actor LocalActor => ActorFor(LocalPlayerId);

        /// <summary>A given player's actor this tick, if present.</summary>
        public Actor ActorFor(string playerId)
        {
            var snap = Latest;
            if (snap?.Actors == null) return null;
            for (int i = 0; i < snap.Actors.Count; i++)
                if (snap.Actors[i].Id == playerId) return snap.Actors[i];
            return null;
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
