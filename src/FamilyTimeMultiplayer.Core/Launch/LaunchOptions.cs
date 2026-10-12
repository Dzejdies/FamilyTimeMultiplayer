using System;
using System.Collections.Generic;
using System.Globalization;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Launch;

public enum LaunchAction
{
    None,
    Host,
    Join,
}

public sealed class LaunchOptions
{
    public const string HostFlag = "-ftmp-host";
    public const string JoinFlag = "-ftmp-join";
    public const string NameFlag = "-ftmp-name";
    public const string ScenarioFlag = "-ftmp-scenario";
    public const string LoadFlag = "-ftmp-load";
    public const string SaveFlag = "-ftmp-save";
    public const string FamilyFlag = "-ftmp-family";
    public const string RelayFlag = "-ftmp-relay";

    private LaunchOptions(LaunchAction action, string? joinHost, int? port, string? playerName, string? scenario, string? loadMap, string? saveMap, FamilyMode? familyMode, string? joinText, string? relayAddress)
    {
        JoinText = joinText;
        RelayAddress = relayAddress;
        FamilyMode = familyMode;
        LoadMap = loadMap;
        SaveMap = saveMap;
        Action = action;
        JoinHost = joinHost;
        Port = port;
        PlayerName = playerName;
        Scenario = scenario;
    }

    public LaunchAction Action { get; }
    public string? JoinHost { get; }
    public int? Port { get; }
    public string? PlayerName { get; }
    public string? Scenario { get; }
    public string? LoadMap { get; }
    public string? SaveMap { get; }
    public FamilyMode? FamilyMode { get; }
    public string? JoinText { get; }
    public string? RelayAddress { get; }
    public bool UsesMultiplayerSaveCollection => Action != LaunchAction.None || LoadMap != null || SaveMap != null;

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        LaunchAction action = LaunchAction.None;
        string? joinHost = null;
        int? port = null;
        string? name = null;
        string? scenario = null;
        string? loadMap = null;
        string? saveMap = null;
        FamilyMode? familyMode = null;
        string? joinText = null;
        string? relayAddress = null;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case HostFlag:
                    RequireSingleAction(action, HostFlag);
                    action = LaunchAction.Host;
                    if (TryPeekValue(args, i, out string? hostPort))
                    {
                        port = ParsePort(hostPort!, HostFlag);
                        i++;
                    }
                    break;
                case JoinFlag:
                    RequireSingleAction(action, JoinFlag);
                    action = LaunchAction.Join;
                    if (!TryPeekValue(args, i, out string? endpoint))
                    {
                        throw new ArgumentException($"{JoinFlag} needs an address, e.g. {JoinFlag} 127.0.0.1:27777.");
                    }
                    joinText = endpoint!;
                    if (!Transport.Relay.RoomCode.TryNormalize(endpoint, out _))
                    {
                        (joinHost, port) = ParseEndpoint(endpoint!);
                    }
                    i++;
                    break;
                case NameFlag:
                    name = RequireValue(args, ref i, NameFlag);
                    break;
                case ScenarioFlag:
                    scenario = RequireValue(args, ref i, ScenarioFlag);
                    break;
                case LoadFlag:
                    loadMap = RequireValue(args, ref i, LoadFlag);
                    break;
                case SaveFlag:
                    saveMap = RequireValue(args, ref i, SaveFlag);
                    break;
                case RelayFlag:
                    relayAddress = RequireValue(args, ref i, RelayFlag);
                    ParseEndpoint(relayAddress);
                    break;
                case FamilyFlag:
                    familyMode = ParseFamilyMode(RequireValue(args, ref i, FamilyFlag));
                    break;
            }
        }
        return new LaunchOptions(action, joinHost, port, name, scenario, loadMap, saveMap, familyMode, joinText, relayAddress);
    }

    public static (string Host, int? Port) ParseEndpoint(string endpoint)
    {
        string trimmed = endpoint.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Address is empty.");
        }
        int colon = trimmed.LastIndexOf(':');
        if (colon < 0)
        {
            return (trimmed, null);
        }
        if (colon == 0)
        {
            throw new ArgumentException($"Address '{endpoint}' has no host before the port.");
        }
        return (trimmed.Substring(0, colon), ParsePort(trimmed.Substring(colon + 1), "address"));
    }

    private static FamilyMode ParseFamilyMode(string value) => value.ToLowerInvariant() switch
    {
        "shared" => Protocol.FamilyMode.Shared,
        "separate" => Protocol.FamilyMode.Separate,
        _ => throw new ArgumentException($"{FamilyFlag}: '{value}' is not a family mode; use shared or separate."),
    };

    private static int ParsePort(string value, string context)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
        {
            throw new ArgumentException($"{context}: '{value}' is not a port in 1..65535.");
        }
        return port;
    }

    private static void RequireSingleAction(LaunchAction current, string flag)
    {
        if (current != LaunchAction.None)
        {
            throw new ArgumentException($"{flag} conflicts with an earlier {HostFlag}/{JoinFlag}.");
        }
    }

    private static bool TryPeekValue(IReadOnlyList<string> args, int index, out string? value)
    {
        if (index + 1 < args.Count && !args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            value = args[index + 1];
            return true;
        }
        value = null;
        return false;
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string flag)
    {
        if (!TryPeekValue(args, index, out string? value))
        {
            throw new ArgumentException($"{flag} needs a value.");
        }
        index++;
        return value!;
    }
}
