using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using UnityEngine;

namespace FamilyTimeMultiplayer.RuntimeHarness;

[BepInPlugin("dzejdies.familytime.runtime-harness", "FamilyTime Runtime Harness", "1.0.0")]
public sealed class RuntimeHarnessPlugin : BaseUnityPlugin
{
    private const string ScenarioArgument = "-ftmp-harness-scenario";
    private const string FamilyPhaseArgument = "-ftmp-family-phase";
    private const string FamilySnapshotArgument = "-ftmp-family-snapshot";
    private const string CompletionFileArgument = "-ftmp-harness-completion-file";
    private const string ReportDirectoryArgument = "-ftmp-report-dir";
    private const string FamilySaveName = "ftmp_stage53_family_roundtrip";
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private ScenarioReport? _report;
    private string _scenario = string.Empty;
    private float _startedAt;
    private string? _timeoutCondition;
    private BoundedWait? _conditionWait;
    private bool _complete;
    private NativeInputProbe? _nativeInputProbe;
    private bool _pupObserverInstalled;
    private Assembly? _pupAssembly;
    private object? _pupModInstance;
    private int _pupInputCount;
    private float _pupCaptureAt = -1;
    private string? _pupBeforeSnapshot;
    private int _tryWrapCountBefore;
    private int _beginWrapCountBefore;
    private int _beginUnwrapCountBefore;
    private string? _lastPupReadiness;
    private string _familyPhase = string.Empty;
    private bool _familySaveStarted;
    private bool _familySaveCallbackCompleted;
    private bool _familySaveSucceeded;
    private int _familySaveCallbackThreadId;
    private float _nextFamilyReadinessLog;
    private int _unityThreadId;

    private void Awake()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int scenarioIndex = Array.IndexOf(arguments, ScenarioArgument);
        if (scenarioIndex < 0 || scenarioIndex + 1 >= arguments.Length)
        {
            Logger.LogInfo("Harness is inactive; pass -ftmp-harness-scenario <family-persistence|family-peer|build-discovery|legal-build|pup-discovery|pup-interaction|child-visibility> to opt in.");
            enabled = false;
            return;
        }
        _scenario = arguments[scenarioIndex + 1];
        int phaseIndex = Array.IndexOf(arguments, FamilyPhaseArgument);
        _familyPhase = phaseIndex >= 0 && phaseIndex + 1 < arguments.Length ? arguments[phaseIndex + 1] : string.Empty;
        if (_scenario == "native-input")
        {
            typeof(Application).GetProperty("runInBackground")?.GetSetMethod()?.Invoke(null, new object[] { true });
            QualitySettings.SetQualityLevel(0, true);
            FamilyTimeMultiplayer.Addons.SessionAddonRegistry.Register(new ReplicationProbeAddon("runtime.shared", message => Logger.LogInfo($"[AddonProbe] {message}")));
            if (arguments.Contains("-ftmp-harness-host-only-addon"))
                FamilyTimeMultiplayer.Addons.SessionAddonRegistry.Register(new ReplicationProbeAddon("runtime.host-only", message => Logger.LogInfo($"[AddonProbe] {message}")));
        }
        _report = new ScenarioReport(_scenario, _runId);
        SetFact("scenario.phase", _familyPhase);
        if (_scenario == "native-input") _nativeInputProbe = new NativeInputProbe(FindStaticField, PropertyValue, SetFact, message => Logger.LogInfo($"[NativeInput] {message}"));
        _startedAt = Time.realtimeSinceStartup;
        _unityThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        AddEnvironmentFacts();
        Logger.LogInfo($"[RuntimeHarness] run={_runId} scenario={_scenario} started on Unity main thread");
    }

    private void Update()
    {
        try
        {
            _nativeInputProbe?.PrepareFixture();
            if (_complete || _report == null) return;
            CaptureCommonFacts();
            if (_nativeInputProbe != null)
            {
                ScenarioVerdict? verdict = _nativeInputProbe.Tick(IsWorldReady());
                if (verdict.HasValue) Finish(verdict.Value, "Native input lifecycle and hover probe completed; inspect captured facts.");
                else FinishOnTimeout(IsWorldReady() ? "native-input" : "world-ready", "Native input probe prerequisites did not become ready.");
                return;
            }
            if (_scenario == "family-peer")
            {
                WaitForFamilyPeerCompletion();
                return;
            }
            if (!IsWorldReady())
            {
                FinishOnTimeout("world-ready", "Native world and controlled player were not ready before the bounded wait expired.");
                return;
            }
            if (_scenario == "pup-discovery")
            {
                InspectPupCandidates();
                return;
            }
            if (_scenario == "child-visibility")
            {
                ObserveChildVisibility();
                return;
            }
            if (_scenario == "pup-interaction")
            {
                ObservePupInteraction();
                return;
            }
            if (_scenario == "build-discovery")
            {
                CaptureBuildFacts();
                Finish(ScenarioVerdict.BlockedPrecondition, "Read-only build inspection completed; no selected legal plan, materials, or authorized client construction request was established.");
                return;
            }
            if (_scenario == "family-persistence")
            {
                RunFamilyPersistence();
                return;
            }
            if (_scenario is "family-save" or "legal-build")
            {
                InspectMultiplayerPreconditions();
                return;
            }
            Finish(ScenarioVerdict.BlockedEnvironment, $"Unknown harness scenario '{_scenario}'.");
        }
        catch (Exception exception)
        {
            _report?.AddFact("exception", exception.ToString());
            Finish(ScenarioVerdict.Fail, "Diagnostic inspection threw before it could classify the scenario.");
        }
    }

    private void CaptureCommonFacts()
    {
        SetFact("unity.thread", "main");
        SetFact("unity.frame", Time.frameCount.ToString());
        SetFact("unity.worldReady", IsWorldReady().ToString());
        CaptureSessionFacts();
    }

    private bool IsWorldReady()
    {
        Type? localPlayer = FindType("FamilyTimeMultiplayer.Game.LocalPlayer");
        return localPlayer?.GetProperty("IsWorldReady", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is true;
    }

    private void CaptureSessionFacts()
    {
        object? controller = FindType("FamilyTimeMultiplayer.MultiplayerController")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        object? session = controller?.GetType().GetProperty("Session")?.GetValue(controller);
        SetFact("session.status", Property(session, "Status") ?? "none");
        SetFact("session.role", Property(session, "Role") ?? "none");
        SetFact("session.isLive", Property(session, "IsLive") ?? "false");
        SetFact("session.familyMode", Property(session, "FamilyMode") ?? "none");
        SetFact("session.remoteLivePlayers", CountEnumerable(PropertyValue(session, "RemotePlayers")).ToString());
        object? roster = PropertyValue(session, "Roster");
        SetFact("session.roster", roster is ICollection collection ? collection.Count.ToString() : CountEnumerable(roster).ToString());
        if (roster is IEnumerable entries)
        {
            int index = 0;
            foreach (object? entry in entries)
            {
                SetFact($"session.player.{index++}", $"{Property(entry, "PlayerId")}:{Property(entry, "Name")}");
            }
        }
    }

    private void InspectMultiplayerPreconditions()
    {
        int rosterCount = ParseInt(_report!.Facts.TryGetValue("session.roster", out string? value) ? value : "0");
        string familyMode = _report.Facts.TryGetValue("session.familyMode", out string? mode) ? mode : "none";
        string role = _report.Facts.TryGetValue("session.role", out string? sessionRole) ? sessionRole : "none";
        bool sessionIsLive = _report.Facts.TryGetValue("session.isLive", out string? isLive) && bool.TryParse(isLive, out bool parsedIsLive) && parsedIsLive;
        int liveRemotePlayers = ParseInt(_report.Facts.TryGetValue("session.remoteLivePlayers", out string? remoteCount) ? remoteCount : "0");
        bool livePair = SessionReadiness.HasLivePair(role == "Host", sessionIsLive, rosterCount, liveRemotePlayers);
        SetFact("precondition.livePair", livePair.ToString());
        if (_scenario == "family-save")
        {
            CaptureFamilyFacts();
            SetFact("precondition.familyModeSeparate", (familyMode == "Separate").ToString());
            if (!livePair)
            {
                FinishOnTimeout("live-host-client-pair", "A Live host-client pair was not established before the bounded wait expired.");
                return;
            }
            Finish(ScenarioVerdict.BlockedPrecondition, "Legacy family-save preflight does not execute a persistence roundtrip; use family-persistence capture/restore phases.");
            return;
        }
        CaptureBuildFacts();
        if (!livePair)
        {
            FinishOnTimeout("live-host-client-pair", "A Live host-client pair was not established before the bounded wait expired.");
            return;
        }
        Finish(ScenarioVerdict.BlockedPrecondition, "Preflight only: this inspector does not send construction requests or prove placement, materials, replication, or rejection.");
    }

    private void CaptureFamilyFacts()
    {
        _report!.RemoveFactsByPrefix("family.semantic.");
        SetFact("family.childOwnershipDuplicate", string.Empty);
        Type? serviceType = FindType("FamilyTimeMultiplayer.Families.HostFamilyService");
        object? service = serviceType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        object? record = service?.GetType().GetMethod("ToRecord")?.Invoke(service, null);
        object? families = PropertyValue(record, "Families");
        int index = 0;
        var ownedMembers = new Dictionary<ushort, string>();
        object? mamaService = FindType("FamilyTimeMultiplayer.Families.HostMamaService")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        object? originalMama = StaticMember(FindType("GAT.WolfMama"), "ActiveInstance");
        if (families is IEnumerable entries)
        {
            foreach (object? family in entries)
            {
                string owner = Property(family, "Owner") ?? "missing";
                bool isHost = bool.TryParse(Property(family, "IsHostFamily"), out bool hostValue) && hostValue;
                object? members = PropertyValue(family, "Members");
                ushort[] memberIds = ToStableIds(members);
                foreach (ushort memberId in memberIds)
                {
                    if (ownedMembers.TryGetValue(memberId, out string? previousOwner))
                        SetFact("family.childOwnershipDuplicate", $"{memberId}:{previousOwner}|{owner}");
                    else ownedMembers.Add(memberId, owner);
                }
                string identity = Property(family, "PublicKey") ?? owner;
                string semanticKey = "family.semantic." + Convert.ToBase64String(Encoding.UTF8.GetBytes(identity));
                bool mamaAssigned = isHost ? originalMama != null : GetMamaAgent(mamaService, owner) is not null;
                string mamaKind = !mamaAssigned ? "none" : isHost ? "original" : "clone";
                string childTemplates = DescribeChildTemplates(memberIds);
                SetFact($"family.{index}", $"owner={owner};identity={identity};host={isHost};children={memberIds.Length};mama={Property(family, "MamaAgent") ?? (isHost && mamaAssigned ? "original" : "none")}");
                SetFact($"family.{index}.owner", owner);
                SetFact($"family.{index}.identity", identity);
                SetFact($"family.{index}.isHost", isHost.ToString());
                SetFact($"family.{index}.childCount", memberIds.Length.ToString());
                SetFact($"family.{index}.childStableIds", string.Join(",", memberIds.OrderBy(id => id)));
                SetFact($"family.{index}.mamaAssigned", mamaAssigned.ToString());
                SetFact($"family.{index}.mamaKind", mamaKind);
                SetFact($"family.{index}.childTemplates", childTemplates);
                SetFact(semanticKey + ".identity", identity);
                SetFact(semanticKey + ".host", isHost.ToString());
                SetFact(semanticKey + ".children", memberIds.Length.ToString());
                SetFact(semanticKey + ".childTemplates", childTemplates);
                SetFact(semanticKey + ".mamaAssigned", mamaAssigned.ToString());
                SetFact(semanticKey + ".mamaKind", mamaKind);
                index++;
            }
        }
        SetFact("family.count", index.ToString());
        SetFact("family.childOwnershipMappings", string.Join("|", ownedMembers.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
        SetFact("family.childOwnershipUnique", string.IsNullOrEmpty(_report.Facts.GetValueOrDefault("family.childOwnershipDuplicate", string.Empty)).ToString());
        SetFact("family.totalOwnedChildren", ownedMembers.Count.ToString());
        SetFact("family.liveChildCount", CountLiveChildren().ToString());
        SetFact("family.mamaAssignments", string.Join("|", Enumerable.Range(0, index).Select(i => $"{_report.Facts[$"family.{i}.identity"]}:{_report.Facts[$"family.{i}.mamaKind"]}")));
        Type? mamaLookup = FindType("FamilyTimeMultiplayer.Holds.MamaLookup");
        object? mamas = mamaLookup?.GetMethod("All", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        SetFact("family.wolfMamaObjects", CountEnumerable(mamas).ToString());
        Type? familyView = FindType("FamilyTimeMultiplayer.Families.LocalFamilyView");
        object? localMembers = familyView?.GetMethod("PhysicalMembers", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        SetFact("family.localMappedChildren", CountEnumerable(localMembers).ToString());
    }

    private void RunFamilyPersistence()
    {
        if (_familyPhase is not ("capture" or "restore"))
        {
            Finish(ScenarioVerdict.BlockedEnvironment, "Set -ftmp-family-phase capture or restore.");
            return;
        }
        if (!HasLiveFamilyPair())
        {
            FinishOnTimeout("live-host-client-pair", "A Live host-client pair was not established before the bounded wait expired.");
            return;
        }
        CaptureFamilyFacts();
        string missing = MissingFamilyPreconditions();
        SetFact("precondition.familyModeSeparate", (Property(FindCurrentSession(), "FamilyMode") == "Separate").ToString());
        SetFact("precondition.familyState", missing.Length == 0 ? "ready" : missing);
        if (missing.Length > 0)
        {
            if (Time.realtimeSinceStartup >= _nextFamilyReadinessLog)
            {
                _nextFamilyReadinessLog = Time.realtimeSinceStartup + 10f;
                Logger.LogInfo($"[FamilyPersistence] waiting for family state: {missing}");
            }
            FinishOnTimeout("family-state", $"Required family ownership, child, or Mama mappings did not become ready: {missing}");
            return;
        }
        if (Property(FindCurrentSession(), "FamilyMode") != "Separate")
        {
            Finish(ScenarioVerdict.BlockedPrecondition, "The active session is not using separate families.");
            return;
        }
        if (_familyPhase == "capture")
        {
            if (!_familySaveStarted) StartNativeFamilySave();
            if (_familySaveCallbackCompleted) CompleteNativeFamilySave();
            return;
        }
        VerifyFamilyRestore();
    }

    private string MissingFamilyPreconditions()
    {
        return string.Join(",", FamilyPersistencePreconditions.FindMissing(_report!.Facts));
    }

    private void StartNativeFamilySave()
    {
        if (System.Threading.Thread.CurrentThread.ManagedThreadId != _unityThreadId)
        {
            Finish(ScenarioVerdict.Fail, "Native save was requested off the Unity main thread.");
            return;
        }
        object? serializer = FindStaticField("GAT.World", "worldSerializer");
        MethodInfo? save = serializer?.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "Save" && method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType == typeof(string));
        if (serializer == null || save == null)
        {
            Finish(ScenarioVerdict.BlockedEnvironment, "Could not locate the native WorldSerializer.Save(string, callback) API.");
            return;
        }
        string path = ResolveNativeSavePath(FamilySaveName);
        SetFact("operation.nativeSaveCompleted", "pending");
        SetFact("save.name", FamilySaveName);
        SetFact("save.path", path);
        Action<bool> callback = success =>
        {
            _familySaveSucceeded = success;
            _familySaveCallbackThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            _familySaveCallbackCompleted = true;
            Logger.LogInfo($"[FamilyPersistence] native save callback success={success}");
        };
        Delegate nativeCallback = CreateCallback(save.GetParameters()[1].ParameterType, callback);
        try
        {
            save.Invoke(serializer, new object[] { FamilySaveName, nativeCallback });
            _familySaveStarted = true;
            Logger.LogInfo($"[FamilyPersistence] native save started at {path}");
        }
        catch (Exception exception)
        {
            Finish(ScenarioVerdict.Fail, $"Native save invocation failed: {exception}");
        }
    }

    private void CompleteNativeFamilySave()
    {
        if (_familySaveCallbackThreadId != _unityThreadId)
        {
            Finish(ScenarioVerdict.Fail, "Native save callback ran off the Unity main thread; persistence evidence was not collected.");
            return;
        }
        string path = _report!.Facts["save.path"];
        bool fileReady = _familySaveSucceeded && File.Exists(path) && new FileInfo(path).Length > 0;
        SetFact("operation.nativeSaveCompleted", fileReady.ToString());
        if (!fileReady)
        {
            Finish(_familySaveSucceeded ? ScenarioVerdict.Fail : ScenarioVerdict.Fail,
                $"Native serializer callback completed with success={_familySaveSucceeded}, but the save file was missing, empty, or unsuccessful: {path}");
            return;
        }
        SetFact("save.fileBytes", new FileInfo(path).Length.ToString());
        SetFact("save.sha256", Hash(path));
        string snapshotPath = ResolveArgumentValue(FamilySnapshotArgument) ?? Path.Combine(ReportDirectory(), "family-snapshot.tsv");
        File.WriteAllText(snapshotPath, FamilySnapshotCodec.Write(_report.Facts), Encoding.UTF8);
        SetFact("save.semanticSnapshot", snapshotPath);
        string checkpointPath = Path.Combine(ReportDirectory(), $"family-capture-{_runId}.json");
        File.WriteAllText(checkpointPath, _report.ToJson(), Encoding.UTF8);
        Logger.LogInfo($"[FamilyPersistenceCheckpoint] save={path} snapshot={snapshotPath} report={checkpointPath}");
        SignalFamilyPeerCompletion("native save checkpoint ready");
        _complete = true;
        LeaveSessionAndQuit();
    }

    private void VerifyFamilyRestore()
    {
        string? snapshotPath = ResolveArgumentValue(FamilySnapshotArgument);
        if (snapshotPath == null || !File.Exists(snapshotPath))
        {
            Finish(ScenarioVerdict.BlockedEnvironment, "The semantic family snapshot is missing; restore comparison cannot run.");
            return;
        }
        IReadOnlyDictionary<string, string> expected;
        try { expected = FamilySnapshotCodec.Read(File.ReadAllText(snapshotPath, Encoding.UTF8)); }
        catch (Exception exception)
        {
            Finish(ScenarioVerdict.BlockedEnvironment, $"Could not read the semantic family snapshot: {exception}");
            return;
        }
        IReadOnlyList<string> differences = FamilySnapshotCodec.Compare(expected, _report!.Facts);
        string savePath = ResolveNativeSavePath(FamilySaveName);
        bool nativeSaveExists = File.Exists(savePath) && new FileInfo(savePath).Length > 0;
        SetFact("operation.nativeSaveCompleted", nativeSaveExists.ToString());
        SetFact("save.path", savePath);
        SetFact("save.reloadedAfterRestart", IsWorldReady().ToString());
        SetFact("restore.semanticDifferences", string.Join(" | ", differences));
        SetFact("restore.semanticMatch", (differences.Count == 0).ToString());
        if (!nativeSaveExists)
        {
            Finish(ScenarioVerdict.Fail, $"The expected native save is missing after process restart: {savePath}");
            return;
        }
        if (differences.Count > 0)
        {
            Finish(ScenarioVerdict.Fail, "Restored family owners, children, or Mama assignments differ from the pre-save semantic snapshot.");
            return;
        }
        Finish(ScenarioVerdict.Pass, "Native save, process restart, host load, client reconnect, and semantic family-state comparison all succeeded.");
    }

    private void WaitForFamilyPeerCompletion()
    {
        string? signal = ResolveArgumentValue(CompletionFileArgument);
        if (signal == null || !File.Exists(signal)) return;
        Logger.LogInfo($"[FamilyPeer] completion signal found: {File.ReadAllText(signal).Trim()}");
        LeaveSessionAndQuit();
    }

    private bool HasLiveFamilyPair()
    {
        string role = _report!.Facts.GetValueOrDefault("session.role", "none");
        int roster = ParseInt(_report.Facts.GetValueOrDefault("session.roster", "0"));
        bool live = bool.TryParse(_report.Facts.GetValueOrDefault("session.isLive", "false"), out bool value) && value;
        int remotes = ParseInt(_report.Facts.GetValueOrDefault("session.remoteLivePlayers", "0"));
        return SessionReadiness.HasLivePair(role == "Host", live, roster, remotes);
    }

    private object? FindCurrentSession()
    {
        object? controller = FindType("FamilyTimeMultiplayer.MultiplayerController")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return controller?.GetType().GetProperty("Session")?.GetValue(controller);
    }

    private ushort[] ToStableIds(object? members)
    {
        if (members is not IEnumerable values) return Array.Empty<ushort>();
        return values.Cast<object?>().Where(value => value != null).Select(value => ParseUShort(value!.ToString()!)).Where(value => value != ushort.MaxValue).ToArray();
    }

    private string DescribeChildTemplates(IEnumerable<ushort> memberIds)
    {
        object? gat = FindStaticField("GAT.World", "gat");
        Type? gatType = FindType("GAT.GAT");
        Type? handleType = FindType("GAT.Handle");
        MethodInfo? getTemplate = gatType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetTemplateFromHandle" && method.GetParameters().Length == 1);
        if (gat == null || handleType == null || getTemplate == null) return "unavailable";
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (ushort memberId in memberIds)
        {
            object? handle = CreateStableHandle(handleType, memberId);
            object? template = handle == null ? null : SafeInvoke(getTemplate, gat, new[] { handle });
            string name = Property(template, "name") ?? "unresolved";
            counts[name] = counts.TryGetValue(name, out int count) ? count + 1 : 1;
        }
        return string.Join(",", counts.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static object? CreateStableHandle(Type handleType, ushort stableId)
    {
        object handle = Activator.CreateInstance(handleType)!;
        FieldInfo? field = handleType.GetField("stableIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(handle, stableId);
            return handle;
        }
        PropertyInfo? property = handleType.GetProperty("stableIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.CanWrite == true)
        {
            property.SetValue(handle, stableId);
            return handle;
        }
        return null;
    }

    private static object? GetMamaAgent(object? mamaService, string owner)
    {
        return mamaService?.GetType().GetMethod("AgentOf")?.Invoke(mamaService, new object[] { owner });
    }

    private int CountLiveChildren()
    {
        object? gat = FindStaticField("GAT.World", "gat");
        Type? gatType = FindType("GAT.GAT");
        int workerIndex = ParseInt(Property(FindStaticField("GAT.World", "commandOwnershipService"), "WorkerTemplateIndex") ?? "-1");
        MethodInfo? entries = gatType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetTemplateEntries" && method.GetParameters().Length == 0);
        if (workerIndex < 0 || entries == null || gat == null || SafeInvoke(entries, gat, Array.Empty<object>()) is not IEnumerable templateEntries)
            return -1;
        object? workerEntry = templateEntries.Cast<object?>().ElementAtOrDefault(workerIndex);
        return ParseInt(Property(workerEntry, "count") ?? "-1");
    }

    private static Delegate CreateCallback(Type callbackType, Action<bool> callback)
    {
        MethodInfo invoke = callbackType.GetMethod("Invoke") ?? throw new MissingMethodException(callbackType.FullName, "Invoke");
        ParameterInfo[] signature = invoke.GetParameters();
        if (signature.Length != 1 || signature[0].ParameterType != typeof(bool))
            throw new InvalidOperationException($"Unexpected native save callback signature: {callbackType.FullName}");
        ParameterExpression result = Expression.Parameter(typeof(bool), "success");
        MethodInfo callbackInvoke = typeof(Action<bool>).GetMethod("Invoke")!;
        return Expression.Lambda(callbackType, Expression.Call(Expression.Constant(callback), callbackInvoke, result), result).Compile();
    }

    private static string ResolveNativeSavePath(string mapName)
    {
        Type? serviceType = FindType("FamilyTimeMultiplayer.Game.SaveCollectionService");
        object? service = serviceType?.GetMethod("RequireCurrent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
        object? path = service?.GetType().GetMethod("GetWorldPath")?.Invoke(service, new object[] { mapName });
        return path as string ?? throw new InvalidOperationException("SaveCollectionService did not return the native world path.");
    }

    private static string? ResolveArgumentValue(string argument)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, argument);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    private static string ReportDirectory()
    {
        string? directory = ResolveArgumentValue(ReportDirectoryArgument);
        return directory ?? Path.Combine(Application.persistentDataPath, "FamilyTimeMultiplayerTestReports");
    }

    private void SignalFamilyPeerCompletion(string status)
    {
        string? path = ResolveArgumentValue(CompletionFileArgument);
        if (path == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, status, Encoding.UTF8);
        Logger.LogInfo($"[FamilyPersistence] completion signal written to {path}: {status}");
    }

    private void LeaveSessionAndQuit()
    {
        try
        {
            object? controller = FindType("FamilyTimeMultiplayer.MultiplayerController")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            controller?.GetType().GetMethod("Leave")?.Invoke(controller, null);
        }
        catch (Exception exception)
        {
            Logger.LogError($"[RuntimeHarness] clean session shutdown failed: {exception}");
        }
        Application.Quit();
    }

    private void CaptureBuildFacts()
    {
        object? player = FindStaticField("GAT.World", "playerController");
        SetFact("build.position", PropertyValue(player, "CachedInstancePosition")?.ToString() ?? "unavailable");
        object? controller = FindStaticField("GAT.World", "playerController");
        object? tools = PropertyValue(controller, "Tools");
        SetFact("build.toolCount", CountEnumerable(tools).ToString());
        SetFact("build.tools", FormatValue(tools));
        object? terrain = FindStaticField("GAT.World", "heightfield");
        SetFact("build.heightfieldAvailable", (terrain != null).ToString());
        object? parts = FindStaticField("GAT.World", "buildPartManager");
        object? templates = PropertyValue(parts, "BuildPartTemplates");
        SetFact("build.templateCount", CountEnumerable(templates).ToString());
        SetFact("build.recipes", DescribeBuildRecipes(templates));
        SetFact("build.placementTarget", DescribeCurrentBuildPlan());
        SetFact("build.heldMaterials", DescribeHeldMaterials());
        SetFact("build.authorizedRequestSent", "false");
        SetFact("build.authority", "HostToolAuthority.Validate remains enabled; this read-only inspector did not send a request.");
    }

    private string DescribeBuildRecipes(object? templates)
    {
        if (templates is not IEnumerable values) return "unavailable";
        var descriptions = new List<string>();
        Type? compatibility = FindType("FamilyTimeMultiplayer.Game.GameApiCompatibility");
        MethodInfo? countMaterial = compatibility?.GetMethod("BuildPartMaterialCount", BindingFlags.Public | BindingFlags.Static);
        Type? materialType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeGetTypes)
            .FirstOrDefault(type => type.IsEnum && type.Name == "ConstructionMaterial");
        object[] materials = materialType == null ? Array.Empty<object>() : Enum.GetValues(materialType).Cast<object>().ToArray();
        foreach (object? part in values)
        {
            if (part == null) continue;
            string cost = countMaterial == null || materials.Length == 0 ? "unavailable" : string.Join(",", materials
                .Select(material => $"{material}={countMaterial.Invoke(null, new[] { part, material })}")
                .Where(value => !value.EndsWith("=0", StringComparison.Ordinal)));
            descriptions.Add($"{Property(part, "name") ?? part.GetType().Name}[{cost}]");
        }
        return string.Join("|", descriptions);
    }

    private string DescribeCurrentBuildPlan()
    {
        Camera? camera = Camera.main;
        if (camera == null) return "unavailable:no-camera";
        MethodInfo? method = FindType("FamilyTimeMultiplayer.Game.GameApiCompatibility")?.GetMethod("FindCommandPlan", BindingFlags.Public | BindingFlags.Static);
        if (method == null) return "unavailable:FindCommandPlan";
        object? plan = method.Invoke(null, new object[] { camera.transform, 30f });
        return plan == null ? "none-under-camera" : $"valid:{FormatValue(plan)}";
    }

    private string DescribeHeldMaterials()
    {
        object? hand = FindStaticField("GAT.PlayerController", "hand");
        MethodInfo? collect = hand?.GetType().GetMethod("CollectAllCarriedRigids", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        object? rigids = collect?.Invoke(hand, null);
        return rigids is IEnumerable values ? string.Join(",", values.Cast<object?>().Select(value => Property(value, "name") ?? value?.GetType().Name ?? "null")) : "unavailable";
    }

    private void InspectPupCandidates()
    {
        Assembly? pupAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name?.Contains("Burritos", StringComparison.OrdinalIgnoreCase) == true);
        if (pupAssembly == null)
        {
            Finish(ScenarioVerdict.BlockedEnvironment, "Pup Burritos assembly is not loaded in this process.");
            return;
        }
        SetFact("mod.name", pupAssembly.GetName().Name ?? "unknown");
        SetFact("mod.version", pupAssembly.GetName().Version?.ToString() ?? "unknown");
        Type[] types = SafeGetTypes(pupAssembly);
        MethodInfo[] eligibility = types.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.Name == "IsPup").ToArray();
        SetFact("mod.eligibilityMethods", string.Join("|", eligibility.Select(Signature)));
        object? modInstance = Resources.FindObjectsOfTypeAll<Component>()
            .FirstOrDefault(component => component != null && component.GetType().Assembly == pupAssembly);
        SetFact("mod.instanceFound", (modInstance != null).ToString());
        FieldInfo[] pupTypeFields = types.SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(field => field.Name.Equals("pupTypes", StringComparison.OrdinalIgnoreCase)).ToArray();
        SetFact("mod.pupTypeFields", string.Join("|", pupTypeFields.Select(field => $"{field.DeclaringType?.FullName}.{field.Name}:{field.FieldType.FullName}={FormatValue(SafeRead(field, field.IsStatic ? null : modInstance))}")));
        List<string> candidates = FindSceneCandidates(pupTypeFields, eligibility, modInstance);
        SetFact("pup.candidateCount", candidates.Count.ToString());
        int eligibleCount = candidates.Count(candidate => candidate.Contains("isPup=true", StringComparison.OrdinalIgnoreCase));
        SetFact("pup.eligibleCount", eligibleCount.ToString());
        for (int index = 0; index < candidates.Count; index++) SetFact($"pup.candidate.{index}", candidates[index]);
        if (eligibility.Length == 0)
        {
            Finish(ScenarioVerdict.BlockedEnvironment, "Could not locate the mod's IsPup eligibility method; candidate names cannot be treated as eligible.");
        }
        else if (eligibleCount == 0)
        {
            Finish(ScenarioVerdict.BlockedPrecondition, "No live candidate passed the mod's IsPup method; no J input was sent.");
        }
        else
        {
            Finish(ScenarioVerdict.Pass, $"Discovery found {candidates.Count} live child candidates; {eligibleCount} passed the mod's actual IsPup method. No interaction or synchronization behavior was tested in this scenario.");
        }
    }

    private void ObservePupInteraction()
    {
        _pupAssembly ??= AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name?.Contains("Burritos", StringComparison.OrdinalIgnoreCase) == true);
        if (_pupAssembly == null)
        {
            Finish(ScenarioVerdict.BlockedEnvironment, "Pup Burritos assembly is not loaded in this process.");
            return;
        }
        if (!_pupObserverInstalled)
        {
            PupActionObserver.Install(_pupAssembly, Logger);
            _pupObserverInstalled = true;
            _pupModInstance = Resources.FindObjectsOfTypeAll<Component>()
                .FirstOrDefault(component => component != null && component.GetType().Assembly == _pupAssembly);
            SetFact("pup.observerInstalled", "true");
            SetFact("pup.scene.before", PupActionObserver.CaptureScene(_pupAssembly));
        }
        if (_pupCaptureAt >= 0)
        {
            if (Time.realtimeSinceStartup >= _pupCaptureAt) CompletePupInputSample();
            return;
        }
        string readiness = CapturePupInteractionReadiness();
        string readinessKey = PupObservation.ReadinessLogKey(readiness);
        if (PupObservation.ShouldLogReadiness(_lastPupReadiness, readinessKey))
        {
            _lastPupReadiness = readinessKey;
            Logger.LogInfo($"[PupInteraction] waiting for J: {DescribeJInput()}; readiness={readiness}");
        }
        if (!IsJPressedThisFrame())
        {
            if (_pupInputCount == 0)
            {
                SetFact("pup.failureDomain", "gui-input-or-window-focus");
                SetFact("pup.input.keyboard", DescribeJInput());
                FinishOnTimeout("unity-j-input", "Unity did not observe a J key event in the focused game window before the bounded wait expired.");
            }
            return;
        }
        if (_pupInputCount >= 2)
        {
            Finish(ScenarioVerdict.Fail, "More than two J input events were sent before the observation completed.");
            return;
        }
        _pupInputCount++;
        _pupBeforeSnapshot = PupActionObserver.CaptureScene(_pupAssembly);
        _tryWrapCountBefore = PupActionObserver.TryWrapCalls;
        _beginWrapCountBefore = PupActionObserver.BeginWrapCalls;
        _beginUnwrapCountBefore = PupActionObserver.BeginUnwrapCalls;
        _pupCaptureAt = Time.realtimeSinceStartup + 5.5f;
        SetFact($"pupAction.{_pupInputCount}.input", "J detected by Unity Input System");
        SetFact("pup.failureDomain", "gameplay-eligibility-or-action");
        SetFact($"pupAction.{_pupInputCount}.preInputReadiness", readiness);
        Logger.LogInfo($"[PupInteraction] J input #{_pupInputCount} observed; waiting 5.5s for the mod operation to settle.");
    }

    private void ObserveChildVisibility()
    {
        int sample = GetVisibilitySampleInput();
        if (sample != 0)
        {
            string snapshot = CaptureChildVisibilitySnapshot();
            SetFact($"visibility.sample.{sample}", snapshot);
            Logger.LogInfo($"[ChildVisibility] sample={sample} {snapshot}");
            if (!string.IsNullOrEmpty(_report!.Facts.GetValueOrDefault("visibility.sample.1"))
                && !string.IsNullOrEmpty(_report.Facts.GetValueOrDefault("visibility.sample.2")))
            {
                Finish(ScenarioVerdict.Pass, "Captured two operator-triggered child visibility samples. Compare camera chunks, child transforms, and GAT culling state.");
                return;
            }
        }
        FinishOnTimeout("two-visibility-samples", "Unity did not observe Ctrl+Shift+F8 and Ctrl+Shift+F10 before the bounded wait expired.");
    }

    private string CaptureChildVisibilitySnapshot()
    {
        object? gat = FindStaticField("GAT.World", "gat");
        object? chunkManager = FindSceneComponent("GAT.GATChunkManager");
        object? listener = PropertyValue(chunkManager, "chunkChangeListener");
        Type? gatType = FindType("GAT.GAT");
        int count = ParseInt(Property(gat, "instanceCount") ?? "0");
        int childTemplateIndex = ParseInt(Property(FindStaticField("GAT.World", "commandOwnershipService"), "WorkerTemplateIndex") ?? "-1");
        MethodInfo? getHandle = FindInstanceMethod(gatType, "GetHandle", typeof(ushort));
        MethodInfo? getTemplate = FindInstanceMethod(gatType, "GetTemplateFromHandle", null);
        MethodInfo? getTemplateIndex = FindInstanceMethod(gatType, "GetTemplateIndexFromPhysical", typeof(int));
        MethodInfo? getMatrix = FindInstanceMethod(gatType, "GetInstanceMatrixImmediate", null);
        var children = new List<string>();
        if (gat != null && getHandle != null && getTemplate != null && getTemplateIndex != null && getMatrix != null)
        {
            for (int index = 0; index < count; index++)
            {
                object? handle = SafeInvoke(getHandle, gat, new object[] { checked((ushort)index) });
                if (handle == null || ParseInt(SafeInvoke(getTemplateIndex, gat, new object[] { index })?.ToString() ?? "-1") != childTemplateIndex) continue;
                object? template = SafeInvoke(getTemplate, gat, new[] { handle });
                if (SafeInvoke(getMatrix, gat, new[] { handle }) is not Matrix4x4 matrix) continue;
                children.Add(DescribeChildVisibility(index, handle, template, matrix, chunkManager, listener));
            }
        }
        Camera? camera = Camera.main;
        Vector3 cameraPosition = camera != null ? camera.transform.position : default;
        object? currentChunk = PropertyValue(listener, "CurrentCameraChunk");
        return $"camera={cameraPosition};cameraChunk={FormatValue(currentChunk)};chunkRadius={Property(listener, "chunkRadius") ?? "unavailable"};chunkManager={DescribeCullManager(chunkManager)};gatInstanceCount={count};childInstances={children.Count};children=[{string.Join("|", children)}]";
    }

    private string DescribeChildVisibility(int physicalIndex, object handle, object? template, Matrix4x4 matrix, object? chunkManager, object? listener)
    {
        Vector3 position = matrix.GetColumn(3);
        object? chunk = InvokeByName(chunkManager, "GetChunkCoord", position);
        object? visibleChunks = PropertyValue(chunkManager, "visibleChunks");
        bool isVisible = ContainsValue(visibleChunks, chunk);
        int cached = ParseInt(InvokeByName(chunkManager, "GetCachedInstanceCount", chunk)?.ToString() ?? "0");
        bool chunkCullable = PropertyValue(template, "chunkCullable") is bool cullable && cullable;
        string culling = ChunkVisibilityClassification.Classify(chunkCullable, isVisible, cached);
        string stableId = Property(handle, "stableIndex") ?? "unknown";
        return $"physical={physicalIndex};stableId={stableId};template={Property(template, "name") ?? "unknown"};position={position};chunk={FormatValue(chunk)};distanceToCamera={Vector3.Distance(Camera.main != null ? Camera.main.transform.position : default, position):0.00};classification={culling}";
    }

    private static string DescribeCullManager(object? chunkManager)
    {
        if (chunkManager == null) return "missing";
        return $"initialized={FormatValue(PropertyValue(chunkManager, "initialized"))};cullRequested={FormatValue(PropertyValue(chunkManager, "cullRequested"))};readbackInFlight={FormatValue(PropertyValue(chunkManager, "cullReadbackInFlight"))};pendingRestores={CollectionCount(PropertyValue(chunkManager, "pendingRestoreCommands"))};pendingRestoreCallbacks={FormatValue(PropertyValue(chunkManager, "pendingRestoreCallbackCount"))}";
    }

    private static object? FindSceneComponent(string typeName)
    {
        Type? expectedType = FindType(typeName);
        return expectedType == null ? null : Resources.FindObjectsOfTypeAll<Component>()
            .FirstOrDefault(component => component != null && expectedType.IsInstanceOfType(component));
    }

    private static MethodInfo? FindInstanceMethod(Type? type, string methodName, Type? parameterType)
    {
        return type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == methodName && (parameterType == null
                ? method.GetParameters().Length == 1
                : HasSingleParameter(method, parameterType)));
    }

    private static object? InvokeByName(object? target, string methodName, object? argument)
    {
        if (target == null) return null;
        MethodInfo? method = FindInstanceMethod(target.GetType(), methodName, argument?.GetType());
        return method == null ? null : SafeInvoke(method, target, new[] { argument! });
    }

    private static bool ContainsValue(object? sequence, object? value)
    {
        return sequence is IEnumerable values && values.Cast<object?>().Any(item => Equals(item, value));
    }

    private static int CollectionCount(object? value) => value is ICollection collection ? collection.Count : -1;

    private static int GetVisibilitySampleInput()
    {
        Type? keyboardType = FindType("UnityEngine.InputSystem.Keyboard") ?? FindType("Unity.InputSystem.Keyboard");
        object? keyboard = StaticMember(keyboardType, "current");
        bool control = PropertyValue(PropertyValue(keyboard, "ctrlKey"), "isPressed") is true;
        bool shift = PropertyValue(PropertyValue(keyboard, "shiftKey"), "isPressed") is true;
        bool f8 = PropertyValue(PropertyValue(keyboard, "f8Key"), "wasPressedThisFrame") is true;
        bool f10 = PropertyValue(PropertyValue(keyboard, "f10Key"), "wasPressedThisFrame") is true;
        return VisibilityCaptureTrigger.GetSample(control, shift, f8, f10);
    }

    private string CapturePupInteractionReadiness()
    {
        object? singleplayer = FindStaticField("GAT.World", "singleplayer");
        object? controller = FindStaticField("GAT.World", "playerController");
        object? hand = StaticPropertyValue("GAT.PlayerController", "hand");
        object? candidate = PropertyValue(hand, "CurrentAvailableCandidate");
        object? handle = PropertyValue(candidate, "CandidateHandle");
        object? gat = FindStaticField("GAT.World", "gat");
        object? template = ResolvePupTemplate(gat, handle);
        object? position = PropertyValue(candidate, "candidateHeadPos");
        object? camera = Camera.main;
        object? cursor = PropertyValue(FindStaticField("GAT.PlayerController", "showCursor"), "value");
        object? serializerBusy = PropertyValue(FindStaticField("GAT.World", "worldSerializer"), "IsBusy");
        object? modalTool = PropertyValue(controller, "ModalTool");
        object? handLocked = PropertyValue(hand, "IsGrabLocked");
        object? carriedRigid = PropertyValue(hand, "HandCarriedRigid");
        object? possessing = PropertyValue(controller, "IsPossessing");
        object? typing = StaticPropertyValue("GAT.PlayerController", "IsTypingInInputField");
        bool isValid = PropertyValue(candidate, "IsValid") is bool valid && valid;
        string eligibility = EvaluatePupEligibility(template);
        string distance = PupObservation.FormatCandidateDistance(
            isValid,
            hasCamera: camera is Camera,
            hasCandidatePosition: position is Vector3,
            distance: position is Vector3 head && camera is Camera activeCamera
                ? Vector3.Distance(activeCamera.transform.position, head)
                : 0);
        string target = handle == null ? "none" : PupActionObserver.DescribeTarget(handle);
        return $"singleplayerPresent={singleplayer != null};playerControllerPresent={controller != null};possessing={FormatValue(possessing)};cameraPresent={camera != null};cursorShown={FormatValue(cursor)};typing={FormatValue(typing)};handPresent={hand != null};handGrabLocked={FormatValue(handLocked)};carriedRigid={PupActionObserver.DescribeTarget(carriedRigid)};serializerBusy={FormatValue(serializerBusy)};modalToolPresent={modalTool != null};candidateValid={isValid};candidate={target};template={Property(template, "name") ?? "unknown"};isPup={eligibility};distance={distance};candidateHead={FormatValue(position)}";
    }

    private object? ResolvePupTemplate(object? gat, object? handle)
    {
        if (gat == null || handle == null) return null;
        MethodInfo? getTemplate = gat.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetTemplateFromHandle" && method.GetParameters().Length == 1);
        return getTemplate == null ? null : SafeInvoke(getTemplate, gat, new[] { handle });
    }

    private string EvaluatePupEligibility(object? template)
    {
        if (template == null || _pupAssembly == null) return "not-evaluated";
        MethodInfo[] eligibility = SafeGetTypes(_pupAssembly)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.Name == "IsPup").ToArray();
        return EvaluatePup(eligibility, template, template, _pupModInstance);
    }

    private static object? StaticPropertyValue(string typeName, string propertyName)
    {
        Type? type = FindType(typeName);
        return type?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? type?.GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
    }

    private void CompletePupInputSample()
    {
        int sample = _pupInputCount;
        string after = PupActionObserver.CaptureScene(_pupAssembly!);
        int tryWrapDelta = PupActionObserver.TryWrapCalls - _tryWrapCountBefore;
        int beginWrapDelta = PupActionObserver.BeginWrapCalls - _beginWrapCountBefore;
        int beginUnwrapDelta = PupActionObserver.BeginUnwrapCalls - _beginUnwrapCountBefore;
        bool changed = !string.Equals(_pupBeforeSnapshot, after, StringComparison.Ordinal);
        SetFact($"pupAction.{sample}.sceneChanged", changed.ToString());
        SetFact($"pupAction.{sample}.tryWrapCalls", tryWrapDelta.ToString());
        SetFact($"pupAction.{sample}.beginWrapCalls", beginWrapDelta.ToString());
        SetFact($"pupAction.{sample}.beginUnwrapCalls", beginUnwrapDelta.ToString());
        SetFact($"pupAction.{sample}.scene.after", after);
        Logger.LogInfo($"[PupInteraction] sample={sample} tryWrapCalls={tryWrapDelta} beginWrapCalls={beginWrapDelta} beginUnwrapCalls={beginUnwrapDelta} sceneChanged={changed} after={after}");
        _pupCaptureAt = -1;
        _pupBeforeSnapshot = null;
        if (sample < 2) return;
        bool wrapped = ParseInt(_report!.Facts["pupAction.1.tryWrapCalls"]) > 0
            && ParseInt(_report.Facts["pupAction.1.beginWrapCalls"]) > 0
            && bool.Parse(_report.Facts["pupAction.1.sceneChanged"]);
        bool unwrapped = beginUnwrapDelta > 0;
        bool bothChanged = bool.Parse(_report.Facts["pupAction.1.sceneChanged"]) && changed;
        ScenarioVerdict verdict = wrapped && unwrapped && bothChanged
            ? ScenarioVerdict.Pass
            : unwrapped ? ScenarioVerdict.Fail : ScenarioVerdict.BlockedPrecondition;
        string reason = verdict switch
        {
            ScenarioVerdict.Pass => "Normal J inputs invoked TryWrap and the mod's Begin(bundle, true) unwrap path; both changed the observed scene state.",
            ScenarioVerdict.Fail => "The mod's Begin(bundle, true) path ran, but the completed unwrap state did not match the captured scene transition.",
            _ => "J inputs were detected, but the mod's Begin(bundle, true) unwrap path did not run; see the captured carried rigid and candidate state."
        };
        Finish(verdict, reason);
    }

    private static bool IsJPressedThisFrame()
    {
        Type? keyboardType = FindType("UnityEngine.InputSystem.Keyboard") ?? FindType("Unity.InputSystem.Keyboard");
        object? keyboard = StaticMember(keyboardType, "current");
        object? jKey = PropertyValue(keyboard, "jKey");
        if (PropertyValue(jKey, "wasPressedThisFrame") is bool pressed) return pressed;
        return false;
    }

    private static string DescribeJInput()
    {
        Type? keyboardType = FindType("UnityEngine.InputSystem.Keyboard") ?? FindType("Unity.InputSystem.Keyboard");
        object? keyboard = StaticMember(keyboardType, "current");
        object? jKey = PropertyValue(keyboard, "jKey");
        return $"keyboardType={keyboardType?.AssemblyQualifiedName ?? "missing"};current={keyboard != null};jKey={jKey != null};pressed={PropertyValue(jKey, "wasPressedThisFrame") ?? "unavailable"}";
    }

    private static object? StaticMember(Type? type, string name)
    {
        return type?.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? type?.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
    }

    private List<string> FindSceneCandidates(FieldInfo[] pupTypeFields, MethodInfo[] eligibilityMethods, object? modInstance)
    {
        HashSet<string> declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FieldInfo field in pupTypeFields)
        {
            object? values = SafeRead(field, field.IsStatic ? null : modInstance);
            if (values is IEnumerable items)
            {
                foreach (object? item in items) if (item != null) declaredNames.Add(item.ToString() ?? string.Empty);
            }
        }
        var candidates = new List<string>();
        Type? gatType = FindType("GAT.GAT");
        object? gat = FindStaticField("GAT.World", "gat");
        int count = ParseInt(Property(gat, "instanceCount") ?? "0");
        SetFact("agent.liveCount", count.ToString());
        int childTemplateIndex = ParseInt(Property(FindStaticField("GAT.World", "commandOwnershipService"), "WorkerTemplateIndex") ?? "-1");
        SetFact("agent.childTemplateIndex", childTemplateIndex.ToString());
        Dictionary<ushort, string> owners = ReadFamilyOwners();
        MethodInfo? getHandle = gatType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetHandle" && HasSingleParameter(method, typeof(ushort)));
        MethodInfo? getTemplate = gatType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetTemplateFromHandle" && method.GetParameters().Length == 1);
        MethodInfo? getMatrix = gatType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetInstanceMatrixImmediate" && method.GetParameters().Length == 1);
        MethodInfo? getTemplateIndex = gatType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetTemplateIndexFromPhysical" && HasSingleParameter(method, typeof(int)));
        var templateCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int childCount = 0;
        if (gat != null && getHandle != null && getTemplate != null)
        {
            for (int index = 0; index < count; index++)
            {
                object? handle = SafeInvoke(getHandle, gat, new object[] { checked((ushort)index) });
                if (handle == null) continue;
                object? template = SafeInvoke(getTemplate, gat, new[] { handle });
                string name = Property(template, "name") ?? Property(template, "Name") ?? string.Empty;
                int templateIndex = getTemplateIndex != null
                    ? ParseInt(SafeInvoke(getTemplateIndex, gat, new object[] { index })?.ToString() ?? "-1")
                    : TemplateIndexFromRanges(gatType, gat, index);
                string templateKey = templateIndex + ":" + (name.Length == 0 ? "<unknown>" : name);
                templateCounts[templateKey] = templateCounts.TryGetValue(templateKey, out int seen) ? seen + 1 : 1;
                string eligibility = EvaluatePup(eligibilityMethods, template, handle, modInstance);
                bool isChild = templateIndex == childTemplateIndex;
                if (isChild) childCount++;
                bool declaredPup = declaredNames.Contains(name);
                bool nameLooksLikePup = name.Contains("pup", StringComparison.OrdinalIgnoreCase);
                bool eligible = eligibility.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (PupCandidateClassifier.ShouldReport(isChild, declaredPup, nameLooksLikePup, eligible))
                {
                    string stableId = Property(handle, "stableIndex") ?? "unknown";
                    string owner = owners.TryGetValue(ParseUShort(stableId), out string? knownOwner)
                        ? knownOwner
                        : (isChild && owners.Count == 0 ? "singleplayer" : "unmapped");
                    string reason = PupCandidateClassifier.RejectionReason(eligibility);
                    string pose = DescribeAgentPose(getMatrix, gat, handle);
                    candidates.Add($"physical={index};stableId={stableId};templateIndex={templateIndex};template={name};faction={Property(template, "faction") ?? "unknown"};familyOwner={owner};child={isChild};declaredType={declaredPup};isPup={eligibility};rejection={reason};{pose}");
                }
            }
        }
        SetFact("agent.childCount", childCount.ToString());
        SetFact("agent.templateCounts", string.Join("|", templateCounts.Select(pair => $"{pair.Key}={pair.Value}")));
        SetFact("family.agentOwnerMappings", string.Join("|", owners.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
        foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!gameObject.activeInHierarchy) continue;
            string componentTypes = string.Join(",", gameObject.GetComponents<Component>().Where(component => component != null).Select(component => component.GetType().FullName));
            if (declaredNames.Contains(gameObject.name) || gameObject.name.Contains("pup", StringComparison.OrdinalIgnoreCase) || componentTypes.Contains("Pup", StringComparison.OrdinalIgnoreCase))
                candidates.Add($"gameObject={gameObject.name};active={gameObject.activeInHierarchy};components={componentTypes};isPup=not-evaluated");
        }
        return candidates;
    }

    private Dictionary<ushort, string> ReadFamilyOwners()
    {
        var owners = new Dictionary<ushort, string>();
        Type? serviceType = FindType("FamilyTimeMultiplayer.Families.HostFamilyService");
        object? service = serviceType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        object? record = service?.GetType().GetMethod("ToRecord")?.Invoke(service, null);
        object? families = PropertyValue(record, "Families");
        if (families is not IEnumerable entries) return owners;
        foreach (object? family in entries)
        {
            string owner = Property(family, "Owner") ?? "unknown";
            if (PropertyValue(family, "Members") is not IEnumerable members) continue;
            foreach (object? member in members)
            {
                if (member != null && ushort.TryParse(member.ToString(), out ushort stableId)) owners[stableId] = owner;
            }
        }
        return owners;
    }

    private static bool HasSingleParameter(MethodInfo method, Type parameterType) =>
        method.GetParameters() is { Length: 1 } parameters && parameters[0].ParameterType == parameterType;

    private static string DescribeAgentPose(MethodInfo? getMatrix, object gat, object handle)
    {
        if (getMatrix == null || SafeInvoke(getMatrix, gat, new[] { handle }) is not Matrix4x4 matrix) return "position=unavailable";
        Vector3 position = matrix.GetColumn(3);
        object? playerController = FindStaticField("GAT.World", "playerController");
        object? currentPosition = PropertyValue(playerController, "CachedInstancePosition");
        string distance = currentPosition is Vector3 playerPosition
            ? Vector3.Distance(position, playerPosition).ToString("0.00")
            : "unavailable";
        return $"position={position};distanceToPlayer={distance}";
    }

    private static int TemplateIndexFromRanges(Type? gatType, object gat, int physical)
    {
        MethodInfo? entriesMethod = gatType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetTemplateEntries" && method.GetParameters().Length == 0);
        if (entriesMethod == null || SafeInvoke(entriesMethod, gat, Array.Empty<object>()) is not IEnumerable entries) return -1;
        int templateIndex = 0;
        foreach (object? entry in entries)
        {
            int start = ParseInt(Property(entry, "startIndex") ?? "-1");
            int amount = ParseInt(Property(entry, "count") ?? "0");
            if (physical >= start && physical < start + amount) return templateIndex;
            templateIndex++;
        }
        return -1;
    }

    private static ushort ParseUShort(string value) => ushort.TryParse(value, out ushort parsed) ? parsed : ushort.MaxValue;

    private static string EvaluatePup(IEnumerable<MethodInfo> methods, object? template, object handle, object? modInstance)
    {
        foreach (MethodInfo method in methods.Where(candidate => candidate.GetParameters().Length == 1))
        {
            Type parameterType = method.GetParameters()[0].ParameterType;
            object? value = template != null && parameterType.IsInstanceOfType(template) ? template : parameterType.IsInstanceOfType(handle) ? handle : null;
            if (value == null) continue;
            object? target = method.IsStatic ? null : modInstance;
            if (!method.IsStatic && target == null) return "not-evaluated:no-mod-instance";
            try
            {
                object? result = method.Invoke(target, new[] { value });
                return result is bool eligible ? eligible.ToString().ToLowerInvariant() : "result-unknown";
            }
            catch (TargetInvocationException exception)
            {
                return "error:" + (exception.InnerException?.GetType().Name ?? exception.GetType().Name) + ":" + (exception.InnerException?.Message ?? exception.Message);
            }
            catch (Exception exception)
            {
                return "error:" + exception.GetType().Name + ":" + exception.Message;
            }
        }
        return "not-evaluated";
    }

    private void AddEnvironmentFacts()
    {
        string gameRoot = AppDomain.CurrentDomain.BaseDirectory;
        SetFact("environment.gameRoot", gameRoot);
        SetFact("environment.pid", System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
        SetFact("environment.startedAtUtc", DateTime.UtcNow.ToString("O"));
        SetFact("environment.buildFile", ReadFile(Path.Combine(gameRoot, "BUILD.txt")));
        SetFact("environment.gameBuildIdentity", StaticProperty("FamilyTimeMultiplayer.Game.GameBuildIdentity", "Current") ?? "unavailable");
        SetFact("environment.protocolVersion", StaticProperty("FamilyTimeMultiplayer.Core.Protocol.ProtocolConstants", "Version") ?? "unavailable");
        if (_scenario is "pup-interaction" or "child-visibility")
        {
            SetFact("graphics.device", SystemInfo.graphicsDeviceName);
            SetFact("graphics.type", SystemInfo.graphicsDeviceType.ToString());
            SetFact("graphics.version", SystemInfo.graphicsDeviceVersion);
            SetFact("graphics.computeShaders", SystemInfo.supportsComputeShaders.ToString());
            SetFact("graphics.asyncReadback", DescribeAsyncReadbackSupport());
        }
        AddHash("FamilyTimeMultiplayer.dll", Path.Combine(gameRoot, "BepInEx", "plugins", "FamilyTimeMultiplayer", "FamilyTimeMultiplayer.dll"));
        AddHash("FamilyTimeMultiplayer.Core.dll", Path.Combine(gameRoot, "BepInEx", "plugins", "FamilyTimeMultiplayer", "FamilyTimeMultiplayer.Core.dll"));
        string pluginRoot = Path.Combine(gameRoot, "BepInEx", "plugins");
        foreach (string mod in Directory.Exists(pluginRoot) ? Directory.GetFiles(pluginRoot, "*Burritos*.dll", SearchOption.AllDirectories) : Array.Empty<string>()) AddHash("pupMod", mod);
    }

    private void AddHash(string key, string path)
    {
        SetFact($"hash.{key}", File.Exists(path) ? Hash(path) : "missing");
    }

    private static string DescribeAsyncReadbackSupport()
    {
        Type? readbackType = FindType("UnityEngine.Rendering.AsyncGPUReadback");
        MethodInfo? isSupported = readbackType?.GetMethod("IsSupported", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        return isSupported?.Invoke(null, null)?.ToString() ?? "unavailable";
    }

    private void FinishOnTimeout(string condition, string reason)
    {
        if (!string.Equals(_timeoutCondition, condition, StringComparison.Ordinal))
        {
            _timeoutCondition = condition;
            _conditionWait = new BoundedWait(_scenario == "native-input" ? 300 : RuntimeHarnessReadinessTimeouts.ForCondition(condition));
        }
        if (!_conditionWait!.IsTimedOutAt(Time.realtimeSinceStartup, condition)) return;
        SetFact("timeout.condition", condition);
        SetFact("timeout.seconds", _conditionWait.TimeoutSeconds.ToString("0"));
        Finish(ScenarioVerdict.BlockedPrecondition, reason);
    }

    private void Finish(ScenarioVerdict verdict, string reason)
    {
        if (_complete || _report == null) return;
        _report.AddFact("elapsedSeconds", (Time.realtimeSinceStartup - _startedAt).ToString("0.0"));
        _report.Finish(verdict, reason);
        string path = ReportPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, _report.ToJson(), Encoding.UTF8);
        Logger.LogInfo($"[RuntimeHarnessReport] {_report.ToJson()}");
        Logger.LogInfo($"[RuntimeHarnessReport] evidenceFile={path}");
        _complete = true;
        if (_scenario == "family-persistence")
        {
            SignalFamilyPeerCompletion($"{verdict}: {reason}");
            LeaveSessionAndQuit();
        }
    }

    private string ReportPath()
    {
        return Path.Combine(ReportDirectory(), $"{_scenario}-{_runId}.json");
    }

    private void SetFact(string key, string value)
    {
        _report?.AddFact(key, value);
    }

    private static Type? FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(fullName, false)).FirstOrDefault(type => type != null);
    private static string? StaticProperty(string typeName, string propertyName)
    {
        Type? type = FindType(typeName);
        object? value = type?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? type?.GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        return value?.ToString();
    }
    private static object? FindStaticField(string typeName, string fieldName)
    {
        Type? type = FindType(typeName);
        FieldInfo? field = type?.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return field?.GetValue(null) ?? type?.GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
    }
    private static object? PropertyValue(object? target, string propertyName)
    {
        if (target == null) return null;
        Type type = target.GetType();
        PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property?.GetValue(target) ?? type.GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);
    }
    private static string? Property(object? target, string propertyName) => PropertyValue(target, propertyName)?.ToString();
    private static string Signature(MethodInfo method) => $"{(method.IsStatic ? "static" : "instance")} {method.DeclaringType?.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName))})";
    private static Type[] SafeGetTypes(Assembly assembly) { try { return assembly.GetTypes(); } catch (ReflectionTypeLoadException exception) { return exception.Types.Where(type => type != null).Cast<Type>().ToArray(); } }
    private static object? SafeRead(FieldInfo field, object? target) { try { return field.GetValue(target); } catch { return null; } }
    private static object? SafeInvoke(MethodInfo method, object? target, object?[] arguments) { try { return method.Invoke(target, arguments); } catch { return null; } }
    private static int CountEnumerable(object? value) => value is IEnumerable enumerable ? enumerable.Cast<object?>().Count() : 0;
    private static int ParseInt(string value) => int.TryParse(value, out int result) ? result : 0;
    private static string FormatValue(object? value) => value is IEnumerable enumerable && value is not string ? string.Join(",", enumerable.Cast<object?>().Select(item => item?.ToString())) : value?.ToString() ?? "null";
    private static string ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : "missing";
    private static string Hash(string path) { using SHA256 algorithm = SHA256.Create(); using FileStream stream = File.OpenRead(path); return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant(); }
}
