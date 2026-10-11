using System;
using System.Reflection;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Families;

public static class MamaCloning
{
    private static readonly MethodInfo ActiveInstanceSetter =
        AccessTools.PropertySetter(typeof(WolfMama), nameof(WolfMama.ActiveInstance))
        ?? throw new MissingMethodException(nameof(WolfMama), "set_ActiveInstance");

    private static readonly MethodInfo OnPositionsUpdated =
        AccessTools.Method(typeof(WolfMama), "OnPositionsUpdated")
        ?? throw new MissingMethodException(nameof(WolfMama), "OnPositionsUpdated");

    private static readonly int WolfMamaIndexId = Shader.PropertyToID("_WolfMamaIndex");

    private static readonly System.Collections.Generic.List<WolfMama> Clones = new System.Collections.Generic.List<WolfMama>();

    public static bool SuppressNewAgent { get; private set; }

    public static System.Collections.Generic.IReadOnlyList<WolfMama> LiveClones
    {
        get
        {
            Clones.RemoveAll(clone => clone == null);
            return Clones;
        }
    }

    public static WolfMama RequireOriginal() =>
        GatInterop.RequireAlive(WolfMama.ActiveInstance, "The scene has no active WolfMama to clone.");

    public static void SetActiveInstance(WolfMama mama) => ActiveInstanceSetter.Invoke(null, new object[] { mama });

    public static void RestoreOriginal(WolfMama original)
    {
        SetActiveInstance(original);
        Shader.SetGlobalInt(WolfMamaIndexId, WolfMama.GetHandlePhysicalIndex());
    }

    public static WolfMama CreateClone(WolfMama original, Vector3 position, string owner, bool createAgent)
    {
        var staging = new GameObject("ftmp_mama_staging");
        staging.SetActive(false);
        SuppressNewAgent = !createAgent;
        try
        {
            WolfMama clone = UnityEngine.Object.Instantiate(original, position, original.transform.rotation, staging.transform);
            clone.name = $"wolf_mama ({owner})";
            clone.transform.SetParent(original.transform.parent, worldPositionStays: true);
            Clones.Add(clone);
            return clone;
        }
        finally
        {
            SuppressNewAgent = false;
            UnityEngine.Object.Destroy(staging);
            RestoreOriginal(original);
        }
    }

    public static void DestroyKeepingAgent(WolfMama clone)
    {
        Traverse fields = Traverse.Create(clone);
        FakeGATManager? manager = fields.Field<FakeGATManager>("_manager").Value;
        Handle agent = clone.FakeGATHandle;
        if (manager != null && !agent.isNull)
        {
            manager.onPositionsUpdated -= (Action)Delegate.CreateDelegate(typeof(Action), clone, OnPositionsUpdated);
            manager.UnregisterHandle(agent);
        }
        fields.Field<Handle>("_fakeGATHandle").Value = Handle.Null;
        fields.Field<FakeGATManager>("_manager").Value = null!;
        (RigidTransformApi.FromMama(clone) ?? throw new MissingMemberException(nameof(WolfMama), "rigid transform")).SetFakeGATHandle(Handle.Null);
        Clones.Remove(clone);
        UnityEngine.Object.Destroy(clone.gameObject);
    }

    public static bool TryBindReplicatedAgent(WolfMama clone, Handle agent)
    {
        FakeGATManager manager = GatInterop.RequireAlive(World.fakeGATManager, "World.fakeGATManager is missing.");
        if (!clone.FakeGATHandle.isNull)
        {
            throw new InvalidOperationException($"{clone.name} already drives fake GAT agent {clone.FakeGATHandle}.");
        }
        if (!manager.RegisterHandle(agent))
        {
            return false;
        }
        Traverse fields = Traverse.Create(clone);
        fields.Field<Handle>("_fakeGATHandle").Value = agent;
        fields.Field<FakeGATManager>("_manager").Value = manager;
        (RigidTransformApi.FromMama(clone) ?? throw new MissingMemberException(nameof(WolfMama), "rigid transform")).SetFakeGATHandle(agent);
        manager.onPositionsUpdated += (Action)Delegate.CreateDelegate(typeof(Action), clone, OnPositionsUpdated);
        clone.RefreshFollowScreenIcon();
        return true;
    }
}
