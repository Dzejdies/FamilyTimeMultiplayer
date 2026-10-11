using System.Collections.Generic;

namespace FamilyTimeMultiplayer.Core.Commands;

public sealed class PickupSelectionSweep
{
    private const int MaxTargetsPerSweep = 256;
    private readonly HashSet<int> _visited = new HashSet<int>();
    private bool _adds;

    public bool IsActive { get; private set; }

    public bool Begin(int rigidNetId, bool alreadySelected)
    {
        Stop();
        if (rigidNetId < 0) return false;
        IsActive = true;
        _adds = !alreadySelected;
        _visited.Add(rigidNetId);
        return true;
    }

    public bool TryVisit(int rigidNetId, bool alreadySelected)
    {
        if (!IsActive || rigidNetId < 0 || _visited.Count >= MaxTargetsPerSweep || !_visited.Add(rigidNetId)) return false;
        return _adds ? !alreadySelected : alreadySelected;
    }

    public void Stop()
    {
        IsActive = false;
        _visited.Clear();
    }
}
