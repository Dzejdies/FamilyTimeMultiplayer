using System;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Holds;
using FamilyTimeMultiplayer.Sessions;
using FamilyTimeMultiplayer.WorldEdits;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class SecurityScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private double _start = -1;
    private int _step;
    private GridKey _plan;
    private bool _rawRejected;
    private Vector2Int _treeCell;
    private byte _treeHeight;
    private RigidTransform? _item;
    private bool _checkedNativeHold;
    private bool _checkedNativeChop;
    public SecurityScenario(ManualLogSource log) { _log = log; QualitySettings.vSyncCount = 0; FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60); }
    public override void Tick(double now, ISession? session)
    {
        if (!HasLivePair(session))
        {
            if (_rawRejected && session is HostSession && _step == 3) { Check("raw-world-disconnected", session.Roster.Count == 1); _step++; }
            return;
        }
        if (_start < 0) _start = now;
        double t = now - _start;
        if (session is HostSession host)
        {
            if (!_checkedNativeHold && t > 12 && _item != null)
            {
                _checkedNativeHold = true;
                int player = host.Roster.First(entry => entry.PlayerId != PlayerRosterMessage.HostPlayerId).PlayerId;
                Check("client-native-hold-arrived", HostHoldRegistry.Current!.IsHeldBy(player, _item));
            }
            if (!_checkedNativeChop && t > 25)
            {
                _checkedNativeChop = true;
                Check("client-native-chop-consumed", _item == null);
            }
            if (_step == 0 && t > 3)
            {
                _step++;
                int player = host.Roster.First(entry => entry.PlayerId != PlayerRosterMessage.HostPlayerId).PlayerId;
                HostRigidRegistry rigids = Traverse.Create(host).Field<HostRigidRegistry>("_rigids").Value;
                ClientPoseMessage accepted = HostPlayerAuthority.RequirePose(player);
                Reject("pose-teleport", () => HostPlayerAuthority.AcceptPose(player, new ClientPoseMessage(new NetVector3(1500, 64, 1500), 0, 0, NetVector3.Zero)));
                Check("pose-origin-unchanged", HostPlayerAuthority.RequirePose(player).Position.Equals(accepted.Position));
                Reject("free-item-spawn", () => HostToolAuthority.Validate(player, new ToolEditsMessage(EditTool.Axe, new[] { WorldEdit.ItemSpawn(0, accepted.Position, NetQuaternion.Identity) }), rigids));
                Reject("free-water", () => HostToolAuthority.Validate(player, new ToolEditsMessage(EditTool.Hand, new[] { WorldEdit.WaterSpawn(0, 0, 1) }), rigids));
                Reject("outside-world-cell", () => HostToolAuthority.Validate(player, new ToolEditsMessage(EditTool.Shovel, new[] { WorldEdit.TerrainHeight(int.MaxValue, 0, 0) }), rigids));
                int x = Mathf.FloorToInt(accepted.Position.X), z = Mathf.FloorToInt(accepted.Position.Z);
                byte before = World.heightfield.GetRawHeight(x, z);
                Reject("batch-preflight", () => HostToolAuthority.Apply(player, "test", new ToolEditsMessage(EditTool.Shovel, new[] { WorldEdit.TerrainHeight(x, z, (byte)(before + 1)), WorldEdit.ItemDelete(1) }), rigids, _log, exception => throw exception));
                Check("batch-left-terrain-unchanged", World.heightfield.GetRawHeight(x, z) == before);
                var template = World.buildPartManager.BuildPartTemplates.Select((part, index) => (part, index)).First(entry => entry.part != null && entry.part.HasMesh && !entry.part.hideFromBlueprintTool && entry.part.rigidTransformPrefab == null);
                _plan = new GridKey(template.index, 7, 64, 1, 0);
                float overlap = Traverse.Create(World.playerController.Tools.OfType<BlueprintTool>().First()).Field<float>("overlapVolumePercent").Value;
                HostToolAuthority.Validate(player, new ToolEditsMessage(EditTool.Blueprint, new[] { WorldEdit.BuildPlanCreate(_plan, overlap) }), rigids);
                Check("nearby-blueprint-allowed", true);
                var commanders = Traverse.Create(Commands.HostCommandHub.Current!).Field<System.Collections.Generic.Dictionary<int, Commands.RemoteCommander>>("_commanders").Value;
                var commander = commanders[player];
                Reject("command-ground-distance", () => commander.Handle(CommandIntentMessage.InteractGround(new NetVector3(1500,64,1500)), rigids, _ => { }));
                RigidTransformManager manager = HostRigidRegistry.RequireManager();
                RigidPrefab asset = Enumerable.Range(0, manager.PrefabCount).Select(i => RigidTransformApi.TryGetPrefab(manager, i, out RigidPrefab prefab) ? prefab : null)
                    .First(prefab => prefab != null && RigidTransformApi.PrefabName(prefab).StartsWith("rt_log01"))!;
                RigidTransform item = RigidTransformApi.Spawn(manager, asset, GatInterop.ToUnity(accepted.Position) + Vector3.right, Quaternion.identity);
                item.SetFrozen(true);
                int netId = rigids.Register(item).NetId;
                var target = HoldTarget.Rigid(netId);
                var begin = new HoldMessage(target, HoldPhase.Begin, true, GatInterop.ToNet(item.position), NetQuaternion.Identity, NetVector3.Zero);
                Check("nearby-hold-allowed", HostHoldRegistry.Current!.TryBegin(player, "test", begin) == null);
                Vector3 at = item.position;
                Reject("hold-teleport", () => HostHoldRegistry.Current.Update(player, new HoldMessage(target, HoldPhase.Update, true, new NetVector3(1500,64,1500), NetQuaternion.Identity, NetVector3.Zero)));
                Check("hold-origin-unchanged", Vector3.Distance(item.position, at) < 0.01f);
                Reject("hold-release-velocity", () => HostHoldRegistry.Current.End(player, new HoldMessage(target, HoldPhase.End, false, GatInterop.ToNet(at), NetQuaternion.Identity, new NetVector3(10000,0,0))));
                HostHoldRegistry.Current.End(player, new HoldMessage(target, HoldPhase.End, false, GatInterop.ToNet(at), NetQuaternion.Identity, NetVector3.Zero));
                Check("legitimate-release-allowed", HostHoldRegistry.Current.HolderOf(item) == null);
                Check("nearby-hold-again-allowed", HostHoldRegistry.Current.TryBegin(player, "test", begin) == null);
                var release = new HoldMessage(target, HoldPhase.Settle, false, GatInterop.ToNet(at), NetQuaternion.Identity, NetVector3.Zero);
                HostHoldRegistry.Current.Settle(player, release);
                Reject("released-hold-teleport", () => HostHoldRegistry.Current.Update(player, new HoldMessage(target, HoldPhase.Update, false, GatInterop.ToNet(at + Vector3.right * 40), NetQuaternion.Identity, NetVector3.Zero)));
                HostHoldRegistry.Current.Update(player, new HoldMessage(target, HoldPhase.Update, false, GatInterop.ToNet(at), NetQuaternion.Identity, NetVector3.Zero));
                Check("released-stream-allowed", HostHoldRegistry.Current.IsHeldBy(player, item));
                Check("settling-regrab-allowed", HostHoldRegistry.Current.TryBegin(player, "test", begin) == null);
                HostHoldRegistry.Current.End(player, new HoldMessage(target, HoldPhase.End, false, GatInterop.ToNet(at), NetQuaternion.Identity, NetVector3.Zero));
                _treeCell = new Vector2Int(x + 3, z);
                _treeHeight = World.heightfield.GetRawHeight(_treeCell.x, _treeCell.y);
                ShovelTool shovel = World.playerController.Tools.OfType<ShovelTool>().First();
                EnvironmentMesh tree = Traverse.Create(shovel).Field<EnvironmentMesh[]>("collisionMeshes").Value.First(mesh => mesh != null);
                World.propChunkManager.AddProp(new Unity.Mathematics.uint2((uint)_treeCell.x, (uint)_treeCell.y), tree);
                _item = item;
            }
            if (_step == 1 && t > 8)
            {
                _step++;
                int player = host.Roster.First(entry => entry.PlayerId != PlayerRosterMessage.HostPlayerId).PlayerId;
                HostRigidRegistry rigids = Traverse.Create(host).Field<HostRigidRegistry>("_rigids").Value;
                HostToolAuthority.Apply(player, "test", new ToolEditsMessage(EditTool.Shovel, new[] { WorldEdit.TerrainHeight(_treeCell.x, _treeCell.y, (byte)(_treeHeight + 1)) }), rigids, _log, exception => throw exception);
            }
            if (_step == 2 && t > 20)
            {
                _step++;
                Check("client-blueprint-arrived", WorldEditRelay.FindPlan(_plan) != null);
                Check("shovel-tree-block-retained", World.heightfield.GetRawHeight(_treeCell.x, _treeCell.y) == _treeHeight);
                World.propChunkManager.RemoveProp(new Unity.Mathematics.uint2((uint)_treeCell.x, (uint)_treeCell.y));
                _rawRejected = true;
            }
        }
        else if (session is ClientSession client)
        {
            if (t > 18 && t < 22 && _item != null)
            {
                Vector3 delta = _item.position - LocalPlayer.Position;
                delta.y = 0;
                if (delta.magnitude > 1) World.playerController.TeleportTo(LocalPlayer.Position + delta.normalized * (2 * Mathf.Min(Time.unscaledDeltaTime, 0.1f)), delta.normalized);
            }
            if (_step == 0 && t > 8)
            {
                _step++;
                Vector3 pos = LocalPlayer.Position;
                var template = World.buildPartManager.BuildPartTemplates.Select((part, index) => (part, index)).First(entry => entry.part != null && entry.part.HasMesh && !entry.part.hideFromBlueprintTool && entry.part.rigidTransformPrefab == null);
                var key = new GridKey(template.index, 7, 64, 1, 0);
                float overlap = Traverse.Create(World.playerController.Tools.OfType<BlueprintTool>().First()).Field<float>("overlapVolumePercent").Value;
                WorldEditRelay.Forward(WorldEdit.BuildPlanCreate(key, overlap));
                RigidTransformManager manager = HostRigidRegistry.RequireManager();
                _item = Enumerable.Range(0, RigidTransformApi.Count(manager)).Select(i => RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform rigid) ? rigid : null)
                    .First(rigid => RigidTransformApi.IsAlive(rigid) && RigidTransformApi.Name(rigid).StartsWith("rt_log01"));
                HandActions.Grab(_item!);
                Check("client-native-grabbed", HandActions.IsHolding(_item!));
                Check("client-blueprint-sent", true);
            }
            if (_step == 1 && t > 16)
            {
                _step++;
                HandActions.Drop(_item!);
                _item!.SetFrozen(true);
                Check("client-native-dropped", !HandActions.IsHolding(_item!));
            }
            if (_step == 2 && t > 20) { _step++; Check("live-after-valid-actions", Commands.ClientCommandBridge.IsActive); }
            if (_step == 3 && t > 22)
            {
                _step++;
                AxeTool axe = World.playerController.Tools.OfType<AxeTool>().First();
                Traverse.Create(axe).Field("_currentTreeTarget").SetValue(new AxeTool.TreeTargetData { rigidTransform = _item, treeRootPos = _item!.position, closestChopPos = _item.position, treeTemplateIndex = -1 });
                AccessTools.Method(typeof(AxeTool), "ChopCurrentTree").Invoke(axe, null);
                Check("client-native-chop-sent", true);
            }
            if (_step == 4 && t > 29)
            {
                _step++;
                var connection = Traverse.Create(client).Field<Core.Transport.INetConnection>("_connection").Value;
                connection.Send(new WorldEditMessage(new[] { WorldEdit.ItemDelete(1) }));
                Check("raw-world-attack-sent", true);
            }
        }
    }
    private void Reject(string name, Action action)
    {
        try { action(); Check(name, false); }
        catch (InvalidDataException) { Check(name, true); }
    }
    private void Check(string name, bool success) => _log.LogInfo($"[Security] {(success ? "PASS" : "FAIL")} {name}");
}
