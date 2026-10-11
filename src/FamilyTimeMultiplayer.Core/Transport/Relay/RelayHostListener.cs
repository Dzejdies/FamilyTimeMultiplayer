using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace FamilyTimeMultiplayer.Core.Transport.Relay;

public enum RelayHostState
{
    Connecting,
    Ready,
    Failed,
}

public sealed class RelayHostListener : INetListener
{
    private readonly string _relayHost;
    private readonly int _relayPort;
    private readonly ConcurrentQueue<TcpClient> _accepted = new ConcurrentQueue<TcpClient>();
    private int _pendingAccepts;
    private readonly object _acceptGate = new object();
    private TcpClient? _control;
    private volatile bool _running = true;
    private string? _secret;
    private int _state = (int)RelayHostState.Connecting;
    private string? _code;
    private string? _error;

    public RelayHostListener(string relayHost, int relayPort)
    {
        _relayHost = relayHost;
        _relayPort = relayPort;
        new Thread(ControlLoop) { IsBackground = true, Name = "FTMPR-Host" }.Start();
    }

    public RelayHostState State => (RelayHostState)Volatile.Read(ref _state);

    public string? Code => Volatile.Read(ref _code);

    public string? Error => Volatile.Read(ref _error);

    public string Endpoint => $"{_relayHost}:{_relayPort}";

    public bool TryAccept(out INetConnection connection)
    {
        lock (_acceptGate)
        {
            if (!_running || !_accepted.TryDequeue(out TcpClient client)) { connection = null!; return false; }
            Interlocked.Decrement(ref _pendingAccepts);
            connection = new TcpNetConnection(client, $"relay {Code}", awaitingHandshake: true);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_acceptGate)
        {
            _running = false;
            _control?.Close();
            while (_accepted.TryDequeue(out TcpClient client)) { client.Close(); Interlocked.Decrement(ref _pendingAccepts); }
        }
    }

    private void ControlLoop()
    {
        try
        {
            TcpClient control = Connect();
            _control = control;
            NetworkStream stream = control.GetStream();
            RelayWire.WriteLine(stream, $"{RelayWire.Greeting} HOST");
            string[] room = RelayWire.Split(RelayWire.ReadLine(stream));
            if (room.Length != 3 || room[0] != "ROOM")
            {
                throw new InvalidDataException(Describe(room));
            }
            _secret = room[2];
            Volatile.Write(ref _code, room[1]);
            RelayWire.SetHandshakeTimeouts(control, enabled: false);
            control.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            Volatile.Write(ref _state, (int)RelayHostState.Ready);
            while (_running)
            {
                string[] words = RelayWire.Split(RelayWire.ReadLine(stream));
                if (words.Length == 2 && words[0] == "INCOMING")
                {
                    if (Interlocked.Increment(ref _pendingAccepts) > TcpNetListener.MaxPendingAccepts)
                    {
                        Interlocked.Decrement(ref _pendingAccepts);
                        continue;
                    }
                    string ticket = words[1];
                    new Thread(() => AcceptJoin(ticket)) { IsBackground = true, Name = "FTMPR-Accept" }.Start();
                }
            }
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is InvalidDataException || exception is ObjectDisposedException || exception is AggregateException)
        {
            if (_running)
            {
                Volatile.Write(ref _error, $"relay {Endpoint}: {exception.GetBaseException().Message}");
                Volatile.Write(ref _state, (int)RelayHostState.Failed);
            }
        }
    }

    private void AcceptJoin(string ticket)
    {
        TcpClient? client = null;
        bool queued = false;
        try
        {
            client = Connect();
            NetworkStream stream = client.GetStream();
            RelayWire.WriteLine(stream, $"{RelayWire.Greeting} ACCEPT {Code} {_secret} {ticket}");
            string reply = RelayWire.ReadLine(stream);
            if (reply != "OK")
            {
                client.Close();
                return;
            }
            RelayWire.SetHandshakeTimeouts(client, enabled: false);
            lock (_acceptGate)
            {
                if (!_running) { client.Close(); return; }
                _accepted.Enqueue(client);
                queued = true;
            }
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is InvalidDataException || exception is AggregateException)
        {
            client?.Close();
        }
        finally { if (!queued) Interlocked.Decrement(ref _pendingAccepts); }
    }

    private TcpClient Connect()
    {
        var client = new TcpClient();
        if (!client.ConnectAsync(_relayHost, _relayPort).Wait(RelayWire.HandshakeTimeout))
        {
            client.Close();
            throw new IOException($"timed out connecting after {RelayWire.HandshakeTimeout.TotalSeconds:0}s");
        }
        client.NoDelay = true;
        RelayWire.SetHandshakeTimeouts(client, enabled: true);
        return client;
    }

    internal static string Describe(string[] reply) =>
        reply.Length > 0 && reply[0] == "ERR" ? string.Join(" ", reply, 1, reply.Length - 1) : $"unexpected relay reply '{string.Join(" ", reply)}'";
}
