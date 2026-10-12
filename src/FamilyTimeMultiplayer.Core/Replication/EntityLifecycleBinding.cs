using System;

namespace FamilyTimeMultiplayer.Core.Replication;

public readonly struct EntityLifecycleBinding
{
    public EntityLifecycleBinding(NetworkEntityId identity, ulong revision)
    {
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        Identity = identity;
        Revision = revision;
    }

    public NetworkEntityId Identity { get; }
    public ulong Revision { get; }
}
