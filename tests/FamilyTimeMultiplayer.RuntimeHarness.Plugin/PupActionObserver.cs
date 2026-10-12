using System;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.RuntimeHarness;

internal static class PupActionObserver
{
    private static ManualLogSource? _log;

    public static int TryWrapCalls { get; private set; }
    public static int BeginWrapCalls { get; private set; }
    public static int BeginUnwrapCalls { get; private set; }

    public static void Install(Assembly modAssembly, ManualLogSource log)
    {
        _log = log;
        var harmony = new Harmony("dzejdies.familytime.runtime-harness.pup-observer");
        MethodInfo prefix = typeof(PupActionObserver).GetMethod(nameof(ObserveCall), BindingFlags.NonPublic | BindingFlags.Static)!;
        int patched = 0;
        foreach (MethodInfo method in modAssembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)))
        {
            bool isAction = method.Name.Equals("TryWrap", StringComparison.OrdinalIgnoreCase)
                || method.Name.Equals("unwrap", StringComparison.OrdinalIgnoreCase)
                || (method.Name.Equals("Begin", StringComparison.OrdinalIgnoreCase)
                    && method.DeclaringType?.FullName == "FamilyTimeBurritos.Plugin")
                || (method.Name.Contains("TryWrap>b__0", StringComparison.Ordinal)
                    && method.GetParameters().Any(parameter => parameter.ParameterType.FullName == "UnityEngine.Rendering.AsyncGPUReadbackRequest"));
            if (!isAction) continue;
            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            patched++;
        }
        log.LogInfo($"[PupObserver] installed on {patched} TryWrap/Begin/unwrap methods");
    }

    public static string CaptureScene(Assembly modAssembly)
    {
        var snapshot = new StringBuilder();
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject gameObject in objects.Where(item => item != null && item.activeInHierarchy))
        {
            Component[] components = gameObject.GetComponentsInChildren<Component>(includeInactive: true).Where(component => component != null).ToArray();
            string[] relevant = components.Select(component => component.GetType())
                .Where(type => type.Assembly == modAssembly || type.Name.Contains("Pup", StringComparison.OrdinalIgnoreCase) || type.Name.Contains("Wrap", StringComparison.OrdinalIgnoreCase) || type.Name.Contains("Burrito", StringComparison.OrdinalIgnoreCase))
                .Select(type => type.FullName ?? type.Name).Distinct().ToArray();
            if (relevant.Length == 0 && !gameObject.name.Contains("pup", StringComparison.OrdinalIgnoreCase) && !gameObject.name.Contains("wrap", StringComparison.OrdinalIgnoreCase)) continue;
            snapshot.Append(gameObject.name).Append('#').Append(gameObject.GetInstanceID())
                .Append(" active=").Append(gameObject.activeInHierarchy)
                .Append(" pos=").Append(gameObject.transform.position)
                .Append(" scale=").Append(gameObject.transform.lossyScale)
                .Append(" children=").Append(DescribeChildren(gameObject.transform))
                .Append(" components=").Append(string.Join(",", relevant));
            AppendVisualState(snapshot, gameObject);
            Component[] bodies = gameObject.GetComponentsInChildren<Component>(includeInactive: true)
                .Where(component => component != null && component.GetType().FullName == "UnityEngine.Rigidbody").ToArray();
            foreach (Component body in bodies)
            {
                snapshot.Append(" rigid[").Append(body.gameObject.name)
                    .Append(";kinematic=").Append(Property(body, "isKinematic"))
                    .Append(";gravity=").Append(Property(body, "useGravity"))
                    .Append(";mass=").Append(Property(body, "mass"))
                    .Append(";velocity=").Append(Property(body, "velocity")).Append(']');
            }
            snapshot.Append('|');
        }
        return snapshot.Length == 0 ? "no-mod-or-pup-scene-objects" : snapshot.ToString();
    }

    public static string DescribeTarget(object? target)
    {
        if (target == null) return "null";
        if (target is GameObject gameObject) return $"GameObject:{gameObject.name}#{gameObject.GetInstanceID()}@{gameObject.transform.position}";
        if (target is Component component) return $"{component.GetType().Name}:{component.gameObject.name}#{component.GetInstanceID()}@{component.transform.position}";
        PropertyInfo? stableIndex = target.GetType().GetProperty("stableIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return stableIndex == null ? $"{target.GetType().FullName}:{target}" : $"{target.GetType().FullName}:stableId={stableIndex.GetValue(target)}";
    }

    private static void ObserveCall(MethodBase __originalMethod, object? __instance, object?[] __args)
    {
        string methodName = __originalMethod.Name;
        if (methodName.Equals("TryWrap", StringComparison.OrdinalIgnoreCase)) TryWrapCalls++;
        if (methodName.Equals("Begin", StringComparison.OrdinalIgnoreCase) && __args.LastOrDefault() is bool unwrap)
        {
            if (unwrap) BeginUnwrapCalls++;
            else BeginWrapCalls++;
        }
        _log?.LogInfo($"[PupObserver] call={__originalMethod.DeclaringType?.FullName}.{methodName} target={DescribeTarget(__args.FirstOrDefault())} args={string.Join(";", __args.Select(DescribeArgument))}");
    }

    private static string DescribeChildren(Transform parent)
    {
        return string.Join(",", Enumerable.Range(0, parent.childCount)
            .Take(12)
            .Select(index => parent.GetChild(index).name));
    }

    private static string DescribeArgument(object? argument)
    {
        if (argument == null) return "null";
        if (argument.GetType().FullName?.Contains("AsyncGPUReadbackRequest", StringComparison.Ordinal) == true)
            return $"{argument.GetType().Name}:done={ReadProperty(argument, "done")};hasError={ReadProperty(argument, "hasError")}";
        return DescribeTarget(argument);
    }

    private static object? ReadProperty(object instance, string name)
    {
        try
        {
            return instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
        }
        catch (Exception exception)
        {
            return $"unavailable:{exception.GetType().Name}";
        }
    }

    private static void AppendVisualState(StringBuilder snapshot, GameObject gameObject)
    {
        Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(includeInactive: true).Take(8).ToArray();
        foreach (Renderer renderer in renderers)
        {
            snapshot.Append(" visual[").Append(renderer.gameObject.name)
                .Append(";type=").Append(renderer.GetType().Name)
                .Append(";enabled=").Append(renderer.enabled)
                .Append(";visible=").Append(ReadProperty(renderer, "isVisible"))
                .Append(";forceOff=").Append(renderer.forceRenderingOff)
                .Append(";materials=").Append(renderer.sharedMaterials.Length)
                .Append(']');
        }
    }

    private static string Property(object target, string name)
    {
        Type type = target.GetType();
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return (property?.GetValue(target) ?? type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target))?.ToString() ?? "unknown";
    }
}
