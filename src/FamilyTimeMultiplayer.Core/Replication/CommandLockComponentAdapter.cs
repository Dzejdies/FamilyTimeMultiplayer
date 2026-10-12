using System;
using FamilyTimeMultiplayer.Core.Commands;

namespace FamilyTimeMultiplayer.Core.Replication;

public sealed class CommandLockComponentAdapter : IEntityComponentAdapter<object>
{
    public const string AdapterId = "familytime.command-lock";
    private readonly Func<ushort, bool> _readNativeLock;
    private readonly Action<ushort, bool> _applyNativeLock;

    public CommandLockComponentAdapter(Func<ushort, bool> readNativeLock, Action<ushort, bool> applyNativeLock)
    {
        _readNativeLock = readNativeLock ?? throw new ArgumentNullException(nameof(readNativeLock));
        _applyNativeLock = applyNativeLock ?? throw new ArgumentNullException(nameof(applyNativeLock));
    }

    public EntityComponentAdapterDescriptor Descriptor { get; } =
        new(AdapterId, 1, new EntityStateDomain("command-lock"), new[] { new EntityKind("agent") });

    public byte[] Capture(object entity) => CommandLockState.Encode(_readNativeLock(NativeAgentIndex.Require(entity)));

    public bool TryValidate(ReadOnlyMemory<byte> state, out string error) => CommandLockState.TryValidate(state, out error);

    public void Apply(object entity, int schemaVersion, ReadOnlyMemory<byte> state)
    {
        if (schemaVersion != Descriptor.SchemaVersion || !TryValidate(state, out _))
            throw new ArgumentException("Invalid command lock component state.", nameof(state));
        _applyNativeLock(NativeAgentIndex.Require(entity), CommandLockState.Decode(state.Span[0]));
    }
}
