using System;
using System.Collections;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Transport;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class CommandMarkerScenario : ScenarioRunner
{
    private static CommandMarkerScenario? _active;
    private readonly ManualLogSource _log;
    private readonly Dictionary<CommandVisuals.HudKind, (double At, Vector3 Position)> _drawn = new Dictionary<CommandVisuals.HudKind, (double, Vector3)>();
    private double _start = -1;
    private int _step;
    private ushort _target;
    private Vector3 _goto;
    private Vector3 _attack;

    public CommandMarkerScenario(ManualLogSource log)
    {
        _log = log;
        _active = this;
        FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60);
        FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground = true;
    }

    public override void Tick(double now, ISession? session)
    {
        FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60);
        if (!HasLivePair(session)) return;
        if (_start < 0)
        {
            _start = now;
            _target = FindAnimal(session!);
            _log.LogInfo($"[MarkerTest] target host={_target} role={session!.Role}");
        }
        double t = now - _start;
        bool client = session is ClientSession;
        if (client && t < 12)
        {
            Traverse player = Traverse.Create(World.playerController);
            player.Field("pitch").SetValue(35f);
            player.Field("yaw").SetValue(t < 7 ? 0f : 90f);
        }
        switch (_step)
        {
            case 0 when t > 4:
                _step++;
                if (client) ClientCommandBridge.Send(CommandIntentMessage.Follow(true));
                break;
            case 1 when t > 6:
                _step++;
                if (client)
                {
                    Check("goto-held", Recent(CommandVisuals.HudKind.Goto, now), "F held");
                    _goto = Position(CommandVisuals.HudKind.Goto);
                }
                break;
            case 2 when t > 10:
                _step++;
                if (client) Check("goto-moves", Recent(CommandVisuals.HudKind.Goto, now) && Vector3.Distance(_goto, Position(CommandVisuals.HudKind.Goto)) > 1, "changed aim");
                break;
            case 3 when t > 11:
                _step++;
                if (client) ClientCommandBridge.Send(CommandIntentMessage.Follow(false));
                break;
            case 4 when t > 14:
                _step++;
                if (session is HostSession)
                {
                    FollowController follow = HostCommandHub.Current!.FollowControllers().First(p => p.PlayerId != PlayerRosterMessage.HostPlayerId).Follow;
                    follow.StopFollowing(false);
                }
                break;
            case 5 when t > 17:
                _step++;
                if (client)
                {
                    Check("goto-clears", !Recent(CommandVisuals.HudKind.Goto, now), "following stopped");
                    Attack();
                }
                break;
            case 6 when t > 21:
                _step++;
                if (client)
                {
                    Check("attack-shows", Recent(CommandVisuals.HudKind.Attack, now), "client attack accepted");
                    _attack = Position(CommandVisuals.HudKind.Attack);
                }
                break;
            case 7 when t > 23:
                _step++;
                if (!client) MoveTarget();
                break;
            case 8 when t > 27:
                _step++;
                if (client)
                {
                    Check("attack-moves", Recent(CommandVisuals.HudKind.Attack, now) && Vector3.Distance(_attack, Position(CommandVisuals.HudKind.Attack)) > 5, "target moved on host");
                    Attack();
                }
                break;
            case 9 when t > 31:
                _step++;
                if (client)
                {
                    Check("attack-toggle-clears", !Recent(CommandVisuals.HudKind.Attack, now), "toggle same target off");
                    Attack();
                }
                break;
            case 10 when t > 35:
                _step++;
                if (client) Check("attack-restarts", Recent(CommandVisuals.HudKind.Attack, now), "second attack");
                break;
            case 11 when t > 37:
                _step++;
                if (!client) World.gat.DeleteInstance(GatInterop.ToHandle(_target));
                break;
            case 12 when t > 41:
                _step++;
                if (client) Check("attack-death-clears", !Recent(CommandVisuals.HudKind.Attack, now), "host deleted target");
                else Check("remote-markers-stay-off-host", !_drawn.ContainsKey(CommandVisuals.HudKind.Goto) && !_drawn.ContainsKey(CommandVisuals.HudKind.Attack), "only issuing player draws");
                break;
            case 13 when t > 44:
                _step++;
                if (session is HostSession host) SendToClient(host, CaptureSiblings());
                break;
            case 14 when t > 48:
                _step++;
                if (client)
                {
                    Check("pickup-sibling", Recent(CommandVisuals.HudKind.Pickup, now) && Vector3.Distance(Position(CommandVisuals.HudKind.Pickup), new Vector3(6, 101.5f, 8)) < 0.01f, "native pickup source -> network -> native HUD");
                    Check("dropoff-sibling", Recent(CommandVisuals.HudKind.Dropoff, now) && Vector3.Distance(Position(CommandVisuals.HudKind.Dropoff), new Vector3(10, 100, 12)) < 0.01f, "native manual dropoff source");
                    Check("construction-sibling", Recent(CommandVisuals.HudKind.Construction, now), "native construction and water collection sources");
                }
                else Check("siblings-stay-off-host", !_drawn.ContainsKey(CommandVisuals.HudKind.Pickup) && !_drawn.ContainsKey(CommandVisuals.HudKind.Dropoff) && !_drawn.ContainsKey(CommandVisuals.HudKind.Construction), "recipient-specific delivery");
                break;
            case 15 when t > 50:
                _step++;
                if (session is HostSession clearHost) SendToClient(clearHost, new CommandMarkersMessage(Array.Empty<CommandMarker>()));
                break;
            case 16 when t > 54:
                _step++;
                if (client) Check("siblings-clear", !Recent(CommandVisuals.HudKind.Pickup, now) && !Recent(CommandVisuals.HudKind.Dropoff, now) && !Recent(CommandVisuals.HudKind.Construction, now), "empty authoritative snapshot");
                break;
        }
    }

    private static void SendToClient(HostSession host, CommandMarkersMessage message)
    {
        foreach (object peer in Traverse.Create(host).Field<IEnumerable>("_peers").Value)
        {
            Traverse fields = Traverse.Create(peer);
            if (fields.Property("Phase").GetValue().ToString() == "Live") fields.Property<INetConnection>("Connection").Value.Send(message);
        }
    }

    private CommandMarkersMessage CaptureSiblings()
    {
        Traverse service = Traverse.Create(World.commandService);
        var pickup = new PickupWorkerScheduler(World.commandOwnershipService, service.Field<CommandVisuals>("_visuals").Value, null!, null!, null!, null!, null!);
        var construction = GameApiCompatibility.CreateConstructionScheduler(pickup, Camera.main, null!, 30, 10);
        Traverse.Create(construction).Field("_sourcePosition").SetValue((Vector3?)new Vector3(14, 100, 16));
        IList targets = Traverse.Create(pickup).Field<IList>("_activePickupTargets").Value;
        Type targetType = targets.GetType().GetGenericArguments()[0];
        object target = targetType.GetConstructor(new[] { typeof(RigidTransform) }).Invoke(new object?[] { null });
        Traverse.Create(target).Field("propPosition").SetValue(new Vector3(6, 100, 8));
        targets.Add(target);
        IList workers = Traverse.Create(pickup).Field<IList>("_workers").Value;
        Type workerType = workers.GetType().GetGenericArguments()[0];
        foreach (bool water in new[] { false, true })
        {
            object worker = Activator.CreateInstance(workerType, true);
            Traverse.Create(worker).Field("job").SetValue(new PickupJobHandle { IsManualDropoff = !water, IsWaterCollection = water, HudPosition = water ? new Vector3(18, 100, 20) : new Vector3(10, 100, 12) });
            workers.Add(worker);
        }
        CommandMarkersMessage message = CommandMarkerCapture.Capture(service.Field<FollowController>("_follow").Value, service.Field<AttackCommandController>("_attack").Value, pickup, construction);
        Check("native-sibling-capture", message.Markers.Count == 4 && message.Markers.Count(m => m.Kind == CommandMarkerKind.Construction) == 2, "pickup, dropoff, build and water collection");
        return message;
    }

    private static ushort FindAnimal(ISession session)
    {
        var candidates = new List<ushort>();
        foreach (GATTemplateEntry entry in World.gat.GetTemplateEntries())
        {
            if (entry?.template == null || (int)entry.template.faction <= (int)Faction.PLAYER) continue;
            for (int p = entry.startIndex; p < entry.startIndex + entry.count; p++)
            {
                Handle handle = World.gat.GetHandle((ushort)p);
                if (session is ClientSession client)
                {
                    if (client.Identities.TryGetHost(handle.stableIndex, out ushort host)) candidates.Add(host);
                }
                else candidates.Add(handle.stableIndex);
            }
        }
        return candidates.Min();
    }

    private void Attack() => ClientCommandBridge.Send(CommandIntentMessage.InteractAgent(_target, GatInterop.ToNet(LocalPlayer.Position)));

    private void MoveTarget()
    {
        Handle handle = GatInterop.ToHandle(_target);
        Matrix4x4 matrix = World.gat.GetInstanceMatrixImmediate(handle) ?? throw new InvalidOperationException("Diagnostic animal disappeared.");
        Vector3 position = matrix.GetColumn(3);
        matrix.SetColumn(3, new Vector4(position.x + 12, position.y, position.z, 1));
        World.gat.SetInstanceMatrix(handle, matrix, false, false, false);
    }

    private bool Recent(CommandVisuals.HudKind kind, double now) => _drawn.TryGetValue(kind, out var draw) && now - draw.At < 2;
    private Vector3 Position(CommandVisuals.HudKind kind) => _drawn.TryGetValue(kind, out var draw) ? draw.Position : Vector3.zero;
    private void Check(string name, bool pass, string detail) => _log.LogInfo($"[MarkerTest] {(pass ? "PASS" : "FAIL")} {name} {detail}");

    internal static void ObserveDraw(CommandVisuals.HudKind kind, Vector3 position)
    {
        if (_active != null) _active._drawn[kind] = (FamilyTimeMultiplayer.Game.GameClock.Now, position);
    }

    public override void Dispose()
    {
        if (ReferenceEquals(_active, this)) _active = null;
        base.Dispose();
    }
}
