// Online client. Guarded by the ELIMINATED_ONLINE scripting-define symbol so the
// default build (which has no networking packages required) is unaffected. Enable
// by adding ELIMINATED_ONLINE to Player → Scripting Define Symbols. It talks the
// same protocol as the verified headless server (server/Eliminated.Server) and the
// same Wire codec, so it also works against a Unity-Relay host that bridges to it.
// See docs/IMPLEMENTATION_GUIDE.md Phase 5.
#if ELIMINATED_ONLINE
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Net;

namespace Eliminated.Game.Net
{
    /// <summary>
    /// Connects to the authoritative server over WebSockets and exposes its
    /// snapshots/room-state through <see cref="ISnapshotSource"/>, so the existing
    /// ArenaView/LocalInputHub/TouchControls render and drive online play
    /// unchanged. (Per-game Snapshot.Data decoding for props/discrete HUDs is the
    /// one remaining TODO — blobs already render; see DecodeData note below.)
    /// </summary>
    public sealed class NetClient : MonoBehaviour, ISnapshotSource
    {
        [Serializable] private class Welcome { public string t, playerId; }
        [Serializable] private class Created { public string t, code; }
        [Serializable] private class RoomMsg { public string t, code, phase, game, youAre, champion; public int round; }

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly List<string> _localIds = new List<string>();
        private volatile RoomMsg _room;
        private volatile Snapshot _latest;

        public string PlayerId { get; private set; }
        public string LastCode { get; private set; }

        public bool HasSeries => _room != null;
        public RoomPhase Phase => Enum.TryParse<RoomPhase>(_room?.phase ?? "Lobby", out var p) ? p : RoomPhase.Lobby;
        public Snapshot Latest => _latest;
        public IReadOnlyList<string> LocalPlayerIds => _localIds;

        public async void Connect(string url, string name, string characterId)
        {
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(url), _cts.Token);
            _ = ReceiveLoop(_cts.Token);
            await SendText($"{{\"t\":\"hello\",\"name\":\"{Esc(name)}\",\"characterId\":\"{Esc(characterId)}\"}}");
        }

        public async void HostRoom(string mode, int rounds) => await SendText($"{{\"t\":\"createRoom\",\"mode\":\"{mode}\",\"rounds\":{rounds}}}");
        public async void JoinByCode(string code) => await SendText($"{{\"t\":\"joinRoom\",\"code\":\"{Esc(code)}\"}}");
        public async void AddBot() => await SendText("{\"t\":\"addBot\"}");
        public async void StartSeries() => await SendText("{\"t\":\"startSeries\"}");

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
        }

        private void HandleText(string json)
        {
            if (json.Contains("\"welcome\""))
            {
                var w = JsonUtility.FromJson<Welcome>(json);
                PlayerId = w.playerId;
                _localIds.Clear();
                _localIds.Add(PlayerId);
            }
            else if (json.Contains("\"created\"")) LastCode = JsonUtility.FromJson<Created>(json).code;
            else if (json.Contains("\"t\":\"room\"")) _room = JsonUtility.FromJson<RoomMsg>(json);
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
            // TODO: decode f.DataJson into the per-game Snapshot.Data type keyed by
            // f.Game (JsonUtility.FromJson<KothData>(...) etc.) so props/discrete HUDs
            // render online too. Blobs already render from the actors above.
            _latest = snap;
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
#endif
