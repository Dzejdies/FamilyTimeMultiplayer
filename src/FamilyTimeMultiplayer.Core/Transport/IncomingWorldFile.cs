using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Transport;

public sealed class IncomingWorldFile : IDisposable
{
    private readonly FileStream _stream;
    private readonly long _expectedLength;
    private uint _crc;
    private readonly string _temporaryPath;
    private bool _closed;
    private bool _completed;

    public IncomingWorldFile(string path, long expectedLength)
    {
        ProtocolConstants.RequireWorldLength(expectedLength);
        Path = path;
        _expectedLength = expectedLength;
        _temporaryPath = path + ".part";
        _stream = new FileStream(_temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    public string Path { get; }

    public long Received => _stream.Position;

    public float Progress => (float)Received / _expectedLength;

    public void Append(WorldChunkMessage chunk)
    {
        if (chunk.Offset != _stream.Position)
        {
            throw new InvalidDataException($"World chunk at offset {chunk.Offset} arrived, expected offset {_stream.Position}.");
        }
        if (chunk.Data.Length > _expectedLength - _stream.Position)
        {
            throw new InvalidDataException($"World chunk overruns the announced size of {_expectedLength} bytes.");
        }
        _stream.Write(chunk.Data, 0, chunk.Data.Length);
        _crc = Crc32.Append(_crc, chunk.Data, 0, chunk.Data.Length);
    }

    public void Complete(WorldCompleteMessage complete)
    {
        if (_stream.Position != _expectedLength)
        {
            throw new InvalidDataException($"World transfer ended at {_stream.Position} of {_expectedLength} bytes.");
        }
        if (complete.Crc32 != _crc)
        {
            throw new InvalidDataException($"World checksum mismatch: host {complete.Crc32:X8}, received {_crc:X8}.");
        }
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        _closed = true;
        if (File.Exists(Path)) File.Replace(_temporaryPath, Path, null);
        else File.Move(_temporaryPath, Path);
        _completed = true;
    }

    public void Dispose()
    {
        if (!_closed) { _stream.Dispose(); _closed = true; }
        if (!_completed && File.Exists(_temporaryPath)) File.Delete(_temporaryPath);
    }
}
