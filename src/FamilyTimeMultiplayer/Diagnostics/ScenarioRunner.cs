using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using FamilyTimeMultiplayer.Core.Saves;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public abstract class ScenarioRunner
{
    private Harmony? _diagnosticHarmony;

    public abstract void Tick(double now, ISession? session);

    public virtual void Dispose()
    {
        _diagnosticHarmony?.UnpatchSelf();
        _diagnosticHarmony = null;
    }

    protected static bool HasLivePair(ISession? session) =>
        session != null && LocalPlayer.IsWorldReady && session.Roster.Count >= 2 &&
        (session is HostSession host ? host.RemotePlayers.Count > 0 : Commands.ClientCommandBridge.IsActive);

    public static ScenarioRunner Create(string name, ManualLogSource log)
    {
        ScenarioRunner scenario = name switch
        {
            "idle" => new IdleScenario(),
            "walk-circle" => new WalkCircleScenario(log),
            "watch-peer" => new WatchPeerScenario(),
            "follow-walk" => new FollowThenWalkScenario(log),
            "mp-commands" => new MultiplayerCommandScenario(log),
            "sp-pickup" => MultiplayerCommandScenario.SinglePlayerControl(log),
            "mp-family" => MultiplayerCommandScenario.SeparateFamilies(log),
            "mp-hold" => MultiplayerCommandScenario.Holding(log),
            "mp-build" => new BuildScenario(log),
            "mp-generic-bridge" => new GenericBridgeRuntimeScenario(log),
            "mp-security" => new SecurityScenario(log),
            "mp-water" => new WaterScenario(log),
            "mp-transfer" => new TransferScenario(log),
            "mp-identity" => new IdentityScenario(log),
            "mp-outlines" => new CommandOutlineScenario(log),
            "mp-markers" => new CommandMarkerScenario(log),
            "mp-family-view" => new FamilyViewScenario(log),
            "mp-orders" => new OrdersScenario(log),
            "mp-visuals" => new VisualsScenario(log),
            "mp-despawn" => new DespawnScenario(log),
            "mp-world" => new WorldStateScenario(log),
            "mp-sync" => new SyncScenario(log),
            "mp-audio" => new CommandAudioScenario(log),
            "mp-audio-weather" => new AudioWeatherScenario(log),
            "menu-shot" => new MenuShotScenario(log),
            "mama-probe" => new MamaProbeScenario(log),
            _ => throw new ArgumentException($"Unknown -ftmp-scenario '{name}'. Known: idle, walk-circle, watch-peer, follow-walk, mp-commands, sp-pickup, mp-family, menu-shot."),
        };
        scenario.InstallDiagnosticPatches(name);
        return scenario;
    }

    private void InstallDiagnosticPatches(string scenario)
    {
        IReadOnlyList<DiagnosticProbe> probes = ScenarioDiagnosticPatchPlan.ForScenario(scenario);
        if (probes.Count == 0) return;

        var harmony = new Harmony($"{Plugin.Guid}.diagnostic.{scenario}.{Guid.NewGuid():N}");
        try
        {
            foreach (DiagnosticProbe probe in probes)
            {
                ScenarioDiagnosticPatchInstaller.Install(harmony, probe);
            }
            _diagnosticHarmony = harmony;
        }
        catch
        {
            harmony.UnpatchSelf();
            throw;
        }
    }

    private sealed class IdleScenario : ScenarioRunner
    {
        public override void Tick(double now, ISession? session)
        {
        }
    }

    private sealed class SyncScenario : ScenarioRunner
    {
        private readonly DespawnScenario _despawns;
        private readonly WorldStateScenario _world;
        private readonly VisualsScenario _visuals;

        public SyncScenario(ManualLogSource log)
        {
            _despawns = new DespawnScenario(log);
            _world = new WorldStateScenario(log, initialStorm: true);
            _visuals = new VisualsScenario(log);
        }

        public override void Tick(double now, ISession? session)
        {
            _despawns.Tick(now, session);
            _world.Tick(now, session);
            _visuals.Tick(now, session);
        }

        public override void Dispose()
        {
            _despawns.Dispose();
            _world.Dispose();
            _visuals.Dispose();
            base.Dispose();
        }
    }

    private sealed class AudioWeatherScenario : ScenarioRunner
    {
        private readonly CommandAudioScenario _audio;
        private readonly WorldStateScenario _weather;

        public AudioWeatherScenario(ManualLogSource log)
        {
            _audio = new CommandAudioScenario(log);
            _weather = new WorldStateScenario(log, initialStorm: true);
        }

        public override void Tick(double now, ISession? session)
        {
            _weather.Tick(now, session);
            _audio.Tick(now, session);
        }

        public override void Dispose()
        {
            _weather.Dispose();
            _audio.Dispose();
            base.Dispose();
        }
    }

    private sealed class MenuShotScenario : ScenarioRunner
    {
        private readonly ManualLogSource _log;
        private SaveImportUiScenario? _saveImportUiScenario;
        private double _start = -1;
        private int _step;

        public MenuShotScenario(ManualLogSource log)
        {
            _log = log;
        }

        public override void Tick(double now, ISession? session)
        {
            MultiplayerController? controller = MultiplayerController.Current;
            if (controller == null || GAT.PauseMenuBook.ActiveInstance == null && _step == 0)
            {
                return;
            }
            if (_start < 0)
            {
                _start = now;
            }
            double t = now - _start;
            string directory = System.IO.Path.Combine(Application.persistentDataPath, "ftmp_shots");
            System.IO.Directory.CreateDirectory(directory);
            switch (_step)
            {
                case 0 when t > 3:
                    _log.LogInfo($"[Scenario] startup page open={GAT.PauseMenuBook.ActiveInstance != null}, tutorial held={FamilyTimeMultiplayer.UI.StartupGate.IsHolding}");
                    _step++;
                    break;
                case 1 when t > 7:
                    FamilyTimeMultiplayer.Game.GameApiCompatibility.CaptureScreenshot(System.IO.Path.Combine(directory, "menu_offline.png"));
                    _step++;
                    break;
                case 2 when t > 9 && Environment.GetCommandLineArgs().Contains("-ftmp-menu-join"):
                    controller.PromptAndJoin();
                    FamilyTimeMultiplayer.Game.GameApiCompatibility.CaptureScreenshot(System.IO.Path.Combine(directory, "menu_join_prompt.png"));
                    _log.LogInfo($"[Scenario] join prompt open={controller.JoinPrompt.IsOpen}");
                    _step = 10;
                    break;
                case 2 when t > 9 && HasSaveImportScenarioArgument():
                    controller.OpenSaveImport();
                    _log.LogInfo("[Scenario] save import picker opened");
                    _step = 20;
                    break;
                case 20 when t > 25:
                    LogActiveCanvases();
                    LogSaveCollectionPaths();
                    FamilyTimeMultiplayer.Game.GameApiCompatibility.CaptureScreenshot(System.IO.Path.Combine(directory, "menu_save_import.png"));
                    _log.LogInfo("[Scenario] save import screenshot captured");
                    if (Environment.GetCommandLineArgs().Contains("-ftmp-save-import-e2e"))
                        _saveImportUiScenario = new SaveImportUiScenario(_log);
                    _step++;
                    break;
                case 10 when t > 12:
                    string code = Environment.GetCommandLineArgs().SkipWhile(a => a != "-ftmp-menu-join").Skip(1).First();
                    controller.JoinPrompt.Confirm(code);
                    _log.LogInfo($"[Scenario] typed '{code}' into Join; session={controller.Session?.Status ?? controller.LastError}");
                    _step++;
                    break;
                case 11 when t > 60:
                    _log.LogInfo($"[Scenario] after menu join: session={controller.Session?.Status ?? controller.LastError}");
                    _step++;
                    break;
                case 2 when t > 9:
                    controller.HostWith(FamilyTimeMultiplayer.Core.Protocol.FamilyMode.Shared);
                    _log.LogInfo($"[Scenario] menu host clicked, world ready={LocalPlayer.IsWorldReady}");
                    _step++;
                    break;
                case 3 when t > 40:
                    _log.LogInfo($"[Scenario] session={controller.Session?.Status ?? controller.LastError}");
                    _log.LogInfo($"[Scenario] after host: book open={GAT.PauseMenuBook.ActiveInstance != null && !GAT.PauseMenuBook.ActiveInstance.IsClosing}, tutorial held={FamilyTimeMultiplayer.UI.StartupGate.IsHolding}");
                    FamilyTimeMultiplayer.Game.GameApiCompatibility.CaptureScreenshot(System.IO.Path.Combine(directory, "menu_hosting.png"));
                    _log.LogInfo("[Scenario] menu screenshots captured");
                    _step++;
                    break;
            }
            _saveImportUiScenario?.Tick(now, controller);
        }

        private static bool HasSaveImportScenarioArgument() =>
            Environment.GetCommandLineArgs().Contains("-ftmp-save-import") ||
            Environment.GetCommandLineArgs().Contains("-ftmp-save-import-e2e");

        private void LogActiveCanvases()
        {
            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
                _log.LogInfo($"[Scenario] canvas {canvas.name} enabled={canvas.enabled} active={canvas.gameObject.activeInHierarchy} mode={canvas.renderMode} order={canvas.sortingOrder}");
            Canvas? dialogCanvas = canvases.FirstOrDefault(canvas => canvas.name == "FamilyTimeMultiplayerDialog");
            GameObject? dialog = dialogCanvas?.gameObject;
            RectTransform? panel = dialog == null ? null : dialog.transform.Find("Panel") as RectTransform;
            _log.LogInfo($"[Scenario] dialog={dialog?.activeInHierarchy}, panel={panel?.gameObject.activeInHierarchy}, rect={panel?.rect}, position={panel?.anchoredPosition}");
        }

        private void LogSaveCollectionPaths()
        {
            SaveCollectionService service = SaveCollectionService.RequireCurrent();
            SaveCollection originalCollection = service.SelectedCollection;
            MethodInfo resolver = AccessTools.Method(typeof(WorldSerializer), "GetPath", new[] { typeof(string) })
                ?? throw new MissingMethodException(nameof(WorldSerializer), "GetPath");
            try
            {
                foreach (SaveCollection collection in new[] { SaveCollection.Solo, SaveCollection.Multiplayer })
                {
                    service.Select(collection);
                    string actualPath = (string)resolver.Invoke(null, new object[] { "ftmp_save_routing_probe" })!;
                    string expectedPath = service.GetWorldPath("ftmp_save_routing_probe");
                    _log.LogInfo($"[Scenario] save path {collection}: {actualPath}, expected={expectedPath}, matches={actualPath == expectedPath}");
                }
                service.Select(SaveCollection.Multiplayer);
                string internalPath = (string)resolver.Invoke(null, new object[] { WorldFiles.HostSnapshotMapName })!;
                _log.LogInfo($"[Scenario] internal snapshot path: {internalPath}");
            }
            catch (Exception exception)
            {
                _log.LogError($"[Scenario] save path routing check failed: {exception}");
            }
            finally
            {
                service.Select(originalCollection);
            }
        }
    }

    private sealed class WatchPeerScenario : ScenarioRunner
    {
        private const float StandOffDistance = 3f;

        public override void Tick(double now, ISession? session)
        {
            if (session == null || !LocalPlayer.IsWorldReady || World.playerController == null)
            {
                return;
            }
            foreach (FamilyTimeMultiplayer.Core.Protocol.RosterEntry entry in session.Roster)
            {
                if (entry.PlayerId == session.LocalPlayerId || !session.TryGetRemotePlayerPosition(entry, out Vector3 peer))
                {
                    continue;
                }
                Vector3 self = LocalPlayer.Position;
                Vector3 toPeer = peer - self;
                toPeer.y = 0f;
                if (toPeer.sqrMagnitude < 0.01f)
                {
                    return;
                }
                Vector3 standAt = toPeer.magnitude > StandOffDistance * 1.5f ? peer - toPeer.normalized * StandOffDistance : self;
                World.playerController.TeleportTo(standAt, (peer + Vector3.up - (standAt + Vector3.up * 1.7f)).normalized);
                return;
            }
        }
    }

    private sealed class FollowThenWalkScenario : ScenarioRunner
    {
        private const double FollowDelaySeconds = 8.0;
        private const double WalkDelaySeconds = 12.0;
        private const double ProbeInterval = 3.0;

        private readonly ManualLogSource _log;
        private readonly WalkCircleScenario _walk;
        private readonly CommandProbe _probe;
        private double _liveSince = -1;
        private double _nextProbe;
        private bool _followIssued;

        public FollowThenWalkScenario(ManualLogSource log)
        {
            _log = log;
            _walk = new WalkCircleScenario(log);
            _probe = new CommandProbe(log);
        }

        public override void Tick(double now, ISession? session)
        {
            if (session == null || !LocalPlayer.IsWorldReady || session.Roster.Count < 2)
            {
                _liveSince = -1;
                return;
            }
            if (_liveSince < 0)
            {
                _liveSince = now;
            }
            if (!_followIssued && now - _liveSince >= FollowDelaySeconds)
            {
                _followIssued = true;
                IssueFollow();
            }
            if (now - _liveSince >= WalkDelaySeconds)
            {
                _walk.Tick(now, session);
            }
            if (now >= _nextProbe)
            {
                _nextProbe = now + ProbeInterval;
                _probe.Sample(session);
            }
        }

        private void IssueFollow()
        {
            CommandService? commands = World.commandService;
            if (commands == null)
            {
                _log.LogWarning("[Scenario] no CommandService");
                return;
            }
            FollowController follow = HarmonyLib.Traverse.Create(commands).Field<FollowController>("_follow").Value;
            int before = World.commandOwnershipService != null ? World.commandOwnershipService.AvailableWorkerCount : -1;
            follow.StartFollowing();
            int after = World.commandOwnershipService != null ? World.commandOwnershipService.AvailableWorkerCount : -1;
            _log.LogInfo($"[Scenario] issued FOLLOW as {ReplicationGate.Role}: available workers {before} -> {after}");
        }
    }

    private sealed class WalkCircleScenario : ScenarioRunner
    {
        private const float TurnDegreesPerSecond = 30f;

        private static readonly System.Reflection.MethodInfo UpdateMovementDirection =
            HarmonyLib.AccessTools.Method(typeof(PlayerController), "UpdateMovementDirection")
            ?? throw new MissingMethodException(nameof(PlayerController), "UpdateMovementDirection");

        private readonly ManualLogSource _log;
        private double _lastTime = -1;
        private bool _announced;

        public WalkCircleScenario(ManualLogSource log)
        {
            _log = log;
        }

        public override void Tick(double now, ISession? session)
        {
            PlayerController? controller = World.playerController;
            if (session == null || !LocalPlayer.IsWorldReady || controller == null)
            {
                _lastTime = -1;
                return;
            }
            if (!_announced)
            {
                _announced = true;
                _log.LogInfo($"[Scenario] walk-circle via movement input from {LocalPlayer.Position} as {LocalPlayer.PossessedHandle}");
            }
            float deltaTime = _lastTime < 0 ? 0f : (float)(now - _lastTime);
            _lastTime = now;
            var fields = HarmonyLib.Traverse.Create(controller);
            fields.Field<float>("yaw").Value += TurnDegreesPerSecond * deltaTime;
            fields.Field<Vector2>("_moveInput").Value = Vector2.up;
            UpdateMovementDirection.Invoke(controller, null);
        }
    }
}
