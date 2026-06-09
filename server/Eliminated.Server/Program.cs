using System;
using System.Threading;

namespace Eliminated.Server
{
    /// <summary>
    /// Console entry point for the headless authoritative server.
    /// Usage: <c>dotnet run -- [port]</c> (default 8080).
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8080;
            var server = new GameServer(port);
            server.Start();
            Console.WriteLine("Press Ctrl+C to stop.");
            var done = new ManualResetEventSlim();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; server.Stop(); done.Set(); };
            done.Wait();
        }
    }
}
