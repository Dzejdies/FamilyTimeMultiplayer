using System;
using System.Reflection;
using FamilyTimeMultiplayer.Families;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch]
internal static class FamilyIconPatch
{
    private static readonly Type PassType = AccessTools.TypeByName("GAT.GATScreenIconsPass") ?? throw new TypeLoadException("GAT.GATScreenIconsPass");
    private static readonly MethodInfo Execute = AccessTools.Method(PassType, "ExecutePass") ?? throw new MissingMethodException(PassType.Name, "ExecutePass");
    private static readonly Type DataType = Execute.GetParameters()[0].ParameterType;
    private static readonly FieldInfo Count = RequireField("commandInstanceCount", "instanceCount");
    private static readonly FieldInfo Properties = RequireField("commandProperties", "materialProperties");
    private static readonly FieldInfo? ExtraIcons = DataType.GetField("extraIconDraws", BindingFlags.Public | BindingFlags.Instance);
    private static FieldInfo RequireField(string current, string previous) => DataType.GetField(current, BindingFlags.Instance | BindingFlags.Public) ?? DataType.GetField(previous, BindingFlags.Instance | BindingFlags.Public) ?? throw new MissingFieldException(DataType.Name, current + "/" + previous);
    [ThreadStatic] private static bool _drawingOwned;

    internal static int[] LastSubmitted { get; private set; } = Array.Empty<int>();
    internal static int DrawCalls { get; private set; }

    private static MethodBase TargetMethod() => Execute;

    private static bool Prefix(object data, object context)
    {
        if (_drawingOwned || !LocalFamilyView.FiltersIcons) return true;
        int originalCount = (int)Count.GetValue(data);
        if (originalCount <= 0) return true;
        var properties = (MaterialPropertyBlock)Properties.GetValue(data);
        object? originalExtraIcons = ExtraIcons?.GetValue(data);
        MethodInfo getOffset = AccessTools.Method(typeof(MaterialPropertyBlock), "GetInt", new[] { typeof(int) }) ?? AccessTools.Method(typeof(MaterialPropertyBlock), "GetInteger", new[] { typeof(int) }) ?? throw new MissingMethodException(nameof(MaterialPropertyBlock), "GetInt/GetInteger");
        int originalOffset = (int)getOffset.Invoke(properties, new object[] { global::Constants.instanceOffsetID })!;
        int[] members = LocalFamilyView.PhysicalMembers();
        LastSubmitted = members;
        _drawingOwned = true;
        ExtraIcons?.SetValue(data, null);
        try
        {
            for (int i = 0; i < members.Length;)
            {
                int start = members[i++];
                if (start < originalOffset || start >= originalOffset + originalCount) continue;
                int end = start + 1;
                while (i < members.Length && members[i] == end && end < originalOffset + originalCount) { i++; end++; }
                properties.SetInt(global::Constants.instanceOffsetID, start);
                Count.SetValue(data, end - start);
                Execute.Invoke(null, new[] { data, context });
                DrawCalls++;
            }
            if (originalExtraIcons != null)
            {
                Count.SetValue(data, 0);
                ExtraIcons!.SetValue(data, originalExtraIcons);
                Execute.Invoke(null, new[] { data, context });
            }
        }
        finally
        {
            properties.SetInt(global::Constants.instanceOffsetID, originalOffset);
            Count.SetValue(data, originalCount);
            ExtraIcons?.SetValue(data, originalExtraIcons);
            _drawingOwned = false;
        }
        return false;
    }
}
