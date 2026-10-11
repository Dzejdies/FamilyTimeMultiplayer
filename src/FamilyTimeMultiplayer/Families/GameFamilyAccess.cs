using System;
using System.Collections.Generic;
using System.Reflection;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using UnityEngine;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Families;

public static class GameFamilyAccess
{
    private const int SpawnAttemptsPerChild = 32;

    private static readonly Func<CommandOwnershipService, Handle, bool>? NativeAvailableWorker = ResolveNativeAvailability();
    private static Func<CommandOwnershipService, Handle, bool>? ResolveNativeAvailability()
    {
        MethodInfo? method = typeof(CommandOwnershipService).GetMethod("IsAvailableWorker", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Handle) }, null);
        return method == null ? null : (Func<CommandOwnershipService, Handle, bool>)method.CreateDelegate(typeof(Func<CommandOwnershipService, Handle, bool>));
    }

    private static class LegacyAvailability
    {
        private static readonly Func<CommandOwnershipLedger, ushort, CommandOwnershipToken> LedgerOwner =
            AccessTools.MethodDelegate<Func<CommandOwnershipLedger, ushort, CommandOwnershipToken>>(
                AccessTools.Method(typeof(CommandOwnershipLedger), "GetOwner")
                ?? throw new MissingMethodException(nameof(CommandOwnershipLedger), "GetOwner"));

        private static readonly Func<CommandOwnershipLedger, ushort, bool> LedgerLocked =
            AccessTools.MethodDelegate<Func<CommandOwnershipLedger, ushort, bool>>(
                AccessTools.Method(typeof(CommandOwnershipLedger), "IsLocked")
                ?? throw new MissingMethodException(nameof(CommandOwnershipLedger), "IsLocked"));

        private static readonly AccessTools.FieldRef<CommandOwnershipService, CommandOwnershipLedger> ServiceLedger =
            AccessTools.FieldRefAccess<CommandOwnershipService, CommandOwnershipLedger>("_ledger");

        public static bool IsFree(CommandOwnershipService service, ushort stableIndex)
        {
            CommandOwnershipLedger ledger = ServiceLedger(service);
            return ledger == null || (!LedgerLocked(ledger, stableIndex) && LedgerOwner(ledger, stableIndex) == null);
        }
    }

    public static Singleplayer RequireSingleplayer() =>
        GatInterop.RequireAlive(World.singleplayer, "World.singleplayer is missing.");

    public static int ChildCountStart => Traverse.Create(RequireSingleplayer()).Field<int>("childCountStart").Value;

    public static float ChildGrowthMultiplier => Traverse.Create(RequireSingleplayer()).Field<float>("childGrowthMult").Value;

    public static int SpawnRadius => Traverse.Create(RequireSingleplayer()).Field<int>("radius").Value;

    public static int RequireChildTemplateIndex()
    {
        CommandOwnershipService ownership = GatInterop.RequireAlive(World.commandOwnershipService, "World.commandOwnershipService is missing.");
        int index = ownership.WorkerTemplateIndex;
        if (index < 0)
        {
            throw new InvalidOperationException("The child (worker) template is not registered in GAT.");
        }
        return index;
    }

    public static List<Handle> CurrentChildren()
    {
        GatSimulation gat = GatInterop.RequireAlive(World.gat, "World.gat is missing.");
        IReadOnlyList<GATTemplateEntry> entries = gat.GetTemplateEntries();
        GATTemplateEntry entry = entries[RequireChildTemplateIndex()];
        var children = new List<Handle>(entry.count);
        int end = Math.Min(gat.instanceCount, entry.startIndex + entry.count);
        for (int physical = Math.Max(0, entry.startIndex); physical < end; physical++)
        {
            children.Add(gat.GetHandle((ushort)physical));
        }
        return children;
    }

    public static (int Start, int End) ChildPhysicalRange()
    {
        GatSimulation gat = GatInterop.RequireAlive(World.gat, "World.gat is missing.");
        GATTemplateEntry entry = gat.GetTemplateEntries()[RequireChildTemplateIndex()];
        return (Math.Max(0, entry.startIndex), Math.Min(gat.instanceCount, entry.startIndex + entry.count));
    }

    public static bool IsFreeWorker(CommandOwnershipService service, ushort stableIndex)
    {
        return NativeAvailableWorker != null ? NativeAvailableWorker(service, GatInterop.ToHandle(stableIndex)) : LegacyAvailability.IsFree(service, stableIndex);
    }

    public static bool IsSleeping(ushort stableIndex)
    {
        GATStationManager? stations = World.gatStationManager;
        return stations != null && stations.IsHandleAssignedToSleepingStation(new Handle { stableIndex = stableIndex });
    }

    public static Handle[] SpawnChildrenNear(Vector3 center, int count, int radius)
    {
        if (count <= 0)
        {
            return Array.Empty<Handle>();
        }
        var matrices = new List<Matrix4x4>(count);
        for (int attempt = 0; attempt < count * SpawnAttemptsPerChild && matrices.Count < count; attempt++)
        {
            if (TryFindWalkableNear(center, radius, out Vector3 position))
            {
                matrices.Add(Matrix4x4.TRS(position, Quaternion.identity, Vector3.one));
            }
        }
        if (matrices.Count == 0)
        {
            throw new InvalidOperationException($"No walkable cell found within {radius} m of {center} to spawn {count} child(ren).");
        }
        GatSimulation gat = GatInterop.RequireAlive(World.gat, "World.gat is missing.");
        return gat.CreateInstances(matrices.ToArray(), RequireChildTemplateIndex());
    }

    public static bool TryFindWalkableNear(Vector3 center, int radius, out Vector3 position)
    {
        Traverse fields = Traverse.Create(RequireSingleplayer());
        NavManager navManager = GatInterop.RequireAlive(fields.Field<NavManager>("navManager").Value, "Singleplayer.navManager is missing.");
        Heightfield? heightfield = fields.Field<Heightfield>("heightfield").Value;
        int x = Mathf.FloorToInt(center.x) + UnityEngine.Random.Range(-radius, radius);
        int z = Mathf.FloorToInt(center.z) + UnityEngine.Random.Range(-radius, radius);
        int top = heightfield != null ? Mathf.Clamp(Mathf.FloorToInt(heightfield.GetWorldHeightAtQuadCenter(x, z)), 0, 255) : 0;
        for (int y = top; y >= 0; y--)
        {
            var cell = new Vector3Int(x, y, z);
            if (navManager.IsWalkableSurface(cell))
            {
                position = new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);
                return true;
            }
        }
        position = Vector3.zero;
        return false;
    }

    public static void AnimateGrowth(int grownBy, Action onComplete)
    {
        Traverse fields = Traverse.Create(RequireSingleplayer());
        ChildGrowthAnimation? prefab = fields.Field<ChildGrowthAnimation>("childGrowthAnimationPrefab").Value;
        Transform? parent = fields.Field<Transform>("childGrowthUIParent").Value;
        if (grownBy <= 0 || prefab == null)
        {
            onComplete();
            return;
        }
        UnityEngine.Object.Instantiate(prefab, parent, worldPositionStays: false).AnimateChildCounter(grownBy, onComplete);
    }
}
