using System;
using System.Globalization;
using System.Net;
using System.Threading;
using FamilyTimeMultiplayer.Core.Transport.Relay;

namespace FamilyTimeMultiplayer.Relay;

public static class Program
{
    public static int Main(string[] args)
    {
        int port = RelayWire.DefaultPort;
        IPAddress bind = IPAddress.Any;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    port = int.Parse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture);
                    break;
                case "--bind" when i + 1 < args.Length:
                    bind = IPAddress.Parse(args[++i]);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: ftmp-relay [--port {RelayWire.DefaultPort}] [--bind 0.0.0.0]");
                    return 2;
            }
        }
        using var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Set();
        using var relay = new RelayServer(bind, port, line => Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z {line}"));
        stop.Wait();
        Console.WriteLine("relay stopping");
        return 0;
    }
}
