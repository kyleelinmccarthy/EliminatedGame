using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Net;
using Eliminated.Sim.Room;

namespace Eliminated.Server
{
    /// <summary>
    /// Headless authoritative game server. Hosts the pure-C# simulation via
    /// <see cref="RoomManager"/> over WebSockets. Control messages are JSON text
    /// frames; input/snapshots are binary frames using the tested
    /// <see cref="Wire"/> codec (tag byte 1 = input, 2 = snapshot). The simulation
    /// is mutated only on the single tick thread (client messages are queued);
    /// each connection has its own outbound channel so sends never overlap.
    /// </summary>
    public sealed class GameServer
    {
        private sealed class OutMsg { public WebSocketMessageType Type; public byte[] Data; }

        private sealed class Conn
        {
            public Guid Id;
            public WebSocket Ws;
            public readonly Channel<OutMsg> Out = Channel.CreateUnbounded<OutMsg>();
            public string PlayerId, Name = "Blob", CharacterId = "avo", RoomCode, LastRoomJson;
        }

        private readonly int _port;
        private readonly int _tickDelayMs;
        private readonly RoomManager _rooms = new RoomManager(seed: 12345);
        private readonly ConcurrentQueue<Action> _commands = new ConcurrentQueue<Action>();
        private readonly Dictionary<Guid, Conn> _conns = new Dictionary<Guid, Conn>();
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions { IncludeFields = true };
        private HttpListener _listener;
        private CancellationTokenSource _cts;

        /// <summary><paramref name="tickDelayMs"/> &lt;= 0 ticks as fast as possible
        /// (decouples sim time from wall-clock, for tests); otherwise real-time 20 Hz.</summary>
        public GameServer(int port, int tickDelayMs = -1)
        {
            _port = port;
            _tickDelayMs = tickDelayMs < 0 ? (int)Constants.TickMs : tickDelayMs;
        }

        public string Url => $"ws://localhost:{_port}/";

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _ = AcceptLoop(_cts.Token);
            _ = TickLoop(_cts.Token);
            Console.WriteLine($"[Eliminated.Server] listening on {Url}");
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
        }

        // ── Accept + per-connection receive ──────────────────────────────
        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                if (!ctx.Request.IsWebSocketRequest) { ctx.Response.StatusCode = 400; ctx.Response.Close(); continue; }
                var wsCtx = await ctx.AcceptWebSocketAsync(null);
                _ = HandleConn(wsCtx.WebSocket, ct);
            }
        }

        private async Task HandleConn(WebSocket ws, CancellationToken ct)
        {
            var conn = new Conn { Id = Guid.NewGuid(), Ws = ws };
            Enqueue(() => _conns[conn.Id] = conn);
            _ = SendLoop(conn, ct);

            var buf = new byte[16 * 1024];
            var ms = new System.IO.MemoryStream();
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (res.MessageType == WebSocketMessageType.Close) goto done;
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);

                    byte[] payload = ms.ToArray();
                    var connId = conn.Id;
                    if (res.MessageType == WebSocketMessageType.Text)
                    {
                        string text = Encoding.UTF8.GetString(payload);
                        Enqueue(() => HandleText(connId, text));
                    }
                    else
                    {
                        Enqueue(() => HandleBinary(connId, payload));
                    }
                }
            }
            catch { /* connection dropped */ }
            done:
            Enqueue(() => Drop(conn.Id));
        }

        private async Task SendLoop(Conn conn, CancellationToken ct)
        {
            try
            {
                await foreach (var msg in conn.Out.Reader.ReadAllAsync(ct))
                {
                    if (conn.Ws.State != WebSocketState.Open) break;
                    await conn.Ws.SendAsync(new ArraySegment<byte>(msg.Data), msg.Type, true, ct);
                }
            }
            catch { }
        }

        private void Enqueue(Action a) => _commands.Enqueue(a);

        private void Send(Conn conn, string json)
            => conn.Out.Writer.TryWrite(new OutMsg { Type = WebSocketMessageType.Text, Data = Encoding.UTF8.GetBytes(json) });

        private void SendBinary(Conn conn, byte[] data)
            => conn.Out.Writer.TryWrite(new OutMsg { Type = WebSocketMessageType.Binary, Data = data });

        // ── Tick thread (the only place the sim is touched) ──────────────
        private async Task TickLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                while (_commands.TryDequeue(out var cmd))
                {
                    try { cmd(); } catch (Exception e) { Console.WriteLine("[cmd] " + e.Message); }
                }
                _rooms.Tick(Constants.Dt);
                SendUpdates();
                if (_tickDelayMs <= 0) await Task.Yield();
                else { try { await Task.Delay(_tickDelayMs, ct); } catch { break; } }
            }
        }

        private void SendUpdates()
        {
            // build each playing room's snapshot ONCE (BuildSnapshot drains fx)
            var snaps = new Dictionary<string, Snapshot>();
            foreach (var conn in _conns.Values)
            {
                if (conn.RoomCode == null) continue;
                var room = _rooms.GetRoom(conn.RoomCode);
                if (room == null) { conn.RoomCode = null; continue; }

                string roomJson = BuildRoomJson(room, conn.PlayerId);
                if (roomJson != conn.LastRoomJson) { Send(conn, roomJson); conn.LastRoomJson = roomJson; }

                if (room.Phase == RoomPhase.Playing)
                {
                    if (!snaps.TryGetValue(conn.RoomCode, out var snap))
                    {
                        snap = room.BuildSnapshot();
                        snaps[conn.RoomCode] = snap;
                    }
                    if (snap != null) SendBinary(conn, BuildSnapshotFrame(snap, conn.PlayerId));
                }
            }
        }

        private byte[] BuildSnapshotFrame(Snapshot snap, string playerId)
        {
            object secret = (snap.Secrets != null && playerId != null && snap.Secrets.TryGetValue(playerId, out var s)) ? s : null;
            string dataJson = JsonSerializer.Serialize(new { data = snap.Data, secret }, _json);
            byte[] frame = Wire.EncodeFrame(snap.Game, snap.T, snap.StartAt, snap.Actors, snap.Fx, dataJson);
            var tagged = new byte[frame.Length + 1];
            tagged[0] = 2; // snapshot
            Buffer.BlockCopy(frame, 0, tagged, 1, frame.Length);
            return tagged;
        }

        private string BuildRoomJson(GameRoom room, string youAre)
        {
            var report = room.LastRoundReport;
            var series = room.SeriesResult;
            return JsonSerializer.Serialize(new
            {
                t = "room",
                code = room.Code,
                phase = room.Phase.ToString(),
                round = room.RoundIndex,
                game = room.CurrentGame?.ToString(),
                youAre,
                isHost = room.HostId == youAre,
                players = room.Players.Select(p => new { p.Id, p.Name, p.Number, alive = p.AliveInSeries, bot = p.IsBot, p.MarblesEarned }).ToList(),
                champion = series?.ChampionId,
                // results so the client's round/series screens render online too
                lastRound = report == null ? null : new
                {
                    game = report.Game.ToString(),
                    number = report.RoundNumber,
                    entries = report.Entries.Select(e => new { e.PlayerId, e.Placement, e.Survived, marbles = e.MarblesEarned, e.Note }).ToList()
                },
                standings = series?.Standings.Select(s => new { s.PlayerId, s.Placement, s.Marbles, s.RoundsSurvived, s.Title }).ToList()
            }, _json);
        }

        // ── Message handlers (run on tick thread) ────────────────────────
        private void HandleText(Guid connId, string text)
        {
            if (!_conns.TryGetValue(connId, out var conn)) return;
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            string t = root.GetProperty("t").GetString();
            switch (t)
            {
                case "hello":
                    conn.Name = Str(root, "name", "Blob");
                    conn.CharacterId = Str(root, "characterId", "avo");
                    conn.PlayerId = "c_" + connId.ToString("N").Substring(0, 8);
                    Send(conn, JsonSerializer.Serialize(new { t = "welcome", playerId = conn.PlayerId }, _json));
                    break;

                case "createRoom":
                {
                    if (conn.PlayerId == null) { Error(conn, "say hello first"); break; }
                    var cfg = ConfigFrom(root);
                    var room = _rooms.CreateRoom(cfg);
                    room.AddPlayer(new Player(conn.PlayerId, conn.Name, conn.CharacterId, isBot: false));
                    conn.RoomCode = room.Code;
                    Send(conn, JsonSerializer.Serialize(new { t = "created", code = room.Code }, _json));
                    break;
                }

                case "joinRoom":
                {
                    if (conn.PlayerId == null) { Error(conn, "say hello first"); break; }
                    string code = Str(root, "code", "");
                    if (_rooms.JoinRoom(code, new Player(conn.PlayerId, conn.Name, conn.CharacterId, isBot: false)))
                        conn.RoomCode = code.ToUpperInvariant();
                    else Error(conn, "room not found or full");
                    break;
                }

                case "addBot":
                    _rooms.GetRoom(conn.RoomCode)?.AddBot();
                    break;

                case "startSeries":
                {
                    var room = _rooms.GetRoom(conn.RoomCode);
                    if (room != null && room.HostId == conn.PlayerId) room.StartSeries();
                    break;
                }

                case "leave":
                    _rooms.GetRoom(conn.RoomCode)?.RemovePlayer(conn.PlayerId);
                    conn.RoomCode = null;
                    break;
            }
        }

        private void HandleBinary(Guid connId, byte[] payload)
        {
            if (payload.Length < 1 || !_conns.TryGetValue(connId, out var conn) || conn.RoomCode == null) return;
            if (payload[0] != 1) return; // 1 = input
            var inner = new byte[payload.Length - 1];
            Buffer.BlockCopy(payload, 1, inner, 0, inner.Length);
            var input = Wire.DecodeInput(inner);
            _rooms.GetRoom(conn.RoomCode)?.HandleInput(conn.PlayerId, input);
        }

        private void Drop(Guid connId)
        {
            if (!_conns.TryGetValue(connId, out var conn)) return;
            if (conn.RoomCode != null) _rooms.GetRoom(conn.RoomCode)?.RemovePlayer(conn.PlayerId);
            conn.Out.Writer.TryComplete();
            _conns.Remove(connId);
        }

        private void Error(Conn conn, string msg)
            => Send(conn, JsonSerializer.Serialize(new { t = "error", msg }, _json));

        private static string Str(JsonElement e, string name, string fallback)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : fallback;

        private RoomConfig ConfigFrom(JsonElement root)
        {
            var cfg = new RoomConfig { BotFill = true };
            if (root.TryGetProperty("mode", out var m) && m.GetString() == "hardcore") cfg.Mode = SeriesMode.Hardcore;
            if (root.TryGetProperty("rounds", out var r) && r.ValueKind == JsonValueKind.Number) cfg.Rounds = RoundsMode.Fixed(r.GetInt32());
            return cfg;
        }
    }
}
