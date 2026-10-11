using System;
using FamilyTimeMultiplayer.Families;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Patches;

[HarmonyPatch(typeof(FakeGATManager), nameof(FakeGATManager.CreateFakeGAT), typeof(Matrix4x4), typeof(Action<Handle>))]
internal static class MamaAgentSuppressionPatch
{
    private static bool Prefix(ref Handle __result)
    {
        if (!MamaCloning.SuppressNewAgent)
        {
            return true;
        }
        __result = Handle.Null;
        return false;
    }
}
