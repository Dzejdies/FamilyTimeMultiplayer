using System;

namespace FamilyTimeMultiplayer.UI;

internal sealed class WorldPauseScopeLease : IDisposable
{
    private IDisposable? _scope;

    public WorldPauseScopeLease(IDisposable scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public void Dispose()
    {
        IDisposable? scope = _scope;
        _scope = null;
        scope?.Dispose();
    }
}
