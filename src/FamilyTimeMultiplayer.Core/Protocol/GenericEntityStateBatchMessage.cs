using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyTimeMultiplayer.Core.Protocol;

public sealed class GenericEntityStateBatchMessage : INetMessage
{
    public const int MaximumEntries = 256;
    public const int MaximumPayloadBytes = 1024 * 1024;

    public GenericEntityStateBatchMessage(IEnumerable<GenericEntityStateMessage> states)
    {
        GenericEntityStateMessage[] copy = states.Take(MaximumEntries + 1).ToArray();
        if (copy.Length == 0 || copy.Length > MaximumEntries || copy.Sum(state => state.Payload.Length) > MaximumPayloadBytes)
            throw new InvalidDataException("Component state batch exceeds its entry or payload limit.");
        States = Array.AsReadOnly(copy);
    }

    public MessageType Type => MessageType.GenericEntityStateBatch;
    public IReadOnlyList<GenericEntityStateMessage> States { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(States.Count);
        foreach (GenericEntityStateMessage state in States) state.Write(writer);
    }

    public static GenericEntityStateBatchMessage Read(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count <= 0 || count > MaximumEntries) throw new InvalidDataException("Invalid component state batch size.");
        var states = new GenericEntityStateMessage[count];
        int payloadBytes = 0;
        for (int index = 0; index < count; index++)
        {
            states[index] = GenericEntityStateMessage.Read(reader);
            payloadBytes += states[index].Payload.Length;
            if (payloadBytes > MaximumPayloadBytes) throw new InvalidDataException("Component state batch exceeds its payload limit.");
        }
        return new GenericEntityStateBatchMessage(states);
    }

    public static IEnumerable<GenericEntityStateBatchMessage> CreateBatches(IEnumerable<GenericEntityStateMessage> states)
    {
        var pending = new List<GenericEntityStateMessage>();
        int bytes = 0;
        foreach (GenericEntityStateMessage state in states)
        {
            if (pending.Count == MaximumEntries || bytes + state.Payload.Length > MaximumPayloadBytes)
            {
                yield return new GenericEntityStateBatchMessage(pending);
                pending.Clear();
                bytes = 0;
            }
            pending.Add(state);
            bytes += state.Payload.Length;
        }
        if (pending.Count > 0) yield return new GenericEntityStateBatchMessage(pending);
    }
}
