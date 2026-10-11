using System;
using FamilyTimeMultiplayer.Core.Launch;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Transport.Relay;

namespace FamilyTimeMultiplayer.Core.Transport;

public sealed class JoinTarget
{
    private JoinTarget(string host, int port, string? roomCode)
    {
        Host = host;
        Port = port;
        RoomCode = roomCode;
    }

    public string Host { get; }
    public int Port { get; }
    public string? RoomCode { get; }
    public bool ViaRelay => RoomCode != null;

    public string Description => ViaRelay ? $"room {RoomCode} via {Host}:{Port}" : $"{Host}:{Port}";

    public static JoinTarget Parse(string typed, string? relayAddress)
    {
        if (Relay.RoomCode.TryNormalize(typed, out string code))
        {
            if (string.IsNullOrWhiteSpace(relayAddress))
            {
                throw new ArgumentException($"'{code}' is a room code, but no relay server is set (Settings -> Relay server).");
            }
            (string relayHost, int? relayPort) = LaunchOptions.ParseEndpoint(relayAddress!);
            return new JoinTarget(relayHost, relayPort ?? RelayWire.DefaultPort, code);
        }
        (string host, int? port) = LaunchOptions.ParseEndpoint(typed);
        return new JoinTarget(host, port ?? ProtocolConstants.DefaultPort, null);
    }

    public PendingConnect Start(TimeSpan timeout) =>
        RoomCode != null ? PendingConnect.StartViaRelay(Host, Port, RoomCode, timeout) : PendingConnect.Start(Host, Port, timeout);
}
