using System.Collections.Generic;
using System.Threading;

namespace FamilyTimeMultiplayer.Core.Transport;

internal sealed class BoundedFrameQueue<T>
{
    private int _maxFrames;
    private int _maxBytes;
    private readonly Queue<(T Value, int Bytes)> _queue = new Queue<(T, int)>();
    private readonly object _gate = new object();
    private int _bytes;
    private bool _closed;
    public BoundedFrameQueue(int maxFrames = 1024, int maxBytes = 32 * 1024 * 1024) => SetLimits(maxFrames, maxBytes);
    public void SetLimits(int maxFrames, int maxBytes)
    {
        if (maxFrames <= 0 || maxBytes <= 0) throw new System.ArgumentOutOfRangeException(nameof(maxBytes));
        lock (_gate) { _maxFrames = maxFrames; _maxBytes = maxBytes; Monitor.PulseAll(_gate); }
    }
    public int Count { get { lock (_gate) return _queue.Count; } }

    public bool Put(T value, int bytes, bool wait)
    {
        lock (_gate)
        {
            if (bytes < 0 || bytes > _maxBytes) return false;
            while (!_closed && (_queue.Count >= _maxFrames || bytes > _maxBytes - _bytes))
            {
                if (!wait) return false;
                Monitor.Wait(_gate, 100);
            }
            if (_closed) return false;
            _queue.Enqueue((value, bytes));
            _bytes += bytes;
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public bool Take(out T value, bool wait)
    {
        lock (_gate)
        {
            while (wait && !_closed && _queue.Count == 0) Monitor.Wait(_gate, 100);
            if (_queue.Count == 0) { value = default!; return false; }
            var entry = _queue.Dequeue();
            _bytes -= entry.Bytes;
            value = entry.Value;
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _queue.Clear();
            _bytes = 0;
            Monitor.PulseAll(_gate);
        }
    }
}
