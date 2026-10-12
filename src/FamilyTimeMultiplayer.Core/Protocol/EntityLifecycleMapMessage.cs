using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.Core.Protocol;

public sealed class EntityLifecycleMapMessage : INetMessage
{
    public const int MaximumBindings = ProtocolConstants.MaxAgents + ProtocolConstants.MaxRigids;

    public EntityLifecycleMapMessage(long generation, IEnumerable<EntityLifecycleBinding> bindings)
    {
        EntityLifecycleBinding[] copy = bindings.Take(MaximumBindings + 1).ToArray();
        if (generation < 1 || copy.Length > MaximumBindings || copy.Any(binding => binding.Identity.WorldGeneration != generation || binding.Revision == 0))
            throw new InvalidDataException("Invalid entity lifecycle snapshot.");
        if (copy.Select(binding => binding.Identity).Distinct().Count() != copy.Length)
            throw new InvalidDataException("Duplicate entity lifecycle snapshot binding.");
        WorldGeneration = generation;
        Bindings = Array.AsReadOnly(copy);
    }

    public MessageType Type => MessageType.EntityLifecycleMap;
    public long WorldGeneration { get; }
    public IReadOnlyList<EntityLifecycleBinding> Bindings { get; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(WorldGeneration);
        writer.Write(Bindings.Count);
        foreach (EntityLifecycleBinding binding in Bindings)
        {
            writer.Write(binding.Identity.Kind.Name);
            writer.Write(binding.Identity.NetworkId);
            writer.Write(binding.Revision);
        }
    }

    public static EntityLifecycleMapMessage Read(BinaryReader reader)
    {
        long generation = reader.ReadInt64();
        int count = reader.ReadInt32();
        if (generation < 1 || count < 0 || count > MaximumBindings) throw new InvalidDataException("Invalid entity lifecycle map size.");
        var bindings = new EntityLifecycleBinding[count];
        for (int index = 0; index < count; index++)
        {
            var kind = new EntityKind(ProtocolGuards.ReadText(reader, GenericEntityStateMessage.MaxEntityKindCharacters, "entity kind"));
            bindings[index] = new EntityLifecycleBinding(new NetworkEntityId(generation, kind, reader.ReadUInt64()), reader.ReadUInt64());
        }
        return new EntityLifecycleMapMessage(generation, bindings);
    }
}
