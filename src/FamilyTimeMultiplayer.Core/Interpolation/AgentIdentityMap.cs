using System;
using System.Collections.Generic;

namespace FamilyTimeMultiplayer.Core.Interpolation;

public sealed class AgentIdentityMap
{
    private readonly Dictionary<ushort, ushort> _hostToLocal = new Dictionary<ushort, ushort>();
    private readonly Dictionary<ushort, ushort> _localToHost = new Dictionary<ushort, ushort>();

    public int Count => _hostToLocal.Count;

    public IEnumerable<KeyValuePair<ushort, ushort>> HostToLocalPairs => _hostToLocal;

    public void Clear()
    {
        _hostToLocal.Clear();
        _localToHost.Clear();
    }

    public void Bind(ushort hostIndex, ushort localIndex)
    {
        if (_hostToLocal.TryGetValue(hostIndex, out ushort existingLocal))
        {
            if (existingLocal == localIndex)
            {
                return;
            }
            throw new InvalidOperationException(
                $"Host agent {hostIndex} is already bound to local agent {existingLocal}; refusing to rebind to {localIndex}.");
        }
        if (_localToHost.TryGetValue(localIndex, out ushort existingHost))
        {
            throw new InvalidOperationException(
                $"Local agent {localIndex} is already bound to host agent {existingHost}; refusing to bind host agent {hostIndex}.");
        }
        _hostToLocal.Add(hostIndex, localIndex);
        _localToHost.Add(localIndex, hostIndex);
    }

    public bool ContainsHost(ushort hostIndex) => _hostToLocal.ContainsKey(hostIndex);

    public bool TryGetLocal(ushort hostIndex, out ushort localIndex) => _hostToLocal.TryGetValue(hostIndex, out localIndex);

    public bool TryGetHost(ushort localIndex, out ushort hostIndex) => _localToHost.TryGetValue(localIndex, out hostIndex);

    public bool UnbindHost(ushort hostIndex, out ushort localIndex)
    {
        if (!_hostToLocal.TryGetValue(hostIndex, out localIndex))
        {
            return false;
        }
        _hostToLocal.Remove(hostIndex);
        _localToHost.Remove(localIndex);
        return true;
    }

    public bool UnbindLocal(ushort localIndex)
    {
        if (!_localToHost.TryGetValue(localIndex, out ushort hostIndex))
        {
            return false;
        }
        _localToHost.Remove(localIndex);
        _hostToLocal.Remove(hostIndex);
        return true;
    }
}
