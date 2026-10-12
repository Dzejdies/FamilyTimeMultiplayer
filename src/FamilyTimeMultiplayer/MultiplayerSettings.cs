using System;
using BepInEx.Configuration;
using FamilyTimeMultiplayer.Core.Protocol;
using UnityEngine.InputSystem;

namespace FamilyTimeMultiplayer;

public sealed class MultiplayerSettings
{
    private const int CurrentMenuKeyLayout = 1;
    private MultiplayerSettings(
        ConfigEntry<string> playerName,
        ConfigEntry<int> port,
        ConfigEntry<int> maxPlayers,
        ConfigEntry<string> lastJoinAddress,
        ConfigEntry<int> snapshotRateHz,
        ConfigEntry<int> poseRateHz,
        ConfigEntry<int> interpolationDelayMs,
        ConfigEntry<Key> toggleWindowKey,
        ConfigEntry<FamilyMode> familyMode,
        ConfigEntry<bool> showMenuOnStartup,
        ConfigEntry<string> relayAddress)
    {
        RelayAddressEntry = relayAddress;
        FamilyModeEntry = familyMode;
        ShowMenuOnStartupEntry = showMenuOnStartup;
        PlayerNameEntry = playerName;
        PortEntry = port;
        MaxPlayersEntry = maxPlayers;
        LastJoinAddressEntry = lastJoinAddress;
        SnapshotRateHzEntry = snapshotRateHz;
        PoseRateHzEntry = poseRateHz;
        InterpolationDelayMsEntry = interpolationDelayMs;
        ToggleWindowKeyEntry = toggleWindowKey;
    }

    public ConfigEntry<string> PlayerNameEntry { get; }
    public ConfigEntry<int> PortEntry { get; }
    public ConfigEntry<int> MaxPlayersEntry { get; }
    public ConfigEntry<string> LastJoinAddressEntry { get; }
    public ConfigEntry<int> SnapshotRateHzEntry { get; }
    public ConfigEntry<int> PoseRateHzEntry { get; }
    public ConfigEntry<int> InterpolationDelayMsEntry { get; }
    public ConfigEntry<Key> ToggleWindowKeyEntry { get; }
    public ConfigEntry<FamilyMode> FamilyModeEntry { get; }
    public ConfigEntry<bool> ShowMenuOnStartupEntry { get; }
    public ConfigEntry<string> RelayAddressEntry { get; }

    private string? _relayOverride;

    public string RelayAddress => _relayOverride ?? RelayAddressEntry.Value;

    public void OverrideRelayForThisRun(string address) => _relayOverride = address;

    private string? _playerNameOverride;

    public string PlayerName => _playerNameOverride ?? PlayerNameEntry.Value;

    public void OverridePlayerNameForThisRun(string name) => _playerNameOverride = name;

    public double SnapshotInterval => 1.0 / SnapshotRateHzEntry.Value;
    public double PoseInterval => 1.0 / PoseRateHzEntry.Value;
    public double InterpolationDelaySeconds => InterpolationDelayMsEntry.Value / 1000.0;

    public static MultiplayerSettings Bind(ConfigFile config)
    {
        ConfigEntry<Key> menuKey = config.Bind("UI", "ToggleWindowKey", Key.F9,
            "Key that opens the Multiplayer page of the pause book.");
        MigrateMenuKey(config, menuKey);

        return new MultiplayerSettings(
            config.Bind("Player", "Name", "Player", "Name shown to other players."),
            config.Bind("Network", "Port", ProtocolConstants.DefaultPort,
                new ConfigDescription("TCP port used when hosting.", new AcceptableValueRange<int>(1, 65535))),
            config.Bind("Network", "MaxPlayers", 4,
                new ConfigDescription("Players allowed in a hosted session, host included.", new AcceptableValueRange<int>(2, 16))),
            config.Bind("Network", "LastJoinAddress", "", "Room code or address prefilled in the Join field."),
            config.Bind("Sync", "SnapshotRateHz", 15,
                new ConfigDescription("How often the host streams agent positions.", new AcceptableValueRange<int>(1, 60))),
            config.Bind("Sync", "PoseRateHz", 30,
                new ConfigDescription("How often a client sends its own position.", new AcceptableValueRange<int>(1, 60))),
            config.Bind("Sync", "InterpolationDelayMs", 120,
                new ConfigDescription("Client render delay behind the host, smooths out jitter.", new AcceptableValueRange<int>(0, 1000))),
            menuKey,
            config.Bind("Session", "FamilyMode", FamilyMode.Shared, "Shared: one family, kids shared by all players. Separate: every player gets their own mama and kids."),
            config.Bind("UI", "ShowMenuOnStartup", true, "Open the Multiplayer page when the game starts so you can pick Solo, Host or Join."),
            config.Bind("Network", "RelayAddress", "92.5.59.1:27780", "Relay server (host or host:port, default port 27780). When set, hosting also opens a room code that friends can join without port forwarding. Empty = off."));
    }

    private static void MigrateMenuKey(ConfigFile config, ConfigEntry<Key> menuKey)
    {
        ConfigEntry<int> layout = config.Bind("UI", "MenuKeyLayoutVersion", 0,
            "Multiplayer menu key migration version.");
        if (layout.Value >= CurrentMenuKeyLayout) return;

        if (menuKey.Value == Key.F8) menuKey.Value = Key.F9;
        layout.Value = CurrentMenuKeyLayout;
        config.Save();
    }
}
