using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace FamilyTimeMultiplayer.Core.Transport.Relay;

public static class RelayWire
{
    public const string Greeting = "FTMPR1";
    public const int DefaultPort = 27780;
    public const int MaxLineBytes = 200;
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

    public static void WriteLine(NetworkStream stream, string line)
    {
        if (line.IndexOf('\n') >= 0 || Encoding.ASCII.GetByteCount(line) >= MaxLineBytes)
        {
            throw new ArgumentException($"Relay line '{line}' is too long or contains a newline.", nameof(line));
        }
        byte[] bytes = Encoding.ASCII.GetBytes(line + "\n");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public static string ReadLine(NetworkStream stream)
    {
        var line = new StringBuilder();
        while (true)
        {
            int next = stream.ReadByte();
            if (next < 0)
            {
                throw new EndOfStreamException("The relay connection closed during the handshake.");
            }
            if (next == '\n')
            {
                return line.ToString();
            }
            if (next < 0x20 || next > 0x7E)
            {
                throw new InvalidDataException($"Relay handshake contains a non-printable byte 0x{next:X2}.");
            }
            line.Append((char)next);
            if (line.Length >= MaxLineBytes)
            {
                throw new InvalidDataException($"Relay handshake line exceeds {MaxLineBytes} bytes.");
            }
        }
    }

    public static string[] Split(string line) => line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    public static void SetHandshakeTimeouts(TcpClient client, bool enabled)
    {
        int ms = enabled ? (int)HandshakeTimeout.TotalMilliseconds : 0;
        client.ReceiveTimeout = ms;
        client.SendTimeout = ms;
    }
}
