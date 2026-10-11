using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class CommandProbe
{
    private const float NearDistance = 8f;

    private readonly ManualLogSource _log;
    private readonly AgentSnapshotSampler _sampler = new AgentSnapshotSampler();

    public CommandProbe(ManualLogSource log)
    {
        _log = log;
    }

    public void Sample(ISession session)
    {
        CommandOwnershipService? ownership = World.commandOwnershipService;
        global::GAT.GAT? gat = World.gat;
        if (ownership == null || gat == null || _sampler.InFlight)
        {
            return;
        }
        int workerTemplate = ownership.WorkerTemplateIndex;
        var players = new List<(string Name, Vector3 Position)> { ("self", LocalPlayer.Position) };
        foreach (RosterEntry entry in session.Roster)
        {
            if (entry.PlayerId != session.LocalPlayerId && session.TryGetRemotePlayerPosition(entry, out Vector3 remote))
            {
                players.Add((entry.Name, remote));
            }
        }
        _sampler.Request(agents =>
        {
            var near = new int[players.Count];
            int kids = 0;
            foreach (AgentState agent in agents)
            {
                if (World.gat == null || World.gat.GetTemplateIndexFromHandle(GatInterop.ToHandle(agent.StableIndex)) != workerTemplate)
                {
                    continue;
                }
                kids++;
                Vector3 position = GatInterop.ToUnity(agent.Pose.Position);
                for (int i = 0; i < players.Count; i++)
                {
                    if (Vector3.Distance(position, players[i].Position) <= NearDistance)
                    {
                        near[i]++;
                    }
                }
            }
            var line = new StringBuilder($"[Command] {session.Role} kids={kids} available={ownership.AvailableWorkerCount}");
            for (int i = 0; i < players.Count; i++)
            {
                line.Append($" near[{players[i].Name}]={near[i]}");
            }
            _log.LogInfo(line.ToString());
        }, error => _log.LogWarning($"[Command] {error}"));
    }
}
