// Online client. Talks the same protocol as the verified headless server
// (server/Eliminated.Server) over WebSockets, using the same Wire codec, so it
// also works against a Unity-Relay host that bridges to it. Control messages are
// JSON text frames; input/snapshots are binary frames (tag byte 1 = input,
// 2 = snapshot). See docs/IMPLEMENTATION_GUIDE.md Phase 5.
//
// Note: ClientWebSocket is supported on desktop/IL2CPP (the Steam target) but not
// WebGL — a WebGL build needs a JS WebSocket transport behind this same surface.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Eliminated.Sim.Net;

namespace Eliminated.Game.Net
{
    /// <summary>
    /// Connects to the authoritative server over WebSockets and exposes its
    /// snapshots/room-state through <see cref="ISnapshotSource"/>, so the existing
    /// ArenaView/LocalInputHub/TouchControls render and drive online play
    /// unchanged. The server already serializes per-round and final standings into
    /// the room message, so the HUD's intro/results screens render online too.
    /// </summary>
    public sealed class NetClient : MonoBehaviour, ISnapshotSource
    {
        /// <summary>Where the connection is in its lifecycle (for the lobby UI).</summary>
        public enum LinkState { Idle, Connecting, Online, Failed }

        /// <summary>One roster row for the lobby list.</summary>
        public struct LobbyPlayer { public string Name; public bool Bot; public bool IsYou; public int Number; }

        // ── JSON shapes, matching GameServer.BuildRoomJson exactly (field names
        //    are case-sensitive for JsonUtility). ──
        [Serializable] private class Welcome { public string t, playerId; }
        [Serializable] private class ErrorMsg { public string t, msg; }
        [Serializable] private class NetPlayer { public string Id, Name; public int Number; public bool alive, bot; public int MarblesEarned; }
        [Serializable] private class NetRankEntry { public string PlayerId; public int Placement; public bool Survived; public int marbles; public string Note; }
        [Serializable] private class NetLastRound { public string game; public int number; public NetRankEntry[] entries; }
        [Serializable] private class NetStanding { public string PlayerId; public int Placement; public int Marbles; public int RoundsSurvived; public string Title; }
        [Serializable] private class RoomMsg
        {
            public string t, code, phase, game, youAre, champion;
            public int round;
            public bool isHost;
            public bool finalGame;
            public NetPlayer[] players;
            public NetLastRound lastRound;
            public NetStanding[] standings;
        }

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly List<string> _localIds = new List<string>();
        private volatile RoomMsg _room;
        private volatile Snapshot _latest;

        public string PlayerId { get; private set; }
        public LinkState State { get; private set; } = LinkState.Idle;
        public string LastError { get; private set; }

        // ── ISnapshotSource: snapshot/identity ──
        public bool HasSeries => _room != null;
        public RoomPhase Phase => Enum.TryParse<RoomPhase>(_room?.phase ?? "Lobby", out var p) ? p : RoomPhase.Lobby;
        public Snapshot Latest => _latest;
        public IReadOnlyList<string> LocalPlayerIds => _localIds;

        // ── ISnapshotSource: room/session state (from the server's room message) ──
        public GameId? CurrentGame
            => Enum.TryParse<GameId>(_room?.game ?? "", out var g) ? (GameId?)g : null;
        public int RoundIndex => _room?.round ?? 0;
        // The server sends a `finalGame` bool in the room message (computed from the
        // authoritative GameRoom, and mystery-safe), so the finale music + "The final
        // game…" announcer fire online exactly as they do in local play.
        public bool IsFinalGame => _room?.finalGame ?? false;
        // The server nulls StartAt in the snapshot once the GO hold ends; only
        // meaningful while Playing (no snapshots are sent during other phases).
        public bool PlayStarted => Phase == RoomPhase.Playing && _latest != null && _latest.StartAt == null;
        public string ChampionId => _room?.champion;

        public string NameOf(string playerId)
        {
            var r = _room;
            if (r?.players != null)
                foreach (var p in r.players)
                    if (p.Id == playerId) return p.Name;
            return playerId;
        }

        public int NumberOf(string playerId)
        {
            var r = _room;
            if (r?.players != null)
                foreach (var p in r.players)
                    if (p.Id == playerId) return p.Number;
            return 0;
        }

        // Results are rebuilt only when the room message changes (cheap, but avoids
        // re-allocating every OnGUI frame during the result screens).
        private RoomMsg _resultsFor;
        private RoundReport _reportCache;
        private SeriesResult _seriesCache;

        public RoundReport LastRoundReport { get { RebuildResults(); return _reportCache; } }
        public SeriesResult SeriesResult { get { RebuildResults(); return _seriesCache; } }

        // ── Lobby helpers ──
        public bool InRoom => _room != null;
        public bool IsHost => _room?.isHost ?? false;
        public string RoomCode => _room?.code;
        public int PlayerCount => _room?.players?.Length ?? 0;

        public LobbyPlayer[] Roster()
        {
            var r = _room;
            var ps = r?.players;
            if (ps == null) return Array.Empty<LobbyPlayer>();
            var list = new LobbyPlayer[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                list[i] = new LobbyPlayer { Name = ps[i].Name, Bot = ps[i].bot, IsYou = ps[i].Id == PlayerId, Number = ps[i].Number };
            return list;
        }

        // ── Connection lifecycle ──
        public async void Connect(string url, string name, string characterId)
        {
            LastError = null;
            State = LinkState.Connecting;
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            try
            {
                await _ws.ConnectAsync(new Uri(url), _cts.Token);
                _ = ReceiveLoop(_cts.Token);
                await SendText($"{{\"t\":\"hello\",\"name\":\"{Esc(name)}\",\"characterId\":\"{Esc(characterId)}\"}}");
            }
            catch (Exception e)
            {
                State = LinkState.Failed;
                LastError = e.Message;
            }
        }

        public async void HostRoom(string mode, int rounds) => await SendText($"{{\"t\":\"createRoom\",\"mode\":\"{mode}\",\"rounds\":{rounds}}}");
        public async void JoinByCode(string code) { LastError = null; await SendText($"{{\"t\":\"joinRoom\",\"code\":\"{Esc(code)}\"}}"); }
        public async void AddBot() => await SendText("{\"t\":\"addBot\"}");
        public async void StartSeries() => await SendText("{\"t\":\"startSeries\"}");

        /// <summary>Leave the current room but stay connected (back to host/join).</summary>
        public async void Leave()
        {
            await SendText("{\"t\":\"leave\"}");
            _room = null;
            _latest = null;
        }

        /// <summary>Tear the connection down entirely (leaving the online page).</summary>
        public void Disconnect()
        {
            try { _ = SendText("{\"t\":\"leave\"}"); } catch { }
            try { _cts?.Cancel(); _ws?.Dispose(); } catch { }
            _ws = null;
            _room = null;
            _latest = null;
            _localIds.Clear();
            PlayerId = null;
            State = LinkState.Idle;
        }

        public void SubmitFor(string playerId, GameInput input)
        {
            // The server knows which player this connection is; the id is ignored online.
            var bytes = Wire.EncodeInput(input);
            var framed = new byte[bytes.Length + 1];
            framed[0] = 1; // input tag
            Buffer.BlockCopy(bytes, 0, framed, 1, bytes.Length);
            _ = SendBinary(framed);
        }

        public Actor ActorFor(string playerId)
        {
            var s = _latest;
            if (s?.Actors == null) return null;
            for (int i = 0; i < s.Actors.Count; i++)
                if (s.Actors[i].Id == playerId) return s.Actors[i];
            return null;
        }

        // ── Receive ──────────────────────────────────────────────────────
        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buf = new byte[32 * 1024];
            var ms = new MemoryStream();
            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (r.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buf, 0, r.Count);
                    } while (!r.EndOfMessage);

                    var payload = ms.ToArray();
                    if (r.MessageType == WebSocketMessageType.Text) HandleText(Encoding.UTF8.GetString(payload));
                    else if (payload.Length > 0 && payload[0] == 2) HandleSnapshot(payload);
                }
            }
            catch { /* dropped */ }
            finally { if (State == LinkState.Online) State = LinkState.Failed; }
        }

        private void HandleText(string json)
        {
            // Discriminate on the leading "t" tag so a player named e.g. "room"
            // can't be mistaken for a control message.
            if (json.Contains("\"t\":\"welcome\""))
            {
                var w = JsonUtility.FromJson<Welcome>(json);
                PlayerId = w.playerId;
                _localIds.Clear();
                _localIds.Add(PlayerId);
                State = LinkState.Online;
            }
            else if (json.Contains("\"t\":\"room\"")) _room = JsonUtility.FromJson<RoomMsg>(json);
            else if (json.Contains("\"t\":\"error\"")) LastError = JsonUtility.FromJson<ErrorMsg>(json).msg;
        }

        private void HandleSnapshot(byte[] payload)
        {
            var inner = new byte[payload.Length - 1];
            Buffer.BlockCopy(payload, 1, inner, 0, inner.Length);
            var f = Wire.DecodeFrame(inner);
            var snap = new Snapshot
            {
                Game = f.Game,
                T = f.T,
                StartAt = f.HasStartAt ? (double?)f.StartAt : null,
                Actors = new List<Actor>(f.Actors.Count),
                Fx = f.Fx
            };
            foreach (var na in f.Actors)
            {
                snap.Actors.Add(new Actor
                {
                    Id = na.Id,
                    Pos = new Vec2(na.X, na.Y),
                    Facing = na.Facing,
                    Scale = na.Scale <= 0f ? 1f : na.Scale,
                    Progress = na.Progress,
                    Number = na.Number,
                    Team = na.Team,
                    Anim = (AnimState)na.Anim,
                    Alive = na.Has(NetActor.Alive),
                    It = na.Has(NetActor.It),
                    Frozen = na.Has(NetActor.Frozen),
                    Burning = na.Has(NetActor.Burning),
                    Shield = na.Has(NetActor.Shield),
                    Ghost = na.Has(NetActor.Ghost)
                });
            }
            // Decode the per-game data so props/discrete HUDs render online too.
            snap.Data = DecodeData(f.Game, f.DataJson);
            _latest = snap;
        }

        // ── Adapt the server's room-message results into the sim result types the
        //    HUD already renders. Rebuilt only when the room message changes. ──
        private void RebuildResults()
        {
            var r = _room;
            if (ReferenceEquals(r, _resultsFor)) return;
            _resultsFor = r;
            _reportCache = BuildReport(r?.lastRound);
            _seriesCache = BuildSeries(r);
        }

        private static RoundReport BuildReport(NetLastRound lr)
        {
            if (lr == null) return null;
            var report = new RoundReport
            {
                Game = Enum.TryParse<GameId>(lr.game ?? "", out var g) ? g : default,
                RoundNumber = lr.number
            };
            if (lr.entries != null)
                foreach (var e in lr.entries)
                    report.Entries.Add(new RankEntry(e.PlayerId, e.Placement, e.Survived, e.Note) { MarblesEarned = e.marbles });
            return report;
        }

        private static SeriesResult BuildSeries(RoomMsg r)
        {
            if (r?.standings == null) return null;
            var sr = new SeriesResult { ChampionId = r.champion };
            foreach (var s in r.standings)
                sr.Standings.Add(new SeriesStanding
                {
                    PlayerId = s.PlayerId,
                    Placement = s.Placement,
                    Marbles = s.Marbles,
                    RoundsSurvived = s.RoundsSurvived,
                    Title = s.Title
                });
            return sr;
        }

        /// <summary>Deserialize the per-game data json into the type
        /// <see cref="DataWire.TypeFor"/> declares (verified by DataWireTests).</summary>
        private static object DecodeData(GameId game, string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            switch (game)
            {
                case GameId.RedLight: return JsonUtility.FromJson<RedLightGreenLight.RlglData>(json);
                case GameId.Tag: return JsonUtility.FromJson<Tag.TagData>(json);
                case GameId.Mingle: return JsonUtility.FromJson<Mingle.MingleData>(json);
                case GameId.GlassBridge: return JsonUtility.FromJson<GlassBridge.GlassData>(json);
                case GameId.TugOfWar: return JsonUtility.FromJson<TugOfWar.TugData>(json);
                case GameId.RpsMinusOne: return JsonUtility.FromJson<RpsMinusOne.RpsData>(json);
                case GameId.JumpRope: return JsonUtility.FromJson<JumpRope.RopeData>(json);
                case GameId.Boomerang: return JsonUtility.FromJson<Boomerang.BoomData>(json);
                case GameId.Dodgeball: return JsonUtility.FromJson<Dodgeball.DodgeData>(json);
                case GameId.MusicalChairs: return JsonUtility.FromJson<MusicalChairs.McData>(json);
                case GameId.PresentSwap: return JsonUtility.FromJson<PresentSwap.PresentData>(json);
                case GameId.PropHunt: return JsonUtility.FromJson<PropHunt.PropData>(json);
                case GameId.ChutesAndLadders: return JsonUtility.FromJson<ChutesAndLadders.ChutesData>(json);
                case GameId.SimonSays: return JsonUtility.FromJson<SimonSays.SimonData>(json);
                case GameId.KeepyUppy: return JsonUtility.FromJson<KeepyUppy.KeepyData>(json);
                case GameId.KingOfTheHill: return JsonUtility.FromJson<KingOfTheHill.KothData>(json);
                default: return null;
            }
        }

        // ── Send ─────────────────────────────────────────────────────────
        private Task SendText(string json) => Send(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text);
        private Task SendBinary(byte[] data) => Send(data, WebSocketMessageType.Binary);

        private async Task Send(byte[] data, WebSocketMessageType type)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            await _sendLock.WaitAsync();
            try { await _ws.SendAsync(new ArraySegment<byte>(data), type, true, _cts.Token); }
            catch { }
            finally { _sendLock.Release(); }
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private void OnDestroy()
        {
            try { _cts?.Cancel(); _ws?.Dispose(); } catch { }
        }
    }
}
