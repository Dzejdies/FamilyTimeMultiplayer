using System;

namespace FamilyTimeMultiplayer.Core.Launch;

public sealed class BackgroundExecutionScope : IDisposable
{
    private readonly Func<bool> _read;
    private readonly Action<bool> _write;
    private bool _active;
    private bool _wasEnabled;

    public BackgroundExecutionScope(Func<bool> read, Action<bool> write)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public void Update(bool required)
    {
        if (!required) Dispose();
        else if (!_active) Enable();
    }

    private void Enable()
    {
        _wasEnabled = _read();
        if (!_wasEnabled) _write(true);
        _active = true;
    }

    public void Dispose()
    {
        if (!_active) return;
        _active = false;
        if (!_wasEnabled && _read()) _write(false);
    }
}
