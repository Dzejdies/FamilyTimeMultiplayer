using System;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using UnityEngine;
using UnityEngine.Rendering;

namespace FamilyTimeMultiplayer.Game;

public sealed class HostWaterSampler : IDisposable
{
    private readonly ManualLogSource _log;
    private RenderTexture? _staging;
    private bool _pending;
    private bool _disposed;
    private int _generation;
    public HostWaterSampler(ManualLogSource log) { _log = log; }
    public void Reset() => _generation++;
    public bool Request(WaterViewMessage view, Action<ushort[]> complete)
    {
        WaterManager? water = World.waterManager;
        if (_disposed || _pending || water == null || !water.IsWaterWindowInitialized || World.worldSerializer.IsBusy) return false;
        RenderTexture ledger = water.WaterLedgerTexture;
        if (ledger == null || !ledger.IsCreated()) return false;
        if (_staging == null || _staging.graphicsFormat != ledger.graphicsFormat)
        {
            Release();
            var descriptor = ledger.descriptor;
            descriptor.width = descriptor.height = 256;
            _staging = new RenderTexture(descriptor) { name = "FTMP water staging" };
            _staging.Create();
        }
        RenderTexture staging = _staging;
        RenderTexture old = RenderTexture.active;
        RenderTexture.active = staging;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = old;
        Copy(ledger, -2048, -2048, staging, view.X, view.Z, -2048, -2048, 2048, 2048);
        Copy(water.WaterWindow, water.WaterWindowOriginX, water.WaterWindowOriginZ, staging, view.X, view.Z,
            water.WaterWindowOriginX, water.WaterWindowOriginZ, water.WaterWindowOriginX + 256, water.WaterWindowOriginZ + 256);
        int generation = _generation;
        _pending = true;
        AsyncGPUReadback.Request(staging, 0, TextureFormat.R16, request =>
        {
            try
            {
                if (_disposed || generation != _generation) return;
                if (request.hasError) { _log.LogWarning("[Water] GPU readback failed; retrying."); return; }
                var data = request.GetData<ushort>();
                if (data.Length != 256 * 256) throw new InvalidOperationException("Unexpected water readback size.");
                var levels = new ushort[data.Length];
                data.CopyTo(levels);
                complete(levels);
            }
            finally { _pending = false; if (_disposed) Release(); }
        });
        return true;
    }
    private static void Copy(Texture source, int sourceX, int sourceZ, Texture destination, int destinationX, int destinationZ, int minX, int minZ, int maxX, int maxZ)
    {
        minX = Math.Max(minX, destinationX); minZ = Math.Max(minZ, destinationZ);
        maxX = Math.Min(maxX, destinationX + 256); maxZ = Math.Min(maxZ, destinationZ + 256);
        if (minX >= maxX || minZ >= maxZ) return;
        Graphics.CopyTexture(source, 0, 0, minX - sourceX, minZ - sourceZ, maxX - minX, maxZ - minZ, destination, 0, 0, minX - destinationX, minZ - destinationZ);
    }
    public void Dispose() { _disposed = true; Reset(); if (!_pending) Release(); }
    private void Release()
    {
        if (_staging == null) return;
        _staging.Release();
        UnityEngine.Object.Destroy(_staging);
        _staging = null;
    }
}
