using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class CommandOutlineScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private readonly RigidTransform?[] _items = new RigidTransform?[4];
    private double _start = -1;
    private int _step;
    private CommandVisuals? _remoteVisuals;
    private RemoteCommander? _commander;

    public CommandOutlineScenario(ManualLogSource log)
    {
        _log = log;
        FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground = true;
    }

    public override void Tick(double now, ISession? session)
    {
        FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60);
        if (!HasLivePair(session)) return;
        if (_start < 0) _start = now;
        double t = now - _start;
        bool host = session is HostSession;
        if (!host && t > 4 && t < 12) FindItems();
        switch (_step)
        {
            case 0 when t > 3:
                _step++;
                if (host) SpawnItems();
                break;
            case 1 when t > 6:
                _step++;
                if (host)
                {
                    IDictionary commanders = Traverse.Create(HostCommandHub.Current).Field<IDictionary>("_commanders").Value;
                    _commander = commanders.Values.Cast<RemoteCommander>().First();
                    _remoteVisuals = Traverse.Create(_commander.Pickup).Field<CommandVisuals>("_visuals").Value;
                    _remoteVisuals.RefreshSelectionOutline(new[] { _items[0]! });
                    _remoteVisuals.RefreshPrimaryOutline(new List<CommandVisuals.PrimaryOutlineEntry>
                    {
                        GameApiCompatibility.CreatePrimaryOutlineEntry(_items[1]!, CommandVisuals.PrimaryOutlineKind.Primary),
                        GameApiCompatibility.CreatePrimaryOutlineEntry(_items[2]!, CommandVisuals.PrimaryOutlineKind.Dropoff),
                        GameApiCompatibility.CreatePrimaryOutlineEntry(_items[3]!, CommandVisuals.PrimaryOutlineKind.Construction),
                    });
                }
                break;
            case 2 when t > 12:
                _step++;
                if (host) Check("remote-stays-off-host", _items.All(item => !HasOutline(item)), "publication outside commander context");
                else
                {
                    Check("selected", HasOutline(_items[0], ColorFor("queuedPickupColor")), "selected item native color");
                    Check("pickup", HasOutline(_items[1], ColorFor("pickupHoverColor")), "primary pickup item");
                    Check("dropoff", HasOutline(_items[2], ColorFor("queuedPickupColor")), "manual dropoff item");
                    Check("construction", HasOutline(_items[3], ColorFor("constructionColor")), "construction delivery item");
                    CheckHoverSelection();
                }
                break;
            case 3 when t > 14:
                _step++;
                if (host) _remoteVisuals!.RefreshPrimaryOutline(new List<CommandVisuals.PrimaryOutlineEntry> { GameApiCompatibility.CreatePrimaryOutlineEntry(_items[1]!, CommandVisuals.PrimaryOutlineKind.Construction) });
                else Traverse.Create(World.commandService).Field<CommandVisuals>("_visuals").Value.ClearOutline();
                break;
            case 4 when t > 20:
                _step++;
                if (!host)
                {
                    Check("style-replaced", HasOutline(_items[1], ColorFor("constructionColor")) && !HasOutline(_items[2]) && !HasOutline(_items[3]), "same object changes job style, removed jobs clear");
                    Check("selection-survives-refresh", HasOutline(_items[0], ColorFor("queuedPickupColor")), "native UI clear and empty scheduler cannot erase authoritative selection");
                }
                break;
            case 5 when t > 22:
                _step++;
                if (host)
                {
                    HostRigidRegistry.RequireManager().Delete(_items[0]!);
                    HostRigidRegistry.RequireManager().Delete(_items[1]!);
                }
                break;
            case 6 when t > 28:
                _step++;
                if (!host) Check("despawn-clears", _items[0] == null && _items[1] == null && OutlineManager.Instance.Count == 0, "consumed selected materials do not leave outlines");
                break;
            case 7 when t > 30:
                _step++;
                if (host) _remoteVisuals!.ClearOutline();
                break;
            case 8 when t > 32:
                _step++;
                if (host)
                {
                    _remoteVisuals!.RefreshSelectionOutline(new[] { _items[2]! });
                    _remoteVisuals.RefreshPrimaryOutline(new List<CommandVisuals.PrimaryOutlineEntry> { GameApiCompatibility.CreatePrimaryOutlineEntry(_items[3]!, CommandVisuals.PrimaryOutlineKind.Dropoff) });
                }
                break;
            case 9 when t > 38:
                _step++;
                if (host) Check("async-stays-off-host", !HasOutline(_items[2]) && !HasOutline(_items[3]), "no CommanderContext needed");
                else Check("async-restores", HasOutline(_items[2], ColorFor("queuedPickupColor")) && HasOutline(_items[3], ColorFor("queuedPickupColor")), "later native callback restores current state");
                break;
            case 10 when t > 40:
                _step++;
                if (host) HostCommandHub.Current!.RemovePlayer(_commander!.PlayerId);
                break;
            case 11 when t > 42:
                _step++;
                if (host) _remoteVisuals!.RefreshSelectionOutline(new[] { _items[2]! });
                break;
            case 12 when t > 46:
                _step++;
                if (host) Check("disposed-callback-suppressed", !HasOutline(_items[2]), "late callback after remote disposal");
                else Check("remote-dispose-clears", !HasOutline(_items[2]) && !HasOutline(_items[3]), "empty player snapshot");
                break;
            case 13 when t > 48:
                _step++;
                if (host)
                {
                    CommandVisuals local = Traverse.Create(World.commandService).Field<CommandVisuals>("_visuals").Value;
                    local.RefreshSelectionOutline(new[] { _items[2]! });
                }
                break;
            case 14 when t > 54:
                _step++;
                if (host) Check("host-local-preserved", HasOutline(_items[2], ColorFor("queuedPickupColor")), "native host selection still works");
                else Check("host-stays-off-client", !HasOutline(_items[2]), "host selection is private");
                break;
        }
    }

    private void CheckHoverSelection()
    {
        if (_items[0] == null) { Check("hover-selection-style", false, "selected item missing"); return; }
        CommandVisuals visuals = Traverse.Create(World.commandService).Field<CommandVisuals>("_visuals").Value;
        Traverse fields = Traverse.Create(visuals);
        Camera original = fields.Field<Camera>("_camera").Value;
        var eye = new GameObject("FTMP outline diagnostic eye");
        Camera camera = eye.AddComponent<Camera>();
        camera.enabled = false;
        Vector3 center = RigidTransformApi.DisplayCenter(_items[0]!);
        eye.transform.SetPositionAndRotation(center - Vector3.forward * 3, Quaternion.identity);
        try
        {
            fields.Field("_camera").SetValue(camera);
            visuals.UpdateHoverOutline(Array.Empty<RigidTransform>());
            Check("hover-selection-style", fields.Field<bool>("_hoverUsesSelectionColor").Value && HasOutline(_items[0], ColorFor("queuedPickupColor") + Color.white * 0.5f), "native hover sees authoritative selected membership");
        }
        finally
        {
            fields.Field("_camera").SetValue(original);
            visuals.UpdateHoverOutline(Array.Empty<RigidTransform>());
            UnityEngine.Object.Destroy(eye);
        }
    }

    private void SpawnItems()
    {
        RigidTransformManager manager = World.rigidTransformManager;
        RigidPrefab prefab = Enumerable.Range(0, manager.PrefabCount).Select(i => RigidTransformApi.TryGetPrefab(manager, i, out RigidPrefab value) ? value : null)
            .First(p => p != null && RigidTransformApi.PrefabName(p) == "rt_log01")!;
        for (int i = 0; i < _items.Length; i++)
        {
            _items[i] = RigidTransformApi.Spawn(manager, prefab, ItemPosition(i), Quaternion.identity);
            _items[i]!.SetFrozen(true);
        }
    }

    private void FindItems()
    {
        RigidTransformManager manager = World.rigidTransformManager;
        for (int p = 0; p < RigidTransformApi.Count(manager); p++)
        {
            if (!RigidTransformApi.TryGetManagedRigid(manager, p, out RigidTransform rigid)) continue;
            for (int i = 0; i < _items.Length; i++) if (Vector3.Distance(rigid.position, ItemPosition(i)) < 0.5f) _items[i] = rigid;
        }
    }

    private static Vector3 ItemPosition(int i) => new Vector3(i * 4, 100, 40);
    private static Color ColorFor(string field) => Traverse.Create(World.commandService).Field<Color>(field).Value;
    private static bool HasOutline(RigidTransform? item, Color? color = null)
    {
        return GameApiCompatibility.HasOutline(item, color);
    }
    private void Check(string name, bool pass, string detail) => _log.LogInfo($"[OutlineTest] {(pass ? "PASS" : "FAIL")} {name} {detail}");
}
