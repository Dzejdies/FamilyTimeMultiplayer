using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class OrdersScenario : ScenarioRunner
{
    private static readonly Vector3Int BedGrid = new Vector3Int(8, 64, 8);
    private static readonly Vector3Int PlanGrid = new Vector3Int(-8, 64, 8);

    private readonly ManualLogSource _log;
    private readonly List<(double At, bool Host, string Name, Action Step)> _steps = new List<(double, bool, string, Action)>();
    private double _start = -1;
    private int _next;
    private double _nextProbe;

    public OrdersScenario(ManualLogSource log)
    {
        _log = log;
        _steps.Add((3, true, "host builds a bed", BuildBed));
        _steps.Add((4, true, "host lays out a plan with logs next to it", PlanWithLogs));
        _steps.Add((10, false, "client orders kids to pick up the log", PickUpPlanLog));
        _steps.Add((22, false, "client orders the kids to build it", OrderBuild));
        _steps.Add((45, false, "client sends kids to bed", SendKidsToBed));
        _steps.Add((60, false, "client locks one of their kids", LockKid));
        _steps.Add((64, false, "client attacks the nearest animal", AttackAnimal));
    }

    public override void Tick(double now, ISession? session)
    {
        if (session == null || !LocalPlayer.IsWorldReady || session.Roster.Count < 2 || (session is ClientSession && !ClientCommandBridge.IsActive))
        {
            return;
        }
        if (_start < 0)
        {
            _start = now;
            _log.LogInfo($"[Orders] timeline as {session.Role}");
        }
        while (_next < _steps.Count && now - _start >= _steps[_next].At)
        {
            (double _, bool host, string name, Action step) = _steps[_next++];
            if (host == (session.Role == SessionRole.Host))
            {
                _log.LogInfo($"[Orders] step '{name}'");
                try
                {
                    step();
                }
                catch (Exception exception)
                {
                    _log.LogError($"[Orders] step '{name}' failed: {exception}");
                }
            }
        }
        if (now >= _nextProbe)
        {
            _nextProbe = now + 3;
            Probe(session);
        }
    }

    private static int TemplateWith(LogicManagerEnum logic) =>
        Enumerable.Range(0, World.buildPartManager!.BuildPartTemplates.Count).First(i => World.buildPartManager.BuildPartTemplates[i]?.logicManager == logic);

    private void BuildBed()
    {
        IReadOnlyList<BuildPart> templates = World.buildPartManager!.BuildPartTemplates;
        int[] stations = Enumerable.Range(0, templates.Count).Where(i => templates[i] != null && templates[i].mask == CPUCollisionType.GAT_STATION && templates[i].stationTemplate != null).ToArray();
        _log.LogInfo($"[Orders] station templates: {string.Join(", ", stations.Select(i => $"{i}:{templates[i].name}"))}");
        int bed = stations.FirstOrDefault(i => templates[i].name.Contains("bed")) is int b && b > 0 ? b : stations.First();
        World.buildPartManager.CreateBuildPart(bed, BedGrid, BuildPart.Rotation.R_0);
        _log.LogInfo($"[Orders] station '{templates[bed].name}' at {BedGrid}, sleep={BedStation()?.IsSleepStation}");
    }

    private static GATStation? BedStation()
    {
        var stations = new List<GATStation>();
        GATStation.GetStationsInSphere(BedGrid, 6f, stations);
        return stations.FirstOrDefault(s => s.IsSleepStation) ?? stations.FirstOrDefault();
    }

    private void SendKidsToBed()
    {
        GATStation station = BedStation() ?? throw new InvalidOperationException("The bed has no station on the client.");
        ClientCommandBridge.Send(CommandIntentMessage.InteractStation(GatInterop.ToNet(station.Position)));
        _log.LogInfo($"[Orders] sent kids to station at {station.Position} (sleep={station.IsSleepStation})");
    }

    private void LockKid()
    {
        ClientSession client = (ClientSession)MultiplayerController.Current!.Session!;
        Handle kid = GameFamilyAccess.CurrentChildren().First(h => client.Identities.TryGetHost(h.stableIndex, out _));
        client.Identities.TryGetHost(kid.stableIndex, out ushort host);
        ClientCommandBridge.Send(CommandIntentMessage.ToggleCommandLock(host));
        _log.LogInfo($"[Orders] lock request for kid {host}");
    }

    private void AttackAnimal()
    {
        ClientSession client = (ClientSession)MultiplayerController.Current!.Session!;
        global::GAT.GAT gat = World.gat!;
        Vector3 me = LocalPlayer.Position;
        Handle best = Handle.Null;
        float bestDistance = float.MaxValue;
        foreach (GATTemplateEntry entry in gat.GetTemplateEntries())
        {
            if (entry?.template == null || (entry.template.faction != Faction.HOSTILE && entry.template.faction != Faction.PASSIVE))
            {
                continue;
            }
            for (int p = entry.startIndex; p < entry.startIndex + entry.count && p < gat.instanceCount; p++)
            {
                Handle h = gat.GetHandle((ushort)p);
                Matrix4x4? m = gat.GetInstanceMatrixImmediate(h);
                float d = m.HasValue ? Vector3.Distance(m.Value.GetColumn(3), me) : float.MaxValue;
                if (d < bestDistance && client.Identities.TryGetHost(h.stableIndex, out _))
                {
                    best = h;
                    bestDistance = d;
                }
            }
        }
        if (best.isNull)
        {
            _log.LogWarning("[Orders] no animal found to attack");
            return;
        }
        client.Identities.TryGetHost(best.stableIndex, out ushort host);
        ClientCommandBridge.Send(CommandIntentMessage.InteractAgent(host, GatInterop.ToNet(me)));
        _log.LogInfo($"[Orders] attack request on {gat.GetTemplateFromHandle(best)?.name} {host}, {bestDistance:0} m away");
    }

    private int _planTemplate = -1;

    private void PlanWithLogs()
    {
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        RigidPrefab log = Enumerable.Range(0, manager.PrefabCount).Select(i => RigidTransformApi.TryGetPrefab(manager, i, out RigidPrefab p) ? p : null)
            .First(p => p != null && RigidTransformApi.PrefabName(p) == "rt_log01")!;
        ConstructionMaterial material = RigidTransformApi.PrefabSelfMaterial(log);
        IReadOnlyList<BuildPart> templates = World.buildPartManager!.BuildPartTemplates;
        _planTemplate = Enumerable.Range(0, templates.Count).First(i => templates[i] != null &&
            GameApiCompatibility.BuildPartMaterialCount(templates[i], material) > 0 && templates[i].rigidTransformPrefab == null);
        int amount = GameApiCompatibility.BuildPartMaterialCount(templates[_planTemplate], material);
        World.buildPlanManager!.CreateBuildPlan(_planTemplate, PlanGrid, BuildPart.Rotation.R_0, 0.5f);
        for (int i = 0; i < amount; i++)
        {
            RigidTransformApi.Spawn(manager, log, (Vector3)PlanGrid + new Vector3(2f + i, 2f, 3f), Quaternion.identity);
        }
        _log.LogInfo($"[Orders] plan '{templates[_planTemplate].name}' needs {amount} log(s); spawned them");
    }

    private void PickUpPlanLog()
    {
        RigidTransformManager manager = HostRigidRegistry.RequireManager();
        RigidTransform log = Enumerable.Range(0, RigidTransformApi.Count(manager))
            .Select(i => RigidTransformApi.TryGetManagedRigid(manager, i, out RigidTransform r) ? r : null)
            .Where(r => RigidTransformApi.IsAlive(r) && RigidTransformApi.Name(r).StartsWith("rt_log01"))
            .OrderBy(r => Vector3.Distance(r!.position, PlanGrid))
            .First()!;
        if (!ClientCommandBridge.TryGetNetId(log, out int netId))
        {
            throw new InvalidOperationException("The plan log has no net id on the client.");
        }
        ClientCommandBridge.Send(CommandIntentMessage.InteractRigid(netId));
        _log.LogInfo($"[Orders] pickup request for log {netId} at {log.position}");
    }

    private void OrderBuild()
    {
        int template = Enumerable.Range(0, World.buildPartManager!.BuildPartTemplates.Count)
            .First(i => WorldEdits.WorldEditRelay.FindPlan(WorldEdits.WorldEditRelay.KeyOf(i, PlanGrid, BuildPart.Rotation.R_0)) != null);
        BuildPart part = World.buildPartManager!.BuildPartTemplates[template];
        Vector3 center = part.GetPlacementMatrix(PlanGrid, BuildPart.Rotation.R_0).MultiplyPoint3x4(part.GetCombinedMeshBounds().center);
        ClientCommandBridge.Send(CommandIntentMessage.InteractBuildPlan(GatInterop.ToNet(center)));
    }

    private void Probe(ISession session)
    {
        FamilyHudCounts? hud = FamilyHud.Override;
        string extra = string.Empty;
        if (session.Role == SessionRole.Host && World.commandOwnershipService != null)
        {
            int locked = GameFamilyAccess.CurrentChildren().Count(h => World.commandOwnershipService.IsCommandLocked(h));
            int sleeping = GameFamilyAccess.CurrentChildren().Count(h => GameFamilyAccess.IsSleeping(h.stableIndex));
            extra = $" lockedKids={locked} sleepingKids={sleeping}";
        }
        GATStation? bed = BedStation();
        extra += $" bedStation={(bed == null ? "none" : bed.Position.ToString())}";
        bool built = World.buildPartManager!.TryGetBuildPart(PlanGrid, BuildPart.Rotation.R_0, out BuildPart builtPart);
        extra += $" planBuilt={(built ? builtPart.name : "no")}";
        _log.LogInfo($"[Orders] probe hud={(hud.HasValue ? $"{hud.Value.Children}/{hud.Value.NotSleeping} awake" : "none")}{extra} notice='{UI.PlayerNotice.Current}'");
    }
}
