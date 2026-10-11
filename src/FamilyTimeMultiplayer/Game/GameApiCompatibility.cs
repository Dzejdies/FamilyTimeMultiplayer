using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game;

internal static class GameApiCompatibility
{
    public static MethodInfo HudRefreshMethod => ResolveMethod(typeof(GATStateDisplayUI), "RefreshProgressDisplay", "RefreshDisplay");
    public static MethodInfo HudHungerUpdateMethod =>
        typeof(GATStateDisplayUI).GetMethod("OnHungryCountChanged", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(GATStateDisplayUI).GetMethod("RequestHungryCount", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(GATStateDisplayUI), "OnHungryCountChanged/RequestHungryCount");

    private static readonly MethodInfo MamaToggleMethod = ResolveMethod(typeof(WolfMama), "OnToolAlt", "OnHandSecondary");
    private static readonly Action<WolfMama> ToggleMamaAction = (Action<WolfMama>)MamaToggleMethod.CreateDelegate(typeof(Action<WolfMama>));
    private static readonly MethodInfo? LegacyConstructionResume = typeof(ConstructionScheduler).GetMethod("ResumeAfterDeletedBuildPlan", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static MethodBase MamaToggleTargetMethod => MamaToggleMethod;
    public static MethodBase FollowCursorTargetMethod => ResolveMethod(typeof(FollowController), "ResolveFollowCursorTarget", "UpdateFollowCursorTarget");
    public static MethodBase CommandLongPressSecondaryTargetMethod => ResolveBooleanMethod(typeof(CommandService), "OnLongPressToolAlt", "OnLongPressSecondary");

    private static readonly Type? NativeGameSessionType = typeof(PauseMenuBook).Assembly.GetType("family.GameSession");

    public static bool HasNativeTitleFlow => NativeGameSessionType != null && typeof(PauseMenuBook).Assembly.GetType("family.TitleMenu") != null;

    public static bool NativeSessionIsStartingOrLoading
    {
        get => NativeSessionStateName == "Starting" || NativeSessionStateName == "Loading";
    }

    public static string NativeSessionStateName
    {
        get
        {
            return GetNativeSessionState();
        }
    }

    private static string GetNativeSessionState()
    {
        if (NativeGameSessionType == null) return string.Empty;
        object? instance = AccessTools.Property(NativeGameSessionType, "Instance")?.GetValue(null);
        object? state = instance == null ? null : AccessTools.Property(NativeGameSessionType, "State")?.GetValue(instance);
        return state?.ToString() ?? string.Empty;
    }

    public static void OpenNativeNewGame(PauseMenuBook book) => InvokeNativeMenu(book, "NewGame");

    public static void OpenNativeSaveLoad(PauseMenuBook book) => InvokeNativeMenu(book, "SaveLoad");

    public static bool IsNativeMainMenuActive(PauseMenuBook book)
    {
        GameObject? main = Traverse.Create(book).Field<GameObject>("mainMenu").Value;
        GameObject? current = Traverse.Create(book).Field<GameObject>("_currentPage").Value;
        return main != null && current == main;
    }

    public static void BeginNativeTitle(object gameSession)
    {
        MethodInfo method = AccessTools.Method(gameSession.GetType(), "BeginFromTitle")
            ?? throw new MissingMethodException(gameSession.GetType().FullName, "BeginFromTitle");
        method.Invoke(gameSession, null);
    }

    private static void InvokeNativeMenu(PauseMenuBook book, string methodName)
    {
        MethodInfo method = AccessTools.Method(typeof(PauseMenuBook), methodName)
            ?? throw new MissingMethodException(nameof(PauseMenuBook), methodName);
        method.Invoke(book, null);
    }

    public static void ToggleMama(WolfMama mama) => ToggleMamaAction(mama);

    public static float CommandRaycastDistance(CommandService commands, float fallback)
    {
        PropertyInfo? distance = AccessTools.Property(typeof(CommandService), "CommandRaycastDistance");
        if (distance != null) return (float)distance.GetValue(commands)!;
        Template? template = commands.CommandTemplate;
        return template == null ? fallback : Traverse.Create(template).Field<float>("maxCommandDistance").Value;
    }

    public static StationCommandController CreateStationController(GATSmartCombatBehavior combat, CommandOwnershipService ownership, float groupingRadius)
    {
        ConstructorInfo? constructor = typeof(StationCommandController).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.GetParameters().Length is 3 or 5);
        if (constructor == null) throw new MissingMethodException(nameof(StationCommandController), ".ctor");
        object[] arguments = constructor.GetParameters().Length == 5
            ? new object[] { combat, ownership, Commands.CommandAudioCatalog.Get(Core.Protocol.CommandSound.Station), Commands.CommandAudioCatalog.Get(Core.Protocol.CommandSound.Stop), groupingRadius }
            : new object[] { combat, ownership, groupingRadius };
        return (StationCommandController)constructor.Invoke(arguments);
    }

    public static bool RunInBackground
    {
        get => (bool)(AccessTools.Property(typeof(Application), "runInBackground")?.GetValue(null) ?? false);
        set => AccessTools.PropertySetter(typeof(Application), "runInBackground")?.Invoke(null, new object[] { value });
    }

    public static void SetTargetFrameRate(int framesPerSecond) =>
        AccessTools.PropertySetter(typeof(Application), "targetFrameRate")?.Invoke(null, new object[] { framesPerSecond });

    public static float TimeScale => (float)(AccessTools.Property(typeof(Time), "timeScale")?.GetValue(null) ?? 1f);

    public static bool CaptureScreenshot(string path)
    {
        Type? captureType = AccessTools.TypeByName("UnityEngine.ScreenCapture");
        MethodInfo? capture = captureType == null ? null : AccessTools.Method(captureType, "CaptureScreenshot", new[] { typeof(string) });
        if (capture == null) return false;
        capture.Invoke(null, new object[] { path });
        return true;
    }

    public static ConstructionScheduler CreateConstructionScheduler(PickupWorkerScheduler pickup, Camera camera, AudioGroup audio, float reach, float groupingRadius)
    {
        ConstructorInfo? constructor = typeof(ConstructionScheduler).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.GetParameters().Length is 4 or 5);
        if (constructor == null) throw new MissingMethodException(nameof(ConstructionScheduler), ".ctor");
        object[] arguments = constructor.GetParameters().Length == 5
            ? new object[] { pickup, camera, audio, reach, groupingRadius }
            : new object[] { pickup, camera, audio, groupingRadius };
        return (ConstructionScheduler)constructor.Invoke(arguments);
    }

    public static int GetSiblingIndex(Transform transform) => (int)(AccessTools.Method(typeof(Transform), "GetSiblingIndex")?.Invoke(transform, null) ?? 0);

    public static void SetSiblingIndex(Transform transform, int index) => AccessTools.Method(typeof(Transform), "SetSiblingIndex")?.Invoke(transform, new object[] { index });

    public static bool TryRegisterAssignment(GATStationManager manager, object station, Handle worker, Faction faction)
    {
        MethodInfo method = AccessTools.Method(typeof(GATStationManager), "TryRegisterAssignment") ?? throw new MissingMethodException(nameof(GATStationManager), "TryRegisterAssignment");
        ParameterInfo[] parameters = method.GetParameters();
        object?[] arguments = parameters.Length == 3 ? new object?[] { station, worker, faction } : new object?[] { station, worker };
        return (bool)method.Invoke(manager, arguments)!;
    }

    public static bool StartConstruction(ConstructionScheduler scheduler, BuildPlanManager.BuildPlan? plan)
    {
        MethodInfo? plannedStart = AccessTools.Method(typeof(ConstructionScheduler), "TryStartConstructionCommand", new[] { typeof(BuildPlanManager.BuildPlan) });
        if (plannedStart != null) return (bool)plannedStart.Invoke(scheduler, new object?[] { plan })!;
        MethodInfo legacyStart = AccessTools.Method(typeof(ConstructionScheduler), "TryStartConstructionCommand")
            ?? throw new MissingMethodException(nameof(ConstructionScheduler), "TryStartConstructionCommand");
        return (bool)legacyStart.Invoke(scheduler, null)!;
    }

    public static bool TryConstructBuildPlan(BuildPlanManager manager, BuildPlanManager.BuildPlan plan, IReadOnlyList<RigidTransform> materials, Vector3 soundPosition)
    {
        MethodInfo method = AccessTools.Method(typeof(BuildPlanManager), "TryConstructBuildPlan")
            ?? throw new MissingMethodException(nameof(BuildPlanManager), "TryConstructBuildPlan");
        ParameterInfo[] parameters = method.GetParameters();
        object thirdArgument = parameters[2].ParameterType == typeof(Vector3) ? soundPosition : Array.Empty<Handle>();
        return (bool)method.Invoke(manager, new object[] { plan, materials, thirdArgument })!;
    }

    public static void ResumeConstruction(ConstructionScheduler scheduler)
    {
        LegacyConstructionResume?.Invoke(scheduler, null);
    }

    public static void StopAvailableFollowers(FollowController follow)
    {
        MethodInfo method = ResolveMethod(typeof(FollowController), "StopAvailableFollowSessions", "RemoveAvailableFollowers");
        method.Invoke(follow, null);
    }

    public static void StartCarryingWorkersFollow(PickupWorkerScheduler pickup)
    {
        MethodInfo? method = AccessTools.Method(typeof(PickupWorkerScheduler), "StartFollowSessionsForCarryingWorkers");
        if (method != null)
        {
            method.Invoke(pickup, null);
            return;
        }
        method = AccessTools.Method(typeof(PickupWorkerScheduler), "AddCarryingWorkersToFollowFlow")
            ?? throw new MissingMethodException(nameof(PickupWorkerScheduler), "carrying-worker follow method");
        method.Invoke(pickup, null);
    }

    public static bool IsFollowingPickupGroup(PickupWorkerScheduler pickup, CommandAgentGroup? group)
    {
        if (group == null) return false;
        MethodInfo? direct = AccessTools.Method(typeof(PickupWorkerScheduler), "IsFollowingPickupGroup");
        if (direct != null) return (bool)direct.Invoke(pickup, new object?[] { group })!;
        MethodInfo? isFollowing = AccessTools.Method(typeof(PickupWorkerScheduler), "IsFollowingPickupHandle", new[] { typeof(Handle) });
        if (isFollowing == null) throw new MissingMethodException(nameof(PickupWorkerScheduler), "IsFollowingPickupHandle");
        foreach (Handle handle in group.GetAgentSnapshot())
        {
            if ((bool)isFollowing.Invoke(pickup, new object[] { handle })!) return true;
        }
        return false;
    }

    public static BuildPlanManager.BuildPlan? FindCommandPlan(Transform eye, float reach)
    {
        BuildPlanManager? manager = World.buildPlanManager;
        if (manager == null) return null;
        return manager.GetBuildPlan(eye.position, eye.forward, reach, out BuildPlanManager.BuildPlan plan) ? plan : null;
    }

    public static bool StartPrimaryPickup(PickupWorkerScheduler pickup, PropChunkManager.PropCollisionInstance prop, object prefab)
    {
        MethodInfo method = typeof(PickupWorkerScheduler).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.Name == "TryTogglePrimaryPickup" &&
                candidate.GetParameters().Length == 2 && candidate.GetParameters()[0].ParameterType.IsInstanceOfType(prop) &&
                candidate.GetParameters()[1].ParameterType.IsInstanceOfType(prefab))
            ?? throw new MissingMethodException(nameof(PickupWorkerScheduler), "TryTogglePrimaryPickup(prop, prefab)");
        return (bool)method.Invoke(pickup, new[] { (object)prop, prefab })!;
    }

    public static GATPickupBehavior.CommandShape PickupCommandShape(object prefab)
    {
        if (prefab is RigidTransform rigid) return rigid.CommandShape;
        object? value = AccessTools.Property(prefab.GetType(), "commandShape")?.GetValue(prefab)
            ?? AccessTools.Field(prefab.GetType(), "commandShape")?.GetValue(prefab);
        return value is GATPickupBehavior.CommandShape shape
            ? shape
            : throw new MissingMemberException(prefab.GetType().FullName, "commandShape");
    }

    public static CommandVisuals.PrimaryOutlineEntry CreatePrimaryOutlineEntry(RigidTransform rigid, CommandVisuals.PrimaryOutlineKind kind)
    {
        ConstructorInfo constructor = typeof(CommandVisuals.PrimaryOutlineEntry).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.GetParameters().Length == 2)
            ?? throw new MissingMethodException(nameof(CommandVisuals.PrimaryOutlineEntry), ".ctor");
        Type targetType = constructor.GetParameters()[0].ParameterType;
        object target = targetType.IsInstanceOfType(rigid)
            ? rigid
            : (object?)RigidTransformApi.GetComponent<Transform>(rigid) ?? throw new InvalidOperationException("Rigid transform has no Unity Transform for its outline.");
        return (CommandVisuals.PrimaryOutlineEntry)constructor.Invoke(new[] { target, (object)kind });
    }

    public static RigidTransform? PrimaryOutlineRigid(CommandVisuals.PrimaryOutlineEntry entry)
    {
        object boxed = entry;
        object? target = AccessTools.Field(boxed.GetType(), "rigid")?.GetValue(boxed)
            ?? AccessTools.Field(boxed.GetType(), "target")?.GetValue(boxed);
        if (target is RigidTransform rigid) return rigid;
        return target is Transform transform ? RigidTransformApi.FromGameObject(transform.gameObject) : null;
    }

    public static bool HasOutline(RigidTransform? rigid, Color? color = null)
    {
        if (!RigidTransformApi.IsAlive(rigid) || OutlineManager.Instance == null) return false;
        object entries = AccessTools.Property(typeof(OutlineManager), "Entries")?.GetValue(OutlineManager.Instance)
            ?? throw new MissingMemberException(nameof(OutlineManager), "Entries");
        foreach (object? entry in (IEnumerable)entries)
        {
            if (!OutlineEntryMatches(entry, rigid!, color)) continue;
            return true;
        }
        return false;
    }

    public static int BuildPartMaterialCount(BuildPart part, ConstructionMaterial material)
    {
        FieldInfo? requirementsField = AccessTools.Field(typeof(BuildPart), "constructionRequirements");
        if (requirementsField?.GetValue(part) is not IEnumerable requirements)
        {
            object? currentMaterial = AccessTools.Field(typeof(BuildPart), "constructionMaterial")?.GetValue(part);
            return currentMaterial is ConstructionMaterial value && value == material ? 1 : 0;
        }
        int count = 0;
        foreach (object requirement in requirements)
        {
            object? requirementMaterial = AccessTools.Field(requirement.GetType(), "req")?.GetValue(requirement);
            object? amount = AccessTools.Field(requirement.GetType(), "amount")?.GetValue(requirement);
            if (requirementMaterial is ConstructionMaterial value && value == material && amount is int units) count += units;
        }
        return count;
    }

    private static bool OutlineEntryMatches(object entry, RigidTransform rigid, Color? color)
    {
        object? entryColor = AccessTools.Field(entry.GetType(), "color")?.GetValue(entry);
        if (color.HasValue && entryColor is Color foundColor && foundColor != color.Value) return false;
        object? entryRigid = AccessTools.Field(entry.GetType(), "rigid")?.GetValue(entry);
        if (entryRigid is RigidTransform foundRigid) return ReferenceEquals(foundRigid, rigid);
        object? renderer = AccessTools.Field(entry.GetType(), "renderer")?.GetValue(entry);
        if (renderer is not Renderer foundRenderer) return false;
        object value = rigid;
        return value is Component component && foundRenderer.transform.IsChildOf(component.transform);
    }

    public static void CancelFollowGesture(FollowController follow)
    {
        MethodInfo? cancelMethod = AccessTools.Method(typeof(FollowController), "CancelFollowInput");
        if (cancelMethod != null) cancelMethod.Invoke(follow, null);
        foreach (string fieldName in new[] { "_followPressed", "_followLongPressTriggered", "_followCursorTargetActive", "_hasFollowCursorTarget" })
        {
            FieldInfo field = AccessTools.Field(typeof(FollowController), fieldName) ?? throw new MissingFieldException(nameof(FollowController), fieldName);
            field.SetValue(follow, false);
        }
    }

    public static void StopLocalSelectionGesture(CommandService service) =>
        ResolveMethod(typeof(CommandService), "StopContinuousSelection").Invoke(service, null);

    public static void ActivateFollowCursor(FollowController follow)
    {
        AccessTools.Field(typeof(FollowController), "_followCursorTargetActive")?.SetValue(follow, true);
    }

    public static int FollowAgentCount(FollowController follow)
    {
        PropertyInfo? property = AccessTools.Property(typeof(FollowController), "FollowAgentCount");
        if (property != null)
        {
            return (int)property.GetValue(follow)!;
        }

        return Traverse.Create(follow).Field<int>("_followAgentCount").Value;
    }

    private static MethodInfo ResolveMethod(Type type, params string[] names)
    {
        foreach (string name in names)
        {
            MethodInfo? method = AccessTools.Method(type, name, Type.EmptyTypes);
            if (method != null)
            {
                return method;
            }
        }

        throw new MissingMethodException(type.FullName, string.Join(" or ", names));
    }

    private static MethodInfo ResolveBooleanMethod(Type type, params string[] names)
    {
        foreach (string name in names)
        {
            MethodInfo? method = AccessTools.Method(type, name, new[] { typeof(bool) });
            if (method != null) return method;
        }
        throw new MissingMethodException(type.FullName, string.Join(" or ", names));
    }
}
