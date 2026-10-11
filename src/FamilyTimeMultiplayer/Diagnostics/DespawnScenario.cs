using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using FamilyTimeMultiplayer.WorldEdits;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class DespawnScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private readonly Vector3 _position = new Vector3(0f, 100f, 80f);
    private double _start = -1;
    private RigidTransform? _material;
    private bool _spawned;
    private bool _seen;
    private bool _destroyed;
    private bool _checked;
    private readonly Vector3Int _planGrid = new Vector3Int(32, 100, 80);
    private readonly List<RigidTransform> _constructionMaterials = new List<RigidTransform>();
    private int _planTemplate;
    private int _materialCount;
    private bool _planSpawned;
    private bool _constructed;
    private bool _constructionChecked;

    public DespawnScenario(ManualLogSource log) => _log = log;

    public override void Tick(double now, ISession? session)
    {
        if (session == null || !HasLivePair(session))
        {
            return;
        }
        if (_start < 0)
        {
            _start = now;
            IReadOnlyList<BuildPart> templates = World.buildPartManager!.BuildPartTemplates;
            _planTemplate = Enumerable.Range(0, templates.Count)
                .Where(i => templates[i] != null && GameApiCompatibility.BuildPartMaterialCount(templates[i], ConstructionMaterial.LOG) > 0 && templates[i].rigidTransformPrefab == null)
                .OrderBy(i => GameApiCompatibility.BuildPartMaterialCount(templates[i], ConstructionMaterial.LOG))
                .First();
            _materialCount = GameApiCompatibility.BuildPartMaterialCount(templates[_planTemplate], ConstructionMaterial.LOG);
        }
        double elapsed = now - _start;
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        if (session.Role == SessionRole.Host && !_spawned && elapsed > 3)
        {
            RigidPrefab prefab = Enumerable.Range(0, manager.PrefabCount)
                .Select(i => RigidTransformApi.TryGetPrefab(manager, i, out RigidPrefab value) ? value : null)
                .First(p => p != null && RigidTransformApi.PrefabName(p) == "rt_log01")!;
            _material = RigidTransformApi.Spawn(manager, prefab, _position, Quaternion.identity);
            _material.SetFrozen(true);
            _spawned = true;
            _log.LogInfo("[DespawnTest] material spawned");
        }
        if (session.Role == SessionRole.Client && !_seen && elapsed > 4)
        {
            _material = Enumerable.Range(0, RigidTransformApi.Count(manager))
                .Select(i => RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform value) ? value : null)
                .FirstOrDefault(r => r != null && RigidTransformApi.IsAlive(r) && Vector3.Distance(r.position, _position) < 1f);
            if (RigidTransformApi.IsAlive(_material))
            {
                _seen = true;
                _log.LogInfo("[DespawnTest] client saw material");
            }
        }
        if (session.Role == SessionRole.Host && !_destroyed && elapsed > 10)
        {
            manager.Delete(_material!);
            _destroyed = true;
            _log.LogInfo("[DespawnTest] material consumed through direct Destroy (construction path)");
        }
        if (!_checked && elapsed > 16)
        {
            _checked = true;
            bool pass = !RigidTransformApi.IsAlive(_material) && (session.Role == SessionRole.Host || _seen);
            if (pass)
            {
                _log.LogInfo($"[DespawnTest] PASS {session.Role}: consumed material is gone");
            }
            else
            {
                _log.LogError($"[DespawnTest] FAIL {session.Role}: ghost={_material != null}, seen={_seen}");
            }
        }
        if (!_planSpawned && session.Role == SessionRole.Host && elapsed > 20)
        {
            _planSpawned = true;
            World.buildPlanManager!.CreateBuildPlan(_planTemplate, _planGrid, BuildPart.Rotation.R_0, 0.5f);
            RigidPrefab prefab = Enumerable.Range(0, manager.PrefabCount)
                .Select(i => RigidTransformApi.TryGetPrefab(manager, i, out RigidPrefab value) ? value : null)
                .First(p => p != null && RigidTransformApi.PrefabName(p) == "rt_log01")!;
            for (int i = 0; i < _materialCount; i++)
            {
                RigidTransform material = RigidTransformApi.Spawn(manager, prefab, (Vector3)_planGrid + new Vector3(2f + i * 2f, 4f, 0f), Quaternion.identity);
                material.SetFrozen(true);
                _constructionMaterials.Add(material);
            }
            _log.LogInfo($"[ConstructionTest] spawned {_materialCount} materials for {World.buildPartManager!.BuildPartTemplates[_planTemplate].name}");
        }
        if (session.Role == SessionRole.Client && elapsed > 20 && elapsed < 28)
        {
            for (int i = 0; i < RigidTransformApi.Count(manager); i++)
            {
                if (RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform material) && RigidTransformApi.IsAlive(material) &&
                    RigidTransformApi.Name(material).StartsWith("rt_log01") && Vector3.Distance(material.position, _planGrid) < _materialCount * 2f + 6f &&
                    !_constructionMaterials.Contains(material))
                {
                    _constructionMaterials.Add(material);
                }
            }
        }
        if (!_constructed && session.Role == SessionRole.Host && elapsed > 28)
        {
            _constructed = true;
            BuildPlanManager.BuildPlan plan = WorldEditRelay.FindPlan(WorldEditRelay.KeyOf(_planTemplate, _planGrid, BuildPart.Rotation.R_0))
                ?? throw new InvalidOperationException("Construction test plan is missing.");
            bool built = GameApiCompatibility.TryConstructBuildPlan(World.buildPlanManager!, plan, _constructionMaterials, _planGrid);
            if (!built)
            {
                throw new InvalidOperationException("Construction test could not consume its materials.");
            }
            _log.LogInfo("[ConstructionTest] built through TryConstructBuildPlan (same path as child construction)");
        }
        if (!_constructionChecked && elapsed > 35)
        {
            _constructionChecked = true;
            bool part = World.buildPartManager!.TryGetBuildPart(_planGrid, BuildPart.Rotation.R_0, out _);
            bool gone = _constructionMaterials.Count == _materialCount && _constructionMaterials.All(m => !RigidTransformApi.IsAlive(m));
            if (part && gone)
            {
                _log.LogInfo($"[ConstructionTest] PASS {session.Role}: part exists and {_materialCount} consumed materials are gone");
            }
            else
            {
                _log.LogError($"[ConstructionTest] FAIL {session.Role}: part={part} materialsGone={gone} observed={_constructionMaterials.Count}/{_materialCount}");
            }
        }
    }
}
