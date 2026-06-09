using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Room
{
    /// <summary>
    /// Hosts many <see cref="GameRoom"/>s for an online host / dedicated server:
    /// create a room (unique join code), join by code, tick them all, and reap
    /// rooms that sit empty past a grace window. Pure C# and deterministic from a
    /// seed. Ported from the reference lib/server/RoomManager.ts (transport
    /// removed — the Unity/NGO layer feeds inputs and ships snapshots).
    /// </summary>
    public sealed class RoomManager
    {
        private static readonly float EmptyGraceSec = Constants.EmptyGraceMs / 1000f;

        private readonly Dictionary<string, GameRoom> _rooms = new Dictionary<string, GameRoom>();
        private readonly Dictionary<string, float> _emptyFor = new Dictionary<string, float>();
        private readonly Rng _rng;
        private int _seedCounter;

        public RoomManager(int seed = 1) { _rng = new Rng(seed); }

        public int RoomCount => _rooms.Count;
        public IEnumerable<string> Codes => _rooms.Keys;

        /// <summary>Create a fresh room with a unique join code.</summary>
        public GameRoom CreateRoom(RoomConfig config = null)
        {
            string code = UniqueCode();
            int roomSeed = unchecked(_rng.NextInt(int.MaxValue) ^ (++_seedCounter * 2654435761u).GetHashCode());
            var room = new GameRoom(code, roomSeed);
            if (config != null) room.UpdateConfig(config);
            _rooms[code] = room;
            _emptyFor[code] = 0f;
            return room;
        }

        public GameRoom GetRoom(string code)
            => code != null && _rooms.TryGetValue(code.ToUpperInvariant(), out var r) ? r : null;

        public bool HasRoom(string code) => GetRoom(code) != null;

        /// <summary>Join an existing room by code. Returns false if missing or full.</summary>
        public bool JoinRoom(string code, Player player)
        {
            var room = GetRoom(code);
            if (room == null) return false;
            if (room.Players.Count(p => !p.Spectator) >= room.Config.MaxPlayers && !player.Spectator)
                return false;
            room.AddPlayer(player);
            return true;
        }

        public void RemoveRoom(string code)
        {
            string key = code?.ToUpperInvariant();
            if (key == null) return;
            _rooms.Remove(key);
            _emptyFor.Remove(key);
        }

        /// <summary>Advance every room, then reap any that have sat empty (no humans)
        /// past the grace window.</summary>
        public void Tick(float dt)
        {
            foreach (var room in _rooms.Values) room.Tick(dt);

            var toReap = new List<string>();
            foreach (var kv in _rooms)
            {
                bool anyHuman = kv.Value.Players.Any(p => !p.IsBot);
                if (anyHuman) { _emptyFor[kv.Key] = 0f; continue; }
                _emptyFor[kv.Key] = _emptyFor.TryGetValue(kv.Key, out var t) ? t + dt : dt;
                if (_emptyFor[kv.Key] >= EmptyGraceSec) toReap.Add(kv.Key);
            }
            foreach (var code in toReap) RemoveRoom(code);
        }

        private string UniqueCode()
        {
            for (int guard = 0; guard < 1000; guard++)
            {
                string code = RoomCode.Make(_rng);
                if (!_rooms.ContainsKey(code)) return code;
            }
            return RoomCode.Make(_rng) + _rooms.Count; // fallback (astronomically unlikely)
        }
    }
}
