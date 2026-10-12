using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FamilyTimeMultiplayer.Core.Replication;

public sealed class AgentInfoComponentAdapter : IEntityComponentAdapter<object>
{
    public const string AdapterId = "familytime.agent-info";
    private readonly Dictionary<ushort, uint> _captured = new();
    private readonly Action<ushort, uint> _applyNativeInfo;

    public AgentInfoComponentAdapter(Action<ushort, uint> applyNativeInfo) =>
        _applyNativeInfo = applyNativeInfo ?? throw new ArgumentNullException(nameof(applyNativeInfo));

    public EntityComponentAdapterDescriptor Descriptor { get; } =
        new(AdapterId, 1, new EntityStateDomain("appearance"), new[] { new EntityKind("agent") });

    public void SetCapturedInfo(ushort localIndex, uint info) => _captured[localIndex] = info;
    public void Reset() => _captured.Clear();
    public void Forget(ushort localIndex) => _captured.Remove(localIndex);

    public byte[] Capture(object entity)
    {
        var payload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, _captured[NativeAgentIndex.Require(entity)]);
        return payload;
    }

    public bool TryValidate(ReadOnlyMemory<byte> state, out string error)
    {
        error = state.Length == sizeof(uint) ? string.Empty : "Agent info state must contain exactly four bytes.";
        return error.Length == 0;
    }

    public void Apply(object entity, int schemaVersion, ReadOnlyMemory<byte> state)
    {
        if (schemaVersion != Descriptor.SchemaVersion || !TryValidate(state, out _))
            throw new ArgumentException("Invalid agent info component state.", nameof(state));
        _applyNativeInfo(NativeAgentIndex.Require(entity), BinaryPrimitives.ReadUInt32LittleEndian(state.Span));
    }

}
