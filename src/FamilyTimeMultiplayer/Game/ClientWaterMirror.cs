using System;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace FamilyTimeMultiplayer.Game;

public sealed class ClientWaterMirror : IDisposable
{
    private Texture2D? _upload;
    private WaterViewMessage? _sentView;
    public void Reset() => _sentView = null;
    public void Tick(Action<INetMessage> send)
    {
        WaterManager? water = World.waterManager;
        if (water == null || !water.IsWaterWindowInitialized) return;
        int x = water.WaterWindowOriginX, z = water.WaterWindowOriginZ;
        if (_sentView != null && _sentView.X == x && _sentView.Z == z) return;
        _sentView = new WaterViewMessage(x, z);
        send(_sentView);
    }
    public void Apply(WaterTilesMessage message)
    {
        WaterManager water = GatInterop.RequireAlive(World.waterManager, "Water manager is missing.");
        RenderTexture ledger = water.WaterLedgerTexture;
        bool narrow = ledger.graphicsFormat == GraphicsFormat.R8_UNorm;
        TextureFormat format = narrow ? TextureFormat.R8 : TextureFormat.R16;
        if (_upload == null || _upload.format != format)
        {
            if (_upload != null) UnityEngine.Object.Destroy(_upload);
            _upload = new Texture2D(32, 32, format, false, true);
        }
        foreach (WaterTile tile in message.Tiles)
        {
            var bytes = new byte[narrow ? 1024 : 2048];
            for (int i = 0; i < 1024; i++)
            {
                ushort level = tile.LevelAt(i);
                if (narrow) bytes[i] = (byte)((level + 128) / 257);
                else { bytes[2 * i] = (byte)level; bytes[2 * i + 1] = (byte)(level >> 8); }
            }
            _upload.LoadRawTextureData(bytes);
            _upload.Apply(false, false);
            Graphics.CopyTexture(_upload, 0, 0, 0, 0, 32, 32, ledger, 0, 0, tile.X + 2048, tile.Z + 2048);
        }
        water.RefreshSimulationWindowFromLedger();
        var windows = Traverse.Create(water).Field<RenderTexture[]>("waterWindow").Value;
        Graphics.CopyTexture(water.WaterWindow, windows[0] == water.WaterWindow ? windows[1] : windows[0]);
    }
    public void Dispose()
    {
        Reset();
        if (_upload != null) UnityEngine.Object.Destroy(_upload);
        _upload = null;
    }
}
