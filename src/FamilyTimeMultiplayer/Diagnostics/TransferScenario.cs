using System;
using System.Collections;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class TransferScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private bool _reloaded;
    private bool _checked;
    public TransferScenario(ManualLogSource log) { _log = log; FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground = true; QualitySettings.vSyncCount = 0; FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60); }
    public override void Tick(double now, ISession? session)
    {
        if (_checked || session == null) return;
        if (session is HostSession host)
        {
            if (!_reloaded)
            {
                IEnumerable peers = Traverse.Create(host).Field<IEnumerable>("_peers").Value;
                bool waiting = peers.Cast<object>().Any(peer => Traverse.Create(peer).Property("Phase").GetValue().ToString() == "AwaitingReady");
                if (!waiting || World.worldSerializer.IsBusy) return;
                _reloaded = true;
                _log.LogInfo("[TransferTest] reload during initial join");
                World.worldSerializer.Load("ftmp_testbed", success => { if (!success) throw new InvalidOperationException("Transfer test world reload failed."); });
            }
            else if (HasLivePair(session)) { _checked = true; _log.LogInfo("[TransferTest] PASS host-rejoined-after-reload"); }
        }
        else if (HasLivePair(session) && Traverse.Create(session).Field<int>("_worldLoadGeneration").Value >= 2)
        {
            _checked = true;
            _log.LogInfo("[TransferTest] PASS client-loaded-latest-world");
        }
    }
}
