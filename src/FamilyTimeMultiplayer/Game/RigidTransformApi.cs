using System;
using System.Reflection;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game;

public sealed class RigidPrefab
{
    internal RigidPrefab(object runtimePrefab) => RuntimePrefab = runtimePrefab;

    internal object RuntimePrefab { get; }
}

public static class RigidTransformApi
{
    private static readonly Type RigidType = typeof(RigidTransform);
    private static readonly Type ManagerType = typeof(RigidTransformManager);

    public static int Count(RigidTransformManager manager) =>
        (int)AccessTools.Property(ManagerType, "Count")!.GetValue(manager)!;

    public static bool TryGetManagedRigid(RigidTransformManager manager, int index, out RigidTransform rigid, out int prefabIndex)
    {
        object?[] arguments = { index, null, -1 };
        MethodInfo method = RequireManagedRigidMethod();
        bool found = (bool)method.Invoke(manager, arguments)!;
        rigid = found ? (RigidTransform)arguments[1]! : null!;
        prefabIndex = found && arguments.Length > 2 ? (int)arguments[2]! : -1;
        return found;
    }

    public static bool TryGetManagedRigid(RigidTransformManager manager, int index, out RigidTransform rigid) =>
        TryGetManagedRigid(manager, index, out rigid, out _);

    public static bool TryGetPrefab(RigidTransformManager manager, int index, out RigidPrefab prefab)
    {
        MethodInfo method = AccessTools.Method(ManagerType, "TryGetPrefab")
            ?? throw new MissingMethodException(ManagerType.FullName, "TryGetPrefab");
        object?[] arguments = { index, null };
        bool found = (bool)method.Invoke(manager, arguments)!;
        prefab = found ? new RigidPrefab(arguments[1]!) : null!;
        return found;
    }

    public static RigidTransform Spawn(RigidTransformManager manager, RigidPrefab prefab, Vector3 position, Quaternion rotation)
    {
        MethodInfo method = AccessTools.Method(ManagerType, "Spawn")
            ?? throw new MissingMethodException(ManagerType.FullName, "Spawn");
        Type expectedPrefabType = method.GetParameters()[0].ParameterType;
        object runtimePrefab = ResolvePrefabValue(prefab, expectedPrefabType);
        return (RigidTransform)method.Invoke(manager, new[] { runtimePrefab, position, rotation })!;
    }

    public static RigidTransform? FromGameObject(GameObject gameObject)
    {
        RigidTransform? direct = (object?)gameObject.GetComponent(RigidType) as RigidTransform;
        if (direct != null) return direct;
        foreach (Component component in gameObject.GetComponents<Component>())
        {
            if (component == null || component.GetType().Name != "RigidTransformBridge") continue;
            object? rigid = AccessTools.Property(component.GetType(), "Rigid")?.GetValue(component);
            if (rigid is RigidTransform value) return value;
        }
        return null;
    }

    public static bool IsAlive(RigidTransform? rigid)
    {
        if (rigid == null) return false;
        object value = rigid;
        if (value is UnityEngine.Object unityObject) return unityObject != null;
        return AccessTools.Property(RigidType, "IsAlive")?.GetValue(rigid) is not bool alive || alive;
    }

    public static RigidTransform? FromMama(WolfMama mama)
    {
        object? rigid = AccessTools.Property(typeof(WolfMama), "_rigidTransform")?.GetValue(mama);
        return rigid as RigidTransform ?? FromGameObject(mama.gameObject);
    }

    public static T? GetComponent<T>(RigidTransform rigid) where T : UnityEngine.Component
    {
        object value = rigid;
        return value is UnityEngine.Component component ? component.GetComponent<T>() : null;
    }

    public static object? Definition(RigidTransform rigid)
    {
        object value = rigid;
        return AccessTools.Property(RigidType, "Definition")?.GetValue(value);
    }

    public static object? DefinitionValue(RigidTransform rigid, string memberName)
    {
        object? definition = Definition(rigid);
        return AccessTools.Field(RigidType, memberName)?.GetValue(rigid)
            ?? (definition == null ? null : AccessTools.Field(definition.GetType(), memberName)?.GetValue(definition))
            ?? (definition == null ? null : AccessTools.Property(definition.GetType(), memberName)?.GetValue(definition));
    }

    public static string Name(RigidTransform? rigid)
    {
        if (rigid == null) return RigidType.Name;
        object value = rigid;
        if (value is UnityEngine.Object unityObject) return unityObject.name;
        return AccessTools.Property(RigidType, "Definition")?.GetValue(rigid) is UnityEngine.Object definition
            ? definition.name
            : RigidType.Name;
    }

    public static string PrefabName(RigidPrefab prefab) =>
        prefab.RuntimePrefab is UnityEngine.Object unityObject ? unityObject.name : prefab.RuntimePrefab.ToString() ?? "Rigid prefab";

    public static GATPickupBehavior.CommandShape PrefabCommandShape(RigidPrefab prefab)
    {
        object? value = AccessTools.Property(prefab.RuntimePrefab.GetType(), "CommandShape")?.GetValue(prefab.RuntimePrefab)
            ?? AccessTools.Field(prefab.RuntimePrefab.GetType(), "commandShape")?.GetValue(prefab.RuntimePrefab)
            ?? AccessTools.Field(prefab.RuntimePrefab.GetType(), "rigidTransformPrefab")?.GetValue(prefab.RuntimePrefab);
        if (value is GATPickupBehavior.CommandShape shape) return shape;
        if (value is UnityEngine.Object unityObject)
        {
            object? component = unityObject is GameObject gameObject ? FromGameObject(gameObject) : null;
            if (component is RigidTransform rigid) return rigid.CommandShape;
        }
        throw new MissingMemberException(prefab.RuntimePrefab.GetType().FullName, "commandShape");
    }

    public static ConstructionMaterial PrefabSelfMaterial(RigidPrefab prefab)
    {
        object? value = AccessTools.Property(prefab.RuntimePrefab.GetType(), "SelfMaterial")?.GetValue(prefab.RuntimePrefab)
            ?? AccessTools.Field(prefab.RuntimePrefab.GetType(), "selfMaterial")?.GetValue(prefab.RuntimePrefab);
        if (value is ConstructionMaterial material) return material;
        if (prefab.RuntimePrefab is GameObject gameObject && FromGameObject(gameObject) is RigidTransform rigid) return rigid.SelfMaterial;
        throw new MissingMemberException(prefab.RuntimePrefab.GetType().FullName, "selfMaterial");
    }

    public static Vector3 Scale(RigidTransform rigid)
    {
        object value = rigid;
        if (value is UnityEngine.Component component) return component.transform.lossyScale;
        return AccessTools.Property(RigidType, "Scale")?.GetValue(rigid) is Vector3 scale ? scale : Vector3.one;
    }

    public static Vector3 DisplayCenter(RigidTransform rigid)
    {
        object value = rigid;
        if (value is UnityEngine.Component component)
        {
            Renderer? renderer = component.GetComponentInChildren<Renderer>();
            if (renderer != null && AccessTools.Property(typeof(Renderer), "bounds")?.GetValue(renderer) is Bounds rendererBounds) return rendererBounds.center;
        }
        object? definition = Definition(rigid);
        Bounds bounds = definition != null && AccessTools.Field(definition.GetType(), "bounds")?.GetValue(definition) is Bounds valueBounds
            ? valueBounds
            : new Bounds(Vector3.zero, Vector3.zero);
        return rigid.position + bounds.center;
    }

    private static MethodInfo RequireManagedRigidMethod()
    {
        Type rigidOutput = RigidType.MakeByRefType();
        Type indexOutput = typeof(int).MakeByRefType();
        return AccessTools.Method(ManagerType, "TryGetManagedRigid", new[] { typeof(int), rigidOutput, indexOutput })
            ?? AccessTools.Method(ManagerType, "TryGetManagedRigid", new[] { typeof(int), rigidOutput })
            ?? throw new MissingMethodException(ManagerType.FullName, "TryGetManagedRigid");
    }

    private static object ResolvePrefabValue(RigidPrefab prefab, Type expectedType)
    {
        if (expectedType.IsInstanceOfType(prefab.RuntimePrefab)) return prefab.RuntimePrefab;
        if (prefab.RuntimePrefab is GameObject gameObject && expectedType == typeof(GameObject)) return gameObject;
        throw new InvalidOperationException($"Prefab '{prefab.RuntimePrefab}' is incompatible with {expectedType.FullName}.");
    }
}
