using System;
using System.Collections;
using System.IO;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Families;
using FamilyTimeMultiplayer.Game;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Families;

public sealed class FamilyWorldPersistence : MonoBehaviour
{
    private const int SectionOrder = 100000;

    private ManualLogSource _log = null!;
    private MultiplayerSettings _settings = null!;
    private WorldSerializer? _registeredWith;

    public void Initialize(ManualLogSource log, MultiplayerSettings settings)
    {
        _log = log;
        _settings = settings;
    }

    private void Update()
    {
        WorldSerializer? serializer = World.worldSerializer;
        if (serializer != null && serializer != _registeredWith)
        {
            serializer.Register(FamilyWorldRecord.SectionId, this, SectionOrder, SaveSection, LoadSection);
            _registeredWith = serializer;
        }
        HostFamilyService? families = HostFamilyService.Current;
        if (families != null && LocalPlayer.IsWorldReady && World.worldSerializer?.isLoadingInProgress != true)
        {
            families.Tick(FamilyTimeMultiplayer.Game.GameClock.Now);
            FamilyHud.Set(families.CountsFor(families.HostOwner));
        }
    }

    private static bool _loadingHostSentWorld;

    public static void DetachBeforeWorldLoad(string mapName)
    {
        _loadingHostSentWorld = string.Equals(mapName, WorldFiles.ClientReceivedMapName, StringComparison.Ordinal);
        if (ReplicationGate.Role == SessionRole.Client)
        {
            return;
        }
        HostFamilyService.Current?.DetachForWorldLoad();
        FamilyHud.Clear();
    }

    private IEnumerator SaveSection(MemoryStream stream)
    {
        FamilyWorldRecord record = ReplicationGate.Role == SessionRole.Client
            ? FamilyWorldRecord.Empty
            : HostFamilyService.Current?.ToRecord() ?? FamilyWorldRecord.Empty;
        record.Write(stream);
        if (record.IsSeparateWorld)
        {
            _log.LogInfo($"[Family] saved {record.Families.Count} famil(ies) with the world");
        }
        yield break;
    }

    private IEnumerator LoadSection(MemoryStream stream)
    {
        FamilyWorldRecord record = FamilyWorldRecord.Read(stream);
        if (ReplicationGate.Role == SessionRole.Client || _loadingHostSentWorld || !record.IsSeparateWorld)
        {
            yield break;
        }
        HostFamilyService.StartFromRecord(_log, _settings.PlayerName, record);
        _log.LogInfo($"[Family] this world has separate families ({record.Families.Count}); restored them");
    }
}
