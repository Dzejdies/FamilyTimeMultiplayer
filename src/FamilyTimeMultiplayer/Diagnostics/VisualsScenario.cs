using System;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class VisualsScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private double _start = -1;
    private double _nextProbe;
    private int _step;

    public VisualsScenario(ManualLogSource log)
    {
        _log = log;
    }

    public override void Tick(double now, ISession? session)
    {
        if (session == null || !HasLivePair(session))
        {
            return;
        }
        if (_start < 0)
        {
            _start = now;
        }
        double t = now - _start;
        if (session.Role == SessionRole.Host)
        {
            if (_step == 0 && t > 6)
            {
                _step++;
                HostCommandHub.HostControllers().Follow.StartFollowing();
                _log.LogInfo("[Visuals] host FOLLOW (icons)");
            }
            if (_step == 1 && t > 14)
            {
                _step++;
                World.gatHungerBehavior!.IncreaseHunger();
                _log.LogInfo("[Visuals] host made every child hungry");
            }
            if (_step == 2 && t > 22)
            {
                _step++;
                HostCommandHub.HostControllers().Follow.StopFollowing(playSound: false);
                _log.LogInfo("[Visuals] host stop follow");
            }
            if (_step == 3 && t > 26)
            {
                _step++;
                foreach (Handle child in Families.GameFamilyAccess.CurrentChildren())
                {
                    if (World.gat!.TryGetInstanceInfoImmediate(child, out GATInstanceInfo info))
                    {
                        World.gat.SetInstanceInfo(child, GatInterop.CreateInstanceInfo(info.info & ~(1u << 19)));
                    }
                }
                _log.LogInfo("[Visuals] host cleared hunger (fed state)");
            }
            if (_step == 4 && t > 31)
            {
                _step++;
                CheckHunger(0);
            }
            if (_step == 5 && t > 34)
            {
                _step++;
                World.gatHungerBehavior!.IncreaseHunger();
                _log.LogInfo("[Visuals] host hungry again");
            }
            if (_step == 6 && t > 39)
            {
                _step++;
                CheckHunger(1);
            }
        }
        else
        {
            if (_step == 0 && t > 31)
            {
                _step++;
                CheckHunger(0);
            }
            if (_step == 1 && t > 39)
            {
                _step++;
                CheckHunger(1);
            }
        }
        if (now >= _nextProbe)
        {
            _nextProbe = now + 2;
            Probe(session);
        }
    }

    private void CheckHunger(byte expected)
    {
        Handle[] children = Families.GameFamilyAccess.CurrentChildren().ToArray();
        bool pass = children.Length > 0 && children.All(child =>
            World.gat!.TryGetInstanceInfoImmediate(child, out GATInstanceInfo info) && info.hunger == expected);
        if (pass)
        {
            _log.LogInfo($"[Visuals] PASS hunger={expected} on {children.Length} children");
        }
        else
        {
            _log.LogError($"[Visuals] FAIL hunger={expected}");
        }
    }

    private void Probe(ISession session)
    {
        global::GAT.GAT gat = World.gat!;
        var children = Families.GameFamilyAccess.CurrentChildren();
        string line = string.Join(" ", children
            .Select(local => (local, host: session is ClientSession client ? (client.Identities.TryGetHost(local.stableIndex, out ushort h) ? h : (ushort)ushort.MaxValue) : local.stableIndex))
            .OrderBy(pair => pair.host)
            .Take(8)
            .Select(pair => gat.TryGetInstanceInfoImmediate(pair.local, out GATInstanceInfo info)
                ? $"{pair.host}:h{info.hunger}/i{((info.info >> 24) & 0xF)}/hp{info.health}"
                : $"{pair.host}:?"));
        _log.LogInfo($"[Visuals] t={_nextProbe - _start:0} {line}");
    }
}
