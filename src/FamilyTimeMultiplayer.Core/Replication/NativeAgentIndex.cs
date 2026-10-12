using System;

namespace FamilyTimeMultiplayer.Core.Replication;

internal static class NativeAgentIndex
{
    internal static ushort Require(object entity) => entity is ushort index
        ? index : throw new ArgumentException("Native agent state requires a bound agent index.", nameof(entity));
}
