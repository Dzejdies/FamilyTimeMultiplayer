using System;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using FamilyTimeMultiplayer.WorldEdits;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

/// <summary>Read-only, bounded build preflight. It never fabricates a build or bypasses tool authority.</summary>
public sealed class BuildScenario : ScenarioRunner
{
    private const float ReadinessTimeoutSeconds = 90f;
    private readonly ManualLogSource _log;
    private float _startedAt = -1;
    private float _nextReport;
    private bool _finished;

    public BuildScenario(ManualLogSource log) => _log = log;

    public override void Tick(double now, ISession? session)
    {
        if (_finished) return;
        float elapsed = _startedAt < 0 ? 0 : Time.realtimeSinceStartup - _startedAt;
        _startedAt = _startedAt < 0 ? Time.realtimeSinceStartup : _startedAt;
        string missing = MissingPreconditions(session);
        if (missing.Length > 0)
        {
            if (elapsed >= ReadinessTimeoutSeconds) Finish("BLOCKED_PRECONDITION", $"readiness timeout; missing={missing}", session);
            else if (Time.realtimeSinceStartup >= _nextReport)
            {
                _nextReport = Time.realtimeSinceStartup + 10;
                _log.LogInfo($"[BuildReport] verdict=WAITING elapsed={elapsed:0.0}s missing={missing} {Facts(session)}");
            }
            return;
        }
        if (session == null) return;

        InspectPlacement(session);
    }

    private static string MissingPreconditions(ISession? session)
    {
        if (session == null || session.Status != "Live") return "live-session";
        if (!LocalPlayer.IsWorldReady) return "world-ready";
        if (session.Roster.Count < 2) return "two-player-roster";
        if (session.Role == SessionRole.Host && HostCommandHub.Current == null) return "host-command-hub";
        if (session is ClientSession && !Commands.ClientCommandBridge.IsActive) return "client-command-bridge";
        return string.Empty;
    }

    private void InspectPlacement(ISession session)
    {
        try
        {
            Vector3 hostPosition = GetHostPosition(session);
            Heightfield terrain = GatInterop.RequireAlive(World.heightfield, "heightfield unavailable");
            BuildPartManager parts = GatInterop.RequireAlive(World.buildPartManager, "build-part manager unavailable");
            var templates = parts.BuildPartTemplates;
            var template = templates.Select((part, index) => (part, index))
                .FirstOrDefault(entry => entry.part != null && entry.part.HasMesh && !entry.part.hideFromBlueprintTool && entry.part.rigidTransformPrefab == null);
            bool hasTemplate = template.part != null;
            BlueprintTool? blueprint = World.playerController?.Tools?.OfType<BlueprintTool>().FirstOrDefault();
            float reach = World.playerController == null ? -1 : World.playerController.ReachDistance;
            Vector3Int? candidate = hasTemplate ? FindNearbyFreeCell(hostPosition, parts, terrain) : null;
            string targetFacts = candidate.HasValue
                ? DescribeCandidate(candidate.Value, template.part!, template.index, hostPosition, reach, parts, terrain)
                : "candidate=none";
            _log.LogInfo($"[BuildReport] verdict=BLOCKED_PRECONDITION reason='preflight has no authorized gameplay input driver; no build request, resource check/consumption, out-of-range rejection, or replication assertion was executed' role={session.Role} hostPosition={hostPosition} controlled={LocalPlayer.PossessedHandle} blueprintTool={blueprint != null} reach={reach:0.0} templates={templates.Count} selectedTemplate={(hasTemplate ? $"{template.index}:{template.part!.name}" : "none")} {targetFacts} authority='HostToolAuthority.Validate preserved'");
        }
        catch (Exception exception)
        {
            Finish("FAIL", $"placement inspection failed: {exception}", session);
        }
        _finished = true;
    }

    private static Vector3 GetHostPosition(ISession session)
    {
        if (session.Role == SessionRole.Host) return LocalPlayer.Position;
        var host = session.Roster.FirstOrDefault(player => player.PlayerId == PlayerRosterMessage.HostPlayerId);
        return session.TryGetRemotePlayerPosition(host, out Vector3 position) ? position : LocalPlayer.Position;
    }

    private static Vector3Int? FindNearbyFreeCell(Vector3 origin, BuildPartManager parts, Heightfield terrain)
    {
        int x = Mathf.RoundToInt(origin.x);
        int z = Mathf.RoundToInt(origin.z);
        for (int radius = 0; radius <= 8; radius++)
        for (int dx = -radius; dx <= radius; dx++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
            int cellX = x + dx;
            int cellZ = z + dz;
            if (!InBounds(terrain, cellX, cellZ)) continue;
            Vector3Int cell = new Vector3Int(cellX, terrain.GetRawHeight(cellX, cellZ), cellZ);
            if (!parts.TryGetBuildPart(cell, BuildPart.Rotation.R_0, out _)) return cell;
        }
        return null;
    }

    private static string DescribeCandidate(Vector3Int cell, BuildPart template, int templateIndex, Vector3 hostPosition, float reach, BuildPartManager parts, Heightfield terrain)
    {
        Vector3 point = template.GetPlacementMatrix(cell, BuildPart.Rotation.R_0).GetColumn(3);
        float extent = template.GetCombinedMeshBounds().extents.magnitude;
        float distance = Vector3.Distance(hostPosition, point);
        bool inRange = reach >= 0 && distance <= reach + extent + 3;
        bool occupied = parts.TryGetBuildPart(cell, BuildPart.Rotation.R_0, out _);
        byte rawHeight = terrain.GetRawHeight(cell.x, cell.z);
        return $"candidate={cell} template={templateIndex}:{template.name} worldPoint={point} distance={distance:0.00} extent={extent:0.00} hostReach={reach:0.00} rangeCheck={inRange} occupied={occupied} rawHeight={rawHeight} terrainY={rawHeight * 0.5f:0.00} materials=not-validated";
    }

    private static bool InBounds(Heightfield terrain, int x, int z) =>
        x >= -2048 && z >= -2048 && (long)x + 2048 < terrain.MapWidth && (long)z + 2048 < terrain.MapDepth;

    private static string Facts(ISession? session) =>
        $"role={session?.Role.ToString() ?? "none"} session={session?.Status ?? "none"} worldReady={LocalPlayer.IsWorldReady} roster={session?.Roster.Count ?? 0} position={LocalPlayer.Position} controlled={LocalPlayer.PossessedHandle}";

    private void Finish(string verdict, string reason, ISession? session)
    {
        _finished = true;
        _log.LogError($"[BuildReport] verdict={verdict} reason='{reason}' {Facts(session)}");
    }
}
