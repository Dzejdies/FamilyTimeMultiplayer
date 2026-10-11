using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Patches;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class FamilyViewScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private double _start = -1;
    private int _step;
    private GATStation? _bed;

    public FamilyViewScenario(ManualLogSource log)
    {
        _log = log;
        FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60);
        FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground = true;
        Traverse.Create(MultiplayerController.Current).Field("_gameRunsInBackground").SetValue(true);
    }

    public override void Tick(double now, ISession? session)
    {
        if (!HasLivePair(session)) return;
        if (_start < 0) _start = now;
        double t = now - _start;
        if (_step == 0 && t > 5)
        {
            _step++;
            if (session is HostSession host)
            {
                SetSleeping(host, false);
                World.gatHungerBehavior.SetHungerType(HungerType.THIRST);
            }
        }
        if (_step == 1 && t > 11)
        {
            _step++;
            Check(session!, session is ClientSession, "client-sleeps");
        }
        if (_step == 2 && t > 16)
        {
            _step++;
            if (session is HostSession host)
            {
                SetSleeping(host, true);
                World.gatHungerBehavior.SetHungerType(HungerType.FOOD);
            }
        }
        if (_step == 3 && t > 23)
        {
            _step++;
            Check(session!, session is HostSession, "host-sleeps");
        }
    }

    private void SetSleeping(HostSession host, bool hostSleeps)
    {
        HostFamilyService families = HostFamilyService.Current!;
        if (_bed == null)
        {
            var templates = World.buildPartManager!.BuildPartTemplates;
            int index = Enumerable.Range(0, templates.Count).First(i => templates[i] != null && templates[i].stationTemplate != null && templates[i].name.Contains("cot"));
            World.buildPartManager.CreateBuildPart(index, new Vector3Int(8, 64, 8), BuildPart.Rotation.R_0);
            var stations = new List<GATStation>();
            GATStation.GetStationsInSphere(new Vector3(8, 64, 8), 6f, stations);
            _bed = stations.First(s => s.IsSleepStation);
        }
        string remoteOwner = families.OwnerOfPlayer(host.RemotePlayers.First().PlayerId);
        foreach (string owner in new[] { families.HostOwner, remoteOwner })
        {
            foreach (ushort id in families.MembersOf(owner))
            {
                var handle = GatInterop.ToHandle(id);
                World.gatStationManager!.UnregisterAssignment(_bed, handle);
                if ((owner == families.HostOwner) == hostSleeps && !GameApiCompatibility.TryRegisterAssignment(World.gatStationManager, _bed, handle, Faction.PLAYER))
                    throw new InvalidOperationException("Cannot assign diagnostic sleeper.");
            }
        }
        _log.LogInfo($"[FamilyView] sleeping owner={(hostSleeps ? "host" : "client")}");
    }

    private void Check(ISession session, bool sleeping, string phase)
    {
        HungerType expectedNeed = phase == "client-sleeps" ? HungerType.THIRST : HungerType.FOOD;
        Report($"{phase}-need", World.gatHungerBehavior.HungerType == expectedNeed, $"actual={World.gatHungerBehavior.HungerType} expected={expectedNeed}");
        FamilyHudCounts counts = FamilyHud.ReadDisplayedCounts();
        Report($"{phase}-sleep", counts.Children == 3 && counts.NotSleeping == (sleeping ? 0 : 3), $"children={counts.Children} awake={counts.NotSleeping}");
        Traverse fields = Traverse.Create(World.stateDisplayUI);
        fields.Field("_hungryTotal").SetValue(999);
        fields.Field("_agentsNotSleepingCount").SetValue(999);
        AccessTools.Method(typeof(GATStateDisplayUI), "RefreshTotalPlayerCount").Invoke(World.stateDisplayUI, null);
        counts = FamilyHud.ReadDisplayedCounts();
        Report($"{phase}-refresh", counts.Children == 3 && counts.NotSleeping == (sleeping ? 0 : 3) && counts.Hungry <= 3, $"children={counts.Children} awake={counts.NotSleeping} hungry={counts.Hungry}");
        World.stateDisplayUI.RunOvernight();
        var entry = fields.Field<GATStateDisplayEntry>("_hungerEntry").Value;
        var thumbnail = entry == null ? null : Traverse.Create(entry).Field<UnityEngine.UI.Image>("thumbnail").Value;
        var info = World.gatHungerBehavior.CurrentHungerTypeInfo;
        Report($"{phase}-need-icon", thumbnail != null && thumbnail.sprite == info.icon && Shader.GetGlobalInt(global::Constants.hungerIconIndexID) == (int)info.screenIconIndex,
            $"thumbnail={(thumbnail == null ? "missing" : thumbnail.sprite.name)} need={World.gatHungerBehavior.HungerType}");
        int[] expected = LocalFamilyView.PhysicalMembers();
        Report($"{phase}-icons", expected.Length == 3 && FamilyIconPatch.LastSubmitted.SequenceEqual(expected) && FamilyIconPatch.DrawCalls > 0, $"owned={string.Join(",", expected)} submitted={string.Join(",", FamilyIconPatch.LastSubmitted)} draws={FamilyIconPatch.DrawCalls}");
    }

    private void Report(string name, bool pass, string detail) => _log.LogInfo($"[FamilyView] {(pass ? "PASS" : "FAIL")} {name} {detail}");
}
