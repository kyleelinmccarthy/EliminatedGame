using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Eliminated.Server.Tests
{
    /// <summary>
    /// End-to-end networked smoke: a real <see cref="GameServer"/> over WebSockets,
    /// two real WebSocket clients, host-by-code, a full casual series driven to
    /// completion — proving the online protocol (control JSON + Wire input/snapshot
    /// frames) works over actual sockets. The server ticks as fast as possible so
    /// the whole series completes in a couple of seconds. This is the headless
    /// analogue of the reference game's networked smoke (server.ts + smoke.mjs).
    /// </summary>
    public class ServerSmokeTests
    {
        private sealed class ClientState
        {
            public readonly TaskCompletionSource<string> Code =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> Done =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public volatile string Phase;
            public volatile string Champion;
            public int SnapCount;
        }

        [Fact]
        public async Task Two_clients_play_a_full_series_over_websockets()
        {
            int port = 8137;
            var server = new GameServer(port, tickDelayMs: 0); // tick flat-out for the test
            server.Start();
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            ClientWebSocket a = null, b = null;
            try
            {
                a = await Connect(port, ct.Token);
                b = await Connect(port, ct.Token);
                var sa = new ClientState();
                var sb = new ClientState();
                _ = ReceiveLoop(a, sa, ct.Token);
                _ = ReceiveLoop(b, sb, ct.Token);

                // Host A creates a 2-round casual room, gets its code.
                await SendJson(a, "{\"t\":\"hello\",\"name\":\"Ana\",\"characterId\":\"fox\"}", ct.Token);
                await SendJson(a, "{\"t\":\"createRoom\",\"mode\":\"casual\",\"rounds\":2}", ct.Token);
                string code = await sa.Code.Task.WaitAsync(TimeSpan.FromSeconds(10), ct.Token);
                Assert.False(string.IsNullOrEmpty(code));

                // Client B joins by code.
                await SendJson(b, "{\"t\":\"hello\",\"name\":\"Ben\",\"characterId\":\"cat\"}", ct.Token);
                await SendJson(b, $"{{\"t\":\"joinRoom\",\"code\":\"{code}\"}}", ct.Token);

                // Host starts the series (bot-fill makes a full lobby).
                await Task.Delay(100, ct.Token);
                await SendJson(a, "{\"t\":\"startSeries\"}", ct.Token);

                // Both clients should ride the series to a crowned champion.
                await Task.WhenAll(sa.Done.Task, sb.Done.Task).WaitAsync(TimeSpan.FromSeconds(80), ct.Token);

                Assert.True(sa.SnapCount > 0, "host received no snapshots");
                Assert.True(sb.SnapCount > 0, "joiner received no snapshots");
                Assert.False(string.IsNullOrEmpty(sa.Champion), "no champion was crowned");
                Assert.Equal(sa.Champion, sb.Champion); // both clients agree on the winner
            }
            finally
            {
                if (a != null) try { await a.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                if (b != null) try { await b.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                server.Stop();
            }
        }

        private static async Task<ClientWebSocket> Connect(int port, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                var ws = new ClientWebSocket();
                try { await ws.ConnectAsync(new Uri($"ws://localhost:{port}/"), ct); return ws; }
                catch when (attempt < 20) { ws.Dispose(); await Task.Delay(100, ct); }
            }
        }

        private static Task SendJson(ClientWebSocket ws, string json, CancellationToken ct)
            => ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);

        private static async Task ReceiveLoop(ClientWebSocket ws, ClientState s, CancellationToken ct)
        {
            var buf = new byte[16 * 1024];
            var ms = new MemoryStream();
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(buf, ct);
                        if (r.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buf, 0, r.Count);
                    } while (!r.EndOfMessage);

                    if (r.MessageType == WebSocketMessageType.Binary)
                    {
                        if (ms.Length > 0 && ms.GetBuffer()[0] == 2) Interlocked.Increment(ref s.SnapCount);
                        continue;
                    }

                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
                    var root = doc.RootElement;
                    switch (root.GetProperty("t").GetString())
                    {
                        case "created":
                            s.Code.TrySetResult(root.GetProperty("code").GetString());
                            break;
                        case "room":
                            s.Phase = root.GetProperty("phase").GetString();
                            if (root.TryGetProperty("champion", out var ch) && ch.ValueKind == JsonValueKind.String)
                            {
                                s.Champion = ch.GetString();
                                if (s.Phase == "SeriesResult") s.Done.TrySetResult(true);
                            }
                            break;
                    }
                }
            }
            catch { }
        }
    }
}
