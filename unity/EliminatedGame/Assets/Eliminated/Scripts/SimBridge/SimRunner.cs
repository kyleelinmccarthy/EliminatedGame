using System.Collections.Generic;
using System.Linq;
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
        public List<string> Accessories;
        public LocalPlayerInfo(string id, string name, string characterId, List<string> accessories = null)
        { Id = id; Name = name; CharacterId = characterId; Accessories = accessories; }
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

        // ── Session surface (delegates straight to the in-process room) ──
        public GameId? CurrentGame => Room?.CurrentGame;
        public int RoundIndex => Room?.RoundIndex ?? 0;
        // Mirrors GameRoom.IsFinalRound(): the last scheduled round (RoundIndex is the current
        // 0-based round), so the finale music cue starts at its intro and holds through any
        // Hardcore overtime rounds, which also satisfy RoundIndex >= TotalRounds - 1.
        public bool IsFinalGame => Room != null && Room.TotalRounds > 0 && Room.RoundIndex >= Room.TotalRounds - 1;
        public bool PlayStarted => Room?.PlayStarted ?? false;
        public string ChampionId => Room?.SeriesResult?.ChampionId;
        public RoundReport LastRoundReport => Room?.LastRoundReport;
        public SeriesResult SeriesResult => Room?.SeriesResult;
        public string NameOf(string playerId)
            => Room?.Players.FirstOrDefault(p => p.Id == playerId)?.Name ?? playerId;
        public int NumberOf(string playerId)
            => Room?.Players.FirstOrDefault(p => p.Id == playerId)?.Number ?? 0;

        /// <summary>The local human player ids in join order (1+ for co-op).</summary>
        public IReadOnlyList<string> LocalPlayerIds => _localIds;
        private readonly List<string> _localIds = new List<string>();

        private float _accumulator;

        /// <summary>Start a solo-vs-bots series hosted locally. <paramref name="fieldSize"/>
        /// (0 = default 12) caps the total contestant count, i.e. how many bots fill in;
        /// <paramref name="allowedGames"/> (null/empty = all) restricts the game pool.</summary>
        public void HostLocalSeries(SeriesMode mode, RoundsMode rounds,
            string playerName = "You", string characterId = "avo", List<string> accessories = null,
            int fieldSize = 0, List<GameId> allowedGames = null)
        {
            HostLocalCoop(mode, rounds, new List<LocalPlayerInfo>
            {
                new LocalPlayerInfo(LocalPlayerId, playerName, characterId, accessories)
            }, fieldSize, allowedGames);
        }

        /// <summary>Start a local series with one or more local humans (co-op); bots fill the rest.</summary>
        public void HostLocalCoop(SeriesMode mode, RoundsMode rounds, List<LocalPlayerInfo> locals,
            int fieldSize = 0, List<GameId> allowedGames = null)
        {
            int seed = System.Environment.TickCount;
            Room = new GameRoom(RoomCode.Make(seed), seed);
            var cfg = new RoomConfig { Mode = mode, Rounds = rounds, BotFill = true };
            // fieldSize caps total players: GameRoom bot-fills until _players.Count == MaxPlayers,
            // so MaxPlayers = N yields N-1 bots + the human(s).
            if (fieldSize > 0) cfg.MaxPlayers = Mathf.Clamp(fieldSize, Constants.MinToStart, Constants.MaxPlayers);
            if (allowedGames != null && allowedGames.Count > 0) cfg.AllowedGames = allowedGames;
            Room.UpdateConfig(cfg);
            _localIds.Clear();
            foreach (var p in locals)
            {
                var player = new Player(p.Id, p.Name, p.CharacterId, isBot: false);
                if (p.Accessories != null) player.Accessories = new List<string>(p.Accessories);
                Room.AddPlayer(player);
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
