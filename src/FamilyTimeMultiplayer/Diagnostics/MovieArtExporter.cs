using System.IO;
using BepInEx.Logging;
using GAT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class MovieArtExporter
{
    private static readonly string[] SleepMovieFields = { "missingChildPrefab", "hungryChildPrefab", "angryPrefab", "happyChildPrefab" };

    private readonly ManualLogSource _log;

    public MovieArtExporter(ManualLogSource log)
    {
        _log = log;
    }

    public void ExportSleepMovies(string outputDirectory)
    {
        if (!HasTextureReadback())
        {
            _log.LogInfo("[Art] texture readback is unavailable in this game build");
            return;
        }
        BedManager[] beds = Resources.FindObjectsOfTypeAll<BedManager>();
        if (beds.Length == 0)
        {
            _log.LogWarning("[Art] no BedManager loaded");
            return;
        }
        Directory.CreateDirectory(outputDirectory);
        foreach (string field in SleepMovieFields)
        {
            GameObject? prefab = Traverse.Create(beds[0]).Field<GameObject>(field).Value;
            if (prefab == null)
            {
                _log.LogInfo($"[Art] {field}: none");
                continue;
            }
            _log.LogInfo($"[Art] {field} = {prefab.name}");
            foreach (Graphic graphic in prefab.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                Texture? texture = graphic.mainTexture;
                string sprite = graphic is Image image && image.sprite != null ? image.sprite.name : "-";
                _log.LogInfo($"[Art]   {graphic.gameObject.name} {graphic.GetType().Name} sprite={sprite} tex={(texture != null ? $"{texture.name} {texture.width}x{texture.height}" : "none")}");

            }
        }
        _log.LogInfo($"[Art] inspected sleep art in {outputDirectory}");
    }

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }

    private static bool HasTextureReadback() =>
        HarmonyLib.AccessTools.Method(typeof(Texture2D), "ReadPixels") != null &&
        HarmonyLib.AccessTools.Method(typeof(Texture2D), "EncodeToPNG") != null;
}
