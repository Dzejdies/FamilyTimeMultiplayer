using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game;

public static class GameBuildIdentity
{
    private const string BuildGuidPrefix = "build-guid=";
    private static readonly Lazy<string> BuildId = new Lazy<string>(ReadBuildId);

    public static string Current => BuildId.Value;

    private static string ReadBuildId()
    {
        string? unityBuildId = AccessTools.Property(typeof(Application), "buildGUID")?.GetValue(null) as string;
        if (!string.IsNullOrWhiteSpace(unityBuildId)) return unityBuildId;
        string bootConfigPath = Path.Combine(Application.dataPath, "boot.config");
        string? configuredId = File.ReadLines(bootConfigPath).FirstOrDefault(line => line.StartsWith(BuildGuidPrefix, StringComparison.Ordinal));
        return configuredId?.Substring(BuildGuidPrefix.Length).Trim()
            ?? throw new InvalidDataException($"Game build identity is missing from {bootConfigPath}.");
    }
}
