using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace FamilyTimeMultiplayer.Core.Transport.Relay;

public sealed class RelayServer : IDisposable
{
    public const int MaxRooms = 512;
    public const int MaxConnections = 128;
    public const int MaxPendingJoinsPerRoom = 8;
    public static readonly TimeSpan HostAnswerTimeout = TimeSpan.FromSeconds(12);

    private sealed class Room
    {
        public Room(string code, string secret, TcpClient control)
        {
            Code = code;
            Secret = secret;
            Control = control;
        }

        public string Code { get; }
        public string Secret { get; }
        public TcpClient Control { get; }
        public object ControlWriteLock { get; } = new object();
        public int PendingJoins;
    }

    private sealed class PendingJoin
    {
        public PendingJoin(Room room, TcpClient client)
        {
            Room = room;
            Client = client;
        }

        public Room Room { get; }
        public TcpClient Client { get; }
        public ManualResetEventSlim Paired { get; } = new ManualResetEventSlim(false);
    }

    private readonly TcpListener _listener;
    private readonly Action<string> _log;
    private readonly object _lock = new object();
    private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingJoin> _pending = new Dictionary<string, PendingJoin>(StringComparer.Ordinal);
    private readonly HashSet<TcpClient> _connections = new HashSet<TcpClient>();
    private volatile bool _running = true;

    public RelayServer(IPAddress bindAddress, int port, Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _listener = new TcpListener(bindAddress, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        new Thread(AcceptLoop) { IsBackground = true, Name = "FTMPR-Accept" }.Start();
        _log($"relay listening on {bindAddress}:{Port}");
    }

    public int Port { get; }

    public int RoomCount
    {
        get
        {
            lock (_lock)
            {
                return _rooms.Count;
            }
        }
    }

    internal int ActiveConnectionCount
    {
        get
        {
            lock (_lock)
            {
                return _connections.Count;
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _listener.Stop();
        lock (_lock)
        {
            foreach (Room room in _rooms.Values)
            {
                room.Control.Close();
            }
            foreach (PendingJoin join in _pending.Values)
            {
                join.Client.Close();
            }
            foreach (TcpClient connection in _connections)
            {
                connection.Close();
            }
            _rooms.Clear();
            _pending.Clear();
        }
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (SocketException) when (!_running)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            StartConnection(client);
        }
    }

    private void StartConnection(TcpClient client)
    {
        lock (_lock)
        {
            if (!_running || _connections.Count >= MaxConnections)
            {
                client.Close();
                return;
            }
            _connections.Add(client);
        }
        try
        {
            new Thread(() => Handle(client)) { IsBackground = true, Name = "FTMPR-Conn" }.Start();
        }
        catch
        {
            ReleaseConnection(client);
            client.Close();
            throw;
        }
    }

    private void ReleaseConnection(TcpClient client)
    {
        lock (_lock)
        {
            _connections.Remove(client);
        }
        client.Close();
    }

    private void Handle(TcpClient client)
    {
        string peer = client.Client.RemoteEndPoint?.ToString() ?? "?";
        bool connectionTransferred = false;
        try
        {
            client.NoDelay = true;
            RelayWire.SetHandshakeTimeouts(client, enabled: true);
            NetworkStream stream = client.GetStream();
            string[] words = RelayWire.Split(RelayWire.ReadLine(stream));
            if (words.Length < 2 || words[0] != RelayWire.Greeting)
            {
                Refuse(client, "not a Family Time Multiplayer relay request");
                return;
            }
            switch (words[1])
            {
                case "HOST" when words.Length == 2:
                    ServeHost(client, stream, peer);
                    break;
                case "JOIN" when words.Length == 3:
                    connectionTransferred = ServeJoin(client, stream, words[2], peer);
                    break;
                case "ACCEPT" when words.Length == 5:
                    ServeAccept(client, stream, words[2], words[3], words[4]);
                    break;
                default:
                    Refuse(client, $"unknown request '{words[1]}'");
                    break;
            }
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is InvalidDataException || exception is ObjectDisposedException)
        {
            _log($"{peer}: {exception.Message}");
        }
        finally
        {
            if (!connectionTransferred)
            {
                ReleaseConnection(client);
            }
        }
    }

    private void ServeHost(TcpClient control, NetworkStream stream, string peer)
    {
        Room room;
        lock (_lock)
        {
            if (_rooms.Count >= MaxRooms)
            {
                Refuse(control, "relay is full, try again later");
                return;
            }
            string code;
            do
            {
                code = RoomCode.Generate();
            }
            while (_rooms.ContainsKey(code));
            room = new Room(code, RoomCode.NewSecret(), control);
            _rooms.Add(code, room);
        }
        _log($"room {room.Code} allocated for {peer}");
        try
        {
            control.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            lock (room.ControlWriteLock)
            {
                RelayWire.WriteLine(stream, $"ROOM {room.Code} {room.Secret}");
            }
            RelayWire.SetHandshakeTimeouts(control, enabled: false);
            _log($"room {room.Code} opened by {peer}");
            while (stream.ReadByte() >= 0)
            {
            }
        }
        catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException)
        {
        }
        finally
        {
            CloseRoom(room);
        }
    }

    private void CloseRoom(Room room)
    {
        lock (_lock)
        {
            if (_rooms.TryGetValue(room.Code, out Room current) && ReferenceEquals(current, room))
            {
                _rooms.Remove(room.Code);
            }
        }
        room.Control.Close();
        _log($"room {room.Code} closed");
    }

    private bool ServeJoin(TcpClient client, NetworkStream stream, string requestedCode, string peer)
    {
        if (!RoomCode.TryNormalize(requestedCode, out string code))
        {
            Refuse(client, $"'{requestedCode}' is not a room code");
            return false;
        }
        string ticket = RoomCode.NewSecret();
        PendingJoin join;
        lock (_lock)
        {
            if (!_rooms.TryGetValue(code, out Room room))
            {
                Refuse(client, $"no room {code} (the host may have stopped)");
                return false;
            }
            if (room.PendingJoins >= MaxPendingJoinsPerRoom)
            {
                Refuse(client, $"room {code} has too many players joining at once");
                return false;
            }
            room.PendingJoins++;
            join = new PendingJoin(room, client);
            _pending.Add(ticket, join);
        }
        try
        {
            lock (join.Room.ControlWriteLock)
            {
                RelayWire.WriteLine(join.Room.Control.GetStream(), $"INCOMING {ticket}");
            }
            if (!join.Paired.Wait(HostAnswerTimeout))
            {
                Refuse(client, $"the host of {code} did not answer");
                return false;
            }
            else
            {
                _log($"{peer} joined room {code}");
                return true;
            }
        }
        finally
        {
            lock (_lock)
            {
                _pending.Remove(ticket);
                join.Room.PendingJoins--;
            }
        }
    }

    private void ServeAccept(TcpClient hostSide, NetworkStream hostStream, string code, string secret, string ticket)
    {
        PendingJoin? join;
        lock (_lock)
        {
            if (!_pending.TryGetValue(ticket, out join) || join.Room.Code != code || join.Room.Secret != secret)
            {
                join = null;
            }
            else
            {
                _pending.Remove(ticket);
            }
        }
        if (join == null)
        {
            Refuse(hostSide, "unknown or expired join ticket");
            return;
        }
        NetworkStream clientStream = join.Client.GetStream();
        RelayWire.WriteLine(hostStream, "OK");
        RelayWire.WriteLine(clientStream, "OK");
        RelayWire.SetHandshakeTimeouts(hostSide, enabled: false);
        RelayWire.SetHandshakeTimeouts(join.Client, enabled: false);
        join.Paired.Set();
        Pipe(join.Client, hostSide);
    }

    private void Pipe(TcpClient a, TcpClient b)
    {
        void Copy(TcpClient from, TcpClient to)
        {
            try
            {
                from.GetStream().CopyTo(to.GetStream(), 64 * 1024);
            }
            catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException || exception is InvalidOperationException)
            {
            }
            finally
            {
                a.Close();
                b.Close();
            }
        }
        try
        {
            new Thread(() => Copy(a, b)) { IsBackground = true, Name = "FTMPR-Pipe" }.Start();
            Copy(b, a);
        }
        finally
        {
            ReleaseConnection(a);
        }
    }

    private static void Refuse(TcpClient client, string reason)
    {
        try
        {
            RelayWire.WriteLine(client.GetStream(), $"ERR {reason}");
        }
        catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException || exception is InvalidOperationException)
        {
        }
        client.Close();
    }
}
