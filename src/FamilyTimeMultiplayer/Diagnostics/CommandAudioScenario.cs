using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class CommandAudioScenario : ScenarioRunner
{
    private static readonly string[] AudioFields =
    {
        "commandStartAudioGroup", "commandStopAudioGroup", "dropOffStartAudioGroup", "queueAddAudioGroup",
        "queueRemoveAudioGroup", "commandConstructionAudioGroup", "commandAttackAudioGroup", "commandStationAudioGroup",
        "followStartAudioGroup", "followEndAudioGroup", "lockAudioGroup", "unlockAudioGroup",
    };
    private static readonly MethodInfo PlayCommandAudio = AccessTools.Method(AccessTools.TypeByName("GAT.CommandAudio"), "Play");
    private readonly ManualLogSource _log;
    private readonly Dictionary<AudioGroup, int> _played = new Dictionary<AudioGroup, int>();
    private double _start = -1;
    private int _step;
    private ushort _lockTarget;
    private WolfMama? _mama;
    private string _mamaCue = string.Empty;
    private int _mamaPrevious;
    private int _lockPrevious;
    private int _unlockPrevious;
    private static CommandAudioScenario? Active;

    public CommandAudioScenario(ManualLogSource log)
    {
        _log = log;
        Active = this;
        FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60);
    }

    private static AudioGroup Group(string field) => Traverse.Create(World.commandService!).Field<AudioGroup>(field).Value;

    private int Count(string field) => _played.TryGetValue(Group(field), out int value) ? value : 0;

    public override void Tick(double now, ISession? session)
    {
        if (session == null || !HasLivePair(session))
        {
            return;
        }
        if (_start < 0)
        {
            _start = now;
            if (session is ClientSession client)
            {
                Handle child = Families.GameFamilyAccess.CurrentChildren().First(h => client.Identities.TryGetHost(h.stableIndex, out _));
                client.Identities.TryGetHost(child.stableIndex, out _lockTarget);
            }
        }
        double elapsed = now - _start;
        switch (_step)
        {
            case 0 when elapsed > 2:
                _step++;
                if (session.Role == SessionRole.Host)
                {
                    HostCommandHub.HostControllers().Follow.StartFollowing();
                    HostCommandHub.HostControllers().Follow.StopFollowing(playSound: true);
                }
                break;
            case 1 when elapsed > 4:
                _step++;
                if (session.Role == SessionRole.Client)
                {
                    Check("host command stays on host", Count("followStartAudioGroup") == 0 && Count("followEndAudioGroup") == 0);
                    FollowClick();
                }
                break;
            case 2 when elapsed > 8:
                _step++;
                if (session.Role == SessionRole.Client)
                {
                    Check("client follow plays once", Count("followStartAudioGroup") == 1);
                    FollowClick();
                }
                break;
            case 3 when elapsed > 12:
                _step++;
                if (session.Role == SessionRole.Client)
                {
                    Check("client stop plays once", Count("followEndAudioGroup") == 1);
                    _lockPrevious = Count("lockAudioGroup");
                    ClientCommandBridge.Send(CommandIntentMessage.ToggleCommandLock(_lockTarget));
                }
                break;
            case 4 when elapsed > 16:
                _step++;
                if (session.Role == SessionRole.Client)
                {
                    Check("client lock plays once", Count("lockAudioGroup") == _lockPrevious + 1);
                    _unlockPrevious = Count("unlockAudioGroup");
                    ClientCommandBridge.Send(CommandIntentMessage.ToggleCommandLock(_lockTarget));
                }
                break;
            case 5 when elapsed > 20:
                _step++;
                if (session.Role == SessionRole.Client)
                {
                    Check("client unlock plays once", Count("unlockAudioGroup") == _unlockPrevious + 1);
                }
                else
                {
                    Check("remote follow stays silent on host", Count("followStartAudioGroup") == 1 && Count("followEndAudioGroup") == 1);
                }
                break;
            case 6 when elapsed > 22:
                _step++;
                if (session.Role == SessionRole.Host)
                {
                    IDictionary commanders = Traverse.Create(HostCommandHub.Current!).Field("_commanders").GetValue() as IDictionary
                        ?? throw new InvalidOperationException("No commander registry for audio test.");
                    RemoteCommander commander = commanders.Values.Cast<RemoteCommander>().Single();
                    using (CommanderContext.Enter(commander))
                    {
                        foreach (string field in AudioFields)
                        {
                            PlayCommandAudio.Invoke(null, new object[] { Group(field) });
                        }
                    }
                }
                break;
            case 7 when elapsed > 26:
                _step++;
                if (session.Role == SessionRole.Client)
                {
                    foreach (string field in AudioFields)
                    {
                        Check($"routed cue {field}", Count(field) > 0);
                    }
                }
                if (session.Role == SessionRole.Client)
                {
                    _mama = Holds.MamaLookup.All().First(m => m.name == "wolf_mama (Client)");
                    _mamaCue = _mama.AllowFollow ? "lockAudioGroup" : "unlockAudioGroup";
                    _mamaPrevious = Count(_mamaCue);
                    GameApiCompatibility.ToggleMama(_mama);
                }
                break;
            case 8 when elapsed > 30:
                _step++;
                if (session.Role == SessionRole.Client)
                {
                    Check("mama toggle plays once", Count(_mamaCue) == _mamaPrevious + 1);
                    _mamaCue = _mama!.AllowFollow ? "lockAudioGroup" : "unlockAudioGroup";
                    _mamaPrevious = Count(_mamaCue);
                    GameApiCompatibility.ToggleMama(_mama);
                }
                break;
            case 9 when elapsed > 34:
                _step++;
                if (session.Role == SessionRole.Client)
                {
                    Check("mama reverse toggle plays once", Count(_mamaCue) == _mamaPrevious + 1);
                }
                _log.LogInfo($"[AudioTest] DONE {session.Role}");
                break;
        }
    }

    private static void FollowClick()
    {
        ClientCommandBridge.Send(CommandIntentMessage.Follow(pressed: true));
        ClientCommandBridge.Send(CommandIntentMessage.Follow(pressed: false));
    }

    private void Check(string name, bool pass)
    {
        if (pass)
        {
            _log.LogInfo($"[AudioTest] PASS {name}");
        }
        else
        {
            _log.LogError($"[AudioTest] FAIL {name}");
        }
    }

    [HarmonyPatch(typeof(GATSoundManager), "PlayAudioGroupAt")]
    private static class PlaybackProbe
    {
        private static void Postfix(bool __result, AudioGroup audioGroup, bool cameraSound)
        {
            if (__result && cameraSound && audioGroup != null && Active != null)
            {
                Active._played.TryGetValue(audioGroup, out int previous);
                Active._played[audioGroup] = previous + 1;
                Active._log.LogInfo($"[AudioTest] source played '{audioGroup.name}'");
            }
        }
    }
}
