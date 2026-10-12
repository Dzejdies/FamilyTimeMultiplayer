using System;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Commands;

public static class CommandLockState
{
    public static byte[] Encode(bool locked) => new[] { locked ? (byte)1 : (byte)0 };

    public static bool Decode(byte value) => value switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Command lock must be encoded as zero or one."),
    };

    public static bool TryValidate(ReadOnlyMemory<byte> payload, out string error)
    {
        error = payload.Length == 1 && payload.Span[0] <= 1 ? string.Empty : "Command lock state requires one canonical boolean byte.";
        return error.Length == 0;
    }
}
