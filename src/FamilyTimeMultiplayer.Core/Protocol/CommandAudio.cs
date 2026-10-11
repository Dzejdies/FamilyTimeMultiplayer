using System;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Protocol;

public enum CommandSound : byte
{
    Start = 1,
    Stop = 2,
    DropOff = 3,
    QueueAdd = 4,
    QueueRemove = 5,
    Construction = 6,
    Attack = 7,
    Station = 8,
    FollowStart = 9,
    FollowStop = 10,
    Lock = 11,
    Unlock = 12,
}

public sealed class CommandAudioMessage : INetMessage
{
    public CommandAudioMessage(CommandSound sound)
    {
        if (sound < CommandSound.Start || sound > CommandSound.Unlock)
        {
            throw new ArgumentOutOfRangeException(nameof(sound), sound, "Unknown command sound.");
        }
        Sound = sound;
    }

    public MessageType Type => MessageType.CommandAudio;
    public CommandSound Sound { get; }

    public void Write(BinaryWriter writer) => writer.Write((byte)Sound);

    public static CommandAudioMessage Read(BinaryReader reader)
    {
        var sound = (CommandSound)reader.ReadByte();
        if (sound < CommandSound.Start || sound > CommandSound.Unlock)
        {
            throw new InvalidDataException($"Unknown command sound {(byte)sound}.");
        }
        return new CommandAudioMessage(sound);
    }
}
