using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class MamaProbeScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private double _at = -1;
    private bool _done;

    public MamaProbeScenario(ManualLogSource log)
    {
        _log = log;
    }

    public override void Tick(double now, ISession? session)
    {
        if (_done || !LocalPlayer.IsWorldReady)
        {
            return;
        }
        if (_at < 0)
        {
            _at = now + 5;
        }
        if (now < _at)
        {
            return;
        }
        _done = true;
        WolfMama[] all = Object.FindObjectsByType<WolfMama>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        _log.LogInfo($"[Mama] {all.Length} WolfMama object(s); active={WolfMama.ActiveInstance?.name}");
        foreach (WolfMama mama in all)
        {
            Transform t = mama.transform;
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                path = p.name + "/" + path;
            }
            RigidTransform? rigid = RigidTransformApi.FromMama(mama);
            RigidTransformManager? manager = World.rigidTransformManager;
            bool managed = manager != null && RigidTransformApi.IsAlive(rigid) && manager.TryGetManagedIndex(rigid!, out int managedIndex) &&
                RigidTransformApi.TryGetManagedRigid(manager, managedIndex, out _, out int prefabIndex) && Log(managedIndex, prefabIndex);
            Handle fake = mama.FakeGATHandle;
            Template? template = fake.isNull || World.gat == null ? null : World.gat.GetTemplateFromHandle(fake);
            _log.LogInfo($"[Mama] '{path}' scene='{mama.gameObject.scene.name}' active={mama.isActiveAndEnabled} managedRigid={managed} fake={fake} template={template?.name} faction={template?.faction} allowFollow={mama.AllowFollow}");
            _log.LogInfo($"[Mama]   components: {string.Join(", ", mama.GetComponents<Component>().Select(c => c.GetType().Name))}");
            _log.LogInfo($"[Mama]   children: {string.Join(", ", mama.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct())}");
        }
        FakeGATManager? fakes = World.fakeGATManager;
        _log.LogInfo($"[Mama] fake GAT manager default template={(fakes == null ? "none" : HarmonyLib.Traverse.Create(fakes).Field<Template>("defaultTemplate").Value?.name)}");
    }

    private bool Log(int managedIndex, int prefabIndex)
    {
        if (!RigidTransformApi.TryGetPrefab(World.rigidTransformManager!, prefabIndex, out RigidPrefab prefab)) return false;
        _log.LogInfo($"[Mama]   managed index {managedIndex}, prefab #{prefabIndex} '{RigidTransformApi.PrefabName(prefab)}'");
        return true;
    }
}
