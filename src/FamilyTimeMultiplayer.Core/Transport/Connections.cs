using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Transport;

public interface INetConnection : IDisposable
{
    int Id { get; }

    string RemoteDescription { get; }

    bool IsOpen { get; }

    string? CloseReason { get; }

    int PendingSendFrames { get; }

    void Send(INetMessage message);

    bool TryReceive(out INetMessage message);

    void Close(string reason);
}

public interface INetListener : IDisposable
{
    bool TryAccept(out INetConnection connection);
}

public sealed class TcpNetConnection : INetConnection
{
    private static int _nextId;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly BoundedFrameQueue<byte[]> _outgoing = new BoundedFrameQueue<byte[]>();
    private readonly BoundedFrameQueue<INetMessage> _incoming;
    private int _admitted;
    private readonly bool _requiresClientAuthentication;
    private readonly Thread _readerThread;
    private readonly Thread _writerThread;
    private int _closed;
    private string? _closeReason;

    public TcpNetConnection(TcpClient client, string? description = null, bool awaitingHandshake = false)
    {
        _admitted = awaitingHandshake ? 0 : 1;
        _requiresClientAuthentication = awaitingHandshake;
        _incoming = awaitingHandshake ? new BoundedFrameQueue<INetMessage>(16, 64 * 1024) : new BoundedFrameQueue<INetMessage>();
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (awaitingHandshake) _client.ReceiveTimeout = 15000;
        _client.NoDelay = true;
        _stream = client.GetStream();
        Id = Interlocked.Increment(ref _nextId);
        RemoteDescription = description ?? client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = $"FTMP-Read-{Id}" };
        _writerThread = new Thread(WriteLoop) { IsBackground = true, Name = $"FTMP-Write-{Id}" };
        _readerThread.Start();
        _writerThread.Start();
    }

    public int Id { get; }

    public string RemoteDescription { get; }

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public string? CloseReason => Volatile.Read(ref _closeReason);

    public int PendingSendFrames => _outgoing.Count;

    public void Send(INetMessage message)
    {
        if (!IsOpen)
        {
            return;
        }
        byte[] payload = MessageCodec.Encode(message);
        if (!_outgoing.Put(payload, payload.Length, false)) Close("outgoing queue limit exceeded");
    }

    public void Admit()
    {
        if (!IsOpen) return;
        try
        {
            _incoming.SetLimits(1024, 32 * 1024 * 1024);
            _client.ReceiveTimeout = 0;
            Volatile.Write(ref _admitted, 1);
        }
        catch (Exception exception) when (exception is SocketException || exception is ObjectDisposedException)
        {
            Close($"admission failed: {exception.Message}");
        }
    }

    public bool TryReceive(out INetMessage message) => _incoming.Take(out message!, false);

    public void Close(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }
        Volatile.Write(ref _closeReason, reason);
        _outgoing.Close();
        _incoming.Close();
        try
        {
            _client.Client.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        _client.Close();
    }

    public void Dispose() => Close("disposed");

    private void ReadLoop()
    {
        try
        {
            var lengthBuffer = new byte[4];
            while (IsOpen)
            {
                if (!ReadExactly(lengthBuffer, 4))
                {
                    Close("remote closed the connection");
                    return;
                }
                int length = BitConverter.ToInt32(lengthBuffer, 0);
                int maximum = Volatile.Read(ref _admitted) == 0 ? 4096 : ProtocolConstants.MaxFrameBytes;
                if (length <= 0 || length > maximum)
                {
                    Close($"protocol error: frame length {length} is outside 1..{maximum}");
                    return;
                }
                var payload = new byte[length];
                if (!ReadExactly(payload, length))
                {
                    Close("remote closed the connection mid-frame");
                    return;
                }
                if (_requiresClientAuthentication)
                {
                    var type = (MessageType)payload[0];
                    bool admitted = Volatile.Read(ref _admitted) != 0;
                    if (!admitted && type != MessageType.Hello && type != MessageType.IdentityProof) { Close("protocol error: handshake message required"); return; }
                    if (admitted && type != MessageType.SignedClient) { Close("protocol error: signed client message required"); return; }
                }
                if (!_incoming.Put(MessageCodec.Decode(payload), length, true)) return;
            }
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException)
        {
            Close($"read failed: {exception.Message}");
        }
        catch (Exception exception) when (exception is InvalidDataException || exception is ArgumentException || exception is OverflowException)
        {
            Close($"protocol error: {exception.Message}");
        }
    }

    private void WriteLoop()
    {
        try
        {
            while (_outgoing.Take(out byte[] payload, true))
            {
                _stream.Write(BitConverter.GetBytes(payload.Length), 0, 4);
                _stream.Write(payload, 0, payload.Length);
            }
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException)
        {
            Close($"write failed: {exception.Message}");
        }
    }

    private bool ReadExactly(byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = _stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                return false;
            }
            offset += read;
        }
        return true;
    }
}

public sealed class TcpNetListener : INetListener
{
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<TcpClient> _accepted = new ConcurrentQueue<TcpClient>();
    private readonly object _acceptGate = new object();
    public const int MaxPendingAccepts = 8;
    private readonly Thread _acceptThread;
    private volatile bool _running = true;

    public TcpNetListener(IPAddress bindAddress, int port)
    {
        _listener = new TcpListener(bindAddress, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "FTMP-Accept" };
        _acceptThread.Start();
    }

    public int Port { get; }

    public bool TryAccept(out INetConnection connection)
    {
        lock (_acceptGate)
        {
            if (!_running || !_accepted.TryDequeue(out TcpClient client)) { connection = null!; return false; }
            connection = new TcpNetConnection(client, awaitingHandshake: true);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_acceptGate)
        {
            _running = false;
            _listener.Stop();
            while (_accepted.TryDequeue(out TcpClient client)) client.Close();
        }
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                TcpClient client = _listener.AcceptTcpClient();
                lock (_acceptGate)
                {
                    if (!_running || _accepted.Count >= MaxPendingAccepts) { client.Close(); continue; }
                    _accepted.Enqueue(client);
                }
            }
            catch (SocketException) when (!_running)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }
}

public sealed class PendingConnect
{
    private INetConnection? _connection;
    private string? _error;
    private int _completed;

    private PendingConnect()
    {
    }

    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    public INetConnection? Connection => Volatile.Read(ref _connection);

    public string? Error => Volatile.Read(ref _error);

    public static PendingConnect Start(string host, int port, TimeSpan timeout)
    {
        var pending = new PendingConnect();
        var thread = new Thread(() => pending.Run(host, port, timeout, roomCode: null)) { IsBackground = true, Name = "FTMP-Connect" };
        thread.Start();
        return pending;
    }

    public static PendingConnect StartViaRelay(string relayHost, int relayPort, string roomCode, TimeSpan timeout)
    {
        if (!Relay.RoomCode.TryNormalize(roomCode, out string code))
        {
            throw new ArgumentException($"'{roomCode}' is not a room code.", nameof(roomCode));
        }
        var pending = new PendingConnect();
        var thread = new Thread(() => pending.Run(relayHost, relayPort, timeout, code)) { IsBackground = true, Name = "FTMP-Connect" };
        thread.Start();
        return pending;
    }

    private void Run(string host, int port, TimeSpan timeout, string? roomCode)
    {
        var client = new TcpClient();
        try
        {
            if (!client.ConnectAsync(host, port).Wait(timeout))
            {
                client.Close();
                Volatile.Write(ref _error, $"timed out connecting to {host}:{port} after {timeout.TotalSeconds:0}s");
            }
            else if (roomCode == null)
            {
                Volatile.Write(ref _connection, new TcpNetConnection(client));
            }
            else
            {
                JoinRoom(client, roomCode);
            }
        }
        catch (AggregateException exception)
        {
            client.Close();
            Volatile.Write(ref _error, $"cannot connect to {host}:{port}: {exception.GetBaseException().Message}");
        }
        catch (SocketException exception)
        {
            client.Close();
            Volatile.Write(ref _error, $"cannot connect to {host}:{port}: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException)
        {
            client.Close();
            Volatile.Write(ref _error, $"relay {host}:{port} handshake failed: {exception.Message}");
        }
        Volatile.Write(ref _completed, 1);
    }

    private void JoinRoom(TcpClient client, string roomCode)
    {
        client.NoDelay = true;
        Relay.RelayWire.SetHandshakeTimeouts(client, enabled: true);
        NetworkStream stream = client.GetStream();
        Relay.RelayWire.WriteLine(stream, $"{Relay.RelayWire.Greeting} JOIN {roomCode}");
        string[] reply = Relay.RelayWire.Split(Relay.RelayWire.ReadLine(stream));
        if (reply.Length == 1 && reply[0] == "OK")
        {
            Relay.RelayWire.SetHandshakeTimeouts(client, enabled: false);
            Volatile.Write(ref _connection, new TcpNetConnection(client, $"room {roomCode}"));
            return;
        }
        client.Close();
        Volatile.Write(ref _error, $"room {roomCode}: {Relay.RelayHostListener.Describe(reply)}");
    }
}
