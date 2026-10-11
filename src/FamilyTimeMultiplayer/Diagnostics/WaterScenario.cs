using System;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class WaterScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private double _start = -1;
    private int _step;
    private readonly Vector2Int _cell = new Vector2Int(20, 20);
    public WaterScenario(ManualLogSource log)
    {
        _log = log;
        FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60);
        FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground = true;
    }
    public override void Tick(double now, ISession? session)
    {
        if (!HasLivePair(session)) return;
        if (_start < 0) _start = now;
        WaterManager water = World.waterManager;
        Traverse.Create(water).Field("simulationStep").SetValue(session!.Role == SessionRole.Host && now - _start > 55 && now - _start < 60 ? 0.1f : 3600f);

        double t = now - _start;
        switch (_step)
        {
            case 0 when t > 3:
                _step++;
                if (session!.Role == SessionRole.Host) water.RemoveWaterAtCell(_cell, 1);
                else
                {
                    Inject(water, _cell, 26214);
                    Read(water, _cell, amount => Check("ghost-seeded", amount == 26214, amount));
                }
                break;
            case 1 when t > 8:
                _step++;
                Read(water, _cell, amount => Check("ghost-cleared", amount == 0, amount));
                break;
            case 2 when t > 11:
                _step++;
                if (session!.Role == SessionRole.Host) water.SpawnWaterAtCell(_cell, 0.3f);
                break;
            case 3 when t > 17:
                _step++;
                Read(water, _cell, amount => Check("host-water-visible", Math.Abs(amount - 19661) <= 2, amount));
                break;
            case 4 when t > 20:
                _step++;
                if (session!.Role == SessionRole.Host) water.RemoveWaterAtCell(_cell, 1);
                break;
            case 5 when t > 26:
                _step++;
                Read(water, _cell, amount => Check("host-removal-visible", amount == 0, amount));
                break;
            case 6 when t > 27:
                _step++;
                if (session.Role == SessionRole.Host)
                {
                    water.SpawnWaterAtCell(_cell, 0.5f);
                    World.gatHungerBehavior.SetHungerType(HungerType.THIRST);
                    Handle child = Families.GameFamilyAccess.CurrentChildren().First();
                    if (World.gat.TryGetInstanceInfoImmediate(child, out GATInstanceInfo info)) World.gat.SetInstanceInfo(child, GatInterop.CreateInstanceInfo(info.info | (1u << 19)));
                }
                break;
            case 7 when t > 29:
                _step++;
                if (session.Role == SessionRole.Host) World.gatHungerBehavior.PerformDrink(Families.GameFamilyAccess.CurrentChildren().First(), _cell);
                break;
            case 8 when t > 30:
                _step++;
                if (session.Role == SessionRole.Host) Inject(water, new Vector2Int(130, 20), 13107, false);
                else
                {
                    Check("local-water-write-blocked", !water.RemoveWaterAtCell(_cell, 1), 0);
                    World.playerController.TeleportTo(LocalPlayer.Position + Vector3.right * 40, Vector3.forward);
                    Traverse.Create(session).Field<LocalPoseTracker>("_pose").Value.Reset();
                    Traverse.Create(session).Field("_poseInitialized").SetValue(false);
                }
                break;
            case 9 when t > 35:
                _step++;
                Read(water, _cell, amount => Check("shader-drink-visible", Math.Abs(amount - 32517) <= 2, amount));
                break;
            case 10 when t > 41:
                _step++;
                if (session.Role == SessionRole.Host) ReadLedger(water, new Vector2Int(130, 20), amount => Check("ledger-water-visible", amount == 13107, amount));
                else
                {
                    Check("different-window-origin", water.WaterWindowOriginX != -128, 0);
                    Read(water, new Vector2Int(130, 20), amount => Check("ledger-water-visible", amount == 13107, amount));
                }
                break;
            case 11 when t > 44:
                _step++;
                if (session.Role == SessionRole.Host)
                {
                    Inject(water, new Vector2Int(130, 20), 6553, false);
                }
                break;
            case 12 when t > 51:
                _step++;
                if (session.Role == SessionRole.Host) ReadLedger(water, new Vector2Int(130, 20), amount => Check("ledger-reduction", amount == 6553, amount));
                else Read(water, new Vector2Int(130, 20), amount => Check("ledger-reduction", amount == 6553, amount));
                break;
            case 13 when t > 54:
                _step++;
                if (session.Role == SessionRole.Host) water.SpawnWaterAtCell(new Vector2Int(31, 20), 0.8f);
                break;
            case 14 when t > 65:
                _step++;
                Read(water, new Vector2Int(31, 20), amount => Check("flow-level", amount < 52428, amount));
                break;
        }
    }
    private static void Inject(WaterManager water, Vector2Int cell, ushort level, bool visible = true)
    {
        var texture = new Texture2D(1, 1, TextureFormat.R16, false, true);
        texture.LoadRawTextureData(new byte[] { (byte)level, (byte)(level >> 8) });
        texture.Apply(false, false);
        if (visible) Graphics.CopyTexture(texture, 0, 0, 0, 0, 1, 1, water.WaterWindow, 0, 0, cell.x - water.WaterWindowOriginX, cell.y - water.WaterWindowOriginZ);
        Graphics.CopyTexture(texture, 0, 0, 0, 0, 1, 1, water.WaterLedgerTexture, 0, 0, cell.x + 2048, cell.y + 2048);
        UnityEngine.Object.Destroy(texture);
    }
    private static void ReadLedger(WaterManager water, Vector2Int cell, Action<ushort> receive)
    {
        AsyncGPUReadback.Request(water.WaterLedgerTexture, 0, cell.x + 2048, 1, cell.y + 2048, 1, 0, 1, TextureFormat.R16, request =>
        {
            if (request.hasError) throw new InvalidOperationException("Ledger probe readback failed.");
            receive(request.GetData<ushort>()[0]);
        });
    }
    internal static void Read(WaterManager water, Vector2Int cell, Action<ushort> receive)
    {
        int x = cell.x - water.WaterWindowOriginX;
        int z = cell.y - water.WaterWindowOriginZ;
        if (x < 0 || z < 0 || x >= 256 || z >= 256) throw new InvalidOperationException("Water probe is outside its window.");
        AsyncGPUReadback.Request(water.WaterWindow, 0, TextureFormat.R16, request =>
        {
            if (request.hasError) throw new InvalidOperationException("Water probe readback failed.");
            receive(request.GetData<ushort>()[z * 256 + x]);
        });
    }
    private void Check(string name, bool passed, ushort amount) => _log.LogInfo($"[WaterTest] {(passed ? "PASS" : "FAIL")} {name} level={amount}");
}
