using System;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Water;

public sealed class WaterTileTracker
{
    private readonly Dictionary<(int, int), WaterTile> _last = new Dictionary<(int, int), WaterTile>();
    private WaterViewMessage? _view;
    public void Reset() { _last.Clear(); _view = null; }
    public WaterTilesMessage Changes(WaterViewMessage view, ushort[] levels)
    {
        if (levels.Length != 256 * 256) throw new ArgumentException("A water window contains 256 squared cells.", nameof(levels));
        if (_view == null || _view.X != view.X || _view.Z != view.Z) Reset();
        _view = view;
        var changes = new List<WaterTile>();
        for (int z = 0; z < 256; z += 32)
        {
            for (int x = 0; x < 256; x += 32)
            {
                int worldX = view.X + x, worldZ = view.Z + z;
                if (worldX < -2048 || worldZ < -2048 || worldX >= 2048 || worldZ >= 2048) continue;
                var data = new ushort[1024];
                for (int row = 0; row < 32; row++) Array.Copy(levels, (z + row) * 256 + x, data, row * 32, 32);
                bool changed = !_last.TryGetValue((worldX, worldZ), out WaterTile previous);
                for (int i = 0; i < data.Length && !changed; i++) changed = data[i] != previous!.LevelAt(i);
                if (!changed) continue;
                var tile = new WaterTile(worldX, worldZ, data);
                _last[(worldX, worldZ)] = tile;
                changes.Add(tile);
            }
        }
        return new WaterTilesMessage(changes);
    }
}
