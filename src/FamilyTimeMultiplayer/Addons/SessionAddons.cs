using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;

namespace FamilyTimeMultiplayer.Addons;

public interface ISessionAddon
{
    AddonDescriptor Descriptor { get; }
    void OnSessionStarted(ISessionAddonContext context);
    void OnSessionStopped();
    void OnPeerReady(int playerId);
    void OnPeerLeft(int playerId);
    void OnMessage(int sender, byte[] payload);
    void OnWorldReloaded();
    bool TryGetAuthoritativePlayerPose(int playerId, out ClientPoseMessage pose);
}

public interface ISessionAddonActionPolicy : Core.Addons.INativeActionPolicy
{
}

public interface ISessionAddonContext
{
    bool IsHost { get; }
    int LocalPlayerId { get; }
    double HostTime { get; }
    long WorldGeneration { get; }
    void SendToHost(byte[] payload);
    void SendToPlayer(int playerId, byte[] payload);
    void Broadcast(byte[] payload);
    void RequireNear(int playerId, NetVector3 point, float reach);
    void RequireOwnedAgent(int playerId, ushort agent);
    string GetFamilyOwner(int playerId);
    bool TryClaimAgent(int playerId, ushort agent);
    void ReleaseAgent(int playerId, ushort agent);
}

public static class SessionAddonRegistry
{
    private static readonly List<ISessionAddon> Registered = new List<ISessionAddon>();
    private static SessionAddonRuntime? _active;
    public static void Register(ISessionAddon addon)
    {
        if (addon == null) throw new ArgumentNullException(nameof(addon));
        if (_active != null) throw new InvalidOperationException("Register addons before starting multiplayer.");
        if (Registered.Any(item => item.Descriptor.Id == addon.Descriptor.Id)) throw new InvalidOperationException("Addon already registered.");
        AddonCapabilities.Normalize(Registered.Select(item => item.Descriptor).Append(addon.Descriptor));
        Registered.Add(addon);
    }
    public static IReadOnlyList<AddonDescriptor> Descriptors => AddonCapabilities.Normalize(Registered.Select(item => item.Descriptor));
    internal static SessionAddonRuntime Start(bool host, Func<int> localPlayer, Func<long> generation, Action<int?, AddonMessage> send, Func<double>? hostTime = null)
    {
        if (_active != null) throw new InvalidOperationException("A multiplayer addon session is already active.");
        _active = new SessionAddonRuntime(Registered.ToArray(), host, localPlayer, generation, send, hostTime ?? (() => GameClock.Now));
        try { _active.Start(); return _active; }
        catch { _active.Dispose(); throw; }
    }
    internal static void Release(SessionAddonRuntime runtime) { if (ReferenceEquals(_active, runtime)) _active = null; }
}

internal sealed class SessionAddonRuntime : IDisposable
{
    private readonly ISessionAddon[] _addons;
    private readonly bool _host;
    private readonly List<AddonContext> _contexts = new List<AddonContext>();
    private bool _disposed;
    private readonly Dictionary<int, HashSet<string>> _peerAddons = new Dictionary<int, HashSet<string>>();
    public SessionAddonRuntime(ISessionAddon[] addons, bool host, Func<int> localPlayer, Func<long> generation, Action<int?, AddonMessage> send, Func<double> hostTime)
    {
        _addons = addons;
        _host = host;
        foreach (ISessionAddon addon in addons) _contexts.Add(new AddonContext(addon.Descriptor.Id, host, localPlayer, generation, send, hostTime));
    }
    public void Start() { for (int index = 0; index < _addons.Length; index++) _addons[index].OnSessionStarted(_contexts[index]); }
    public void PeerReady(int id, IEnumerable<AddonDescriptor> capabilities)
    {
        _peerAddons[id] = new HashSet<string>(capabilities.Select(addon => addon.Id), StringComparer.Ordinal);
        foreach (ISessionAddon addon in AddonsForPeer(id)) addon.OnPeerReady(id);
    }
    public void PeerLeft(int id)
    {
        foreach (ISessionAddon addon in AddonsForPeer(id)) addon.OnPeerLeft(id);
        _peerAddons.Remove(id);
        foreach (AddonContext context in _contexts) context.ReleasePlayer(id);
    }
    private IEnumerable<ISessionAddon> AddonsForPeer(int id) =>
        _peerAddons.TryGetValue(id, out HashSet<string>? supported) ? _addons.Where(addon => supported.Contains(addon.Descriptor.Id)) : Array.Empty<ISessionAddon>();
    public void Reload() { foreach (AddonContext context in _contexts) context.ReleaseClaims(); foreach (ISessionAddon addon in _addons) addon.OnWorldReloaded(); }
    public void Receive(int sender, AddonMessage message)
    {
        ISessionAddon addon = _addons.SingleOrDefault(item => item.Descriptor.Id == message.AddonId) ?? throw new InvalidDataException("Unknown session addon.");
        if (!AddonsForPeer(sender).Contains(addon)) throw new InvalidDataException("Addon is not available for this peer.");
        if (_host)
        {
            HostCommandHub commands = HostCommandHub.Current ?? throw new InvalidOperationException("Host command service is missing.");
            commands.RunAsPlayer(sender, () => addon.OnMessage(sender, message.Payload));
        }
        else addon.OnMessage(sender, message.Payload);
    }
    public bool TryPose(int playerId, out ClientPoseMessage pose)
    {
        pose = null!;
        bool found = false;
        foreach (ISessionAddon addon in AddonsForPlayer(playerId))
        {
            if (!addon.TryGetAuthoritativePlayerPose(playerId, out ClientPoseMessage candidate)) continue;
            if (found) throw new InvalidOperationException("Multiple addons claimed player movement authority.");
            pose = candidate; found = true;
        }
        return found;
    }
    private IEnumerable<ISessionAddon> AddonsForPlayer(int playerId) =>
        playerId == PlayerRosterMessage.HostPlayerId && _host ? _addons : AddonsForPeer(playerId);

    public bool AllowsNativeAction(int playerId, INetMessage message) =>
        Core.Addons.NativeActionPolicy.Allows(playerId, message, AddonsForPlayer(playerId).OfType<ISessionAddonActionPolicy>());

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { foreach (ISessionAddon addon in _addons) addon.OnSessionStopped(); }
        finally { foreach (AddonContext context in _contexts) context.Stop(); SessionAddonRegistry.Release(this); }
    }
}

internal sealed class AddonContext : ISessionAddonContext
{
    private readonly string _id;
    private readonly Func<int> _localPlayer;
    private readonly Func<long> _generation;
    private readonly Func<double> _hostTime;
    private readonly Action<int?, AddonMessage> _send;
    private readonly Dictionary<(int Player, ushort Agent), CommandOwnershipToken> _claims = new Dictionary<(int, ushort), CommandOwnershipToken>();
    private bool _stopped;
    public AddonContext(string id, bool host, Func<int> localPlayer, Func<long> generation, Action<int?, AddonMessage> send, Func<double> hostTime) { _id = id; IsHost = host; _localPlayer = localPlayer; _generation = generation; _send = send; _hostTime = hostTime; }
    public bool IsHost { get; }
    public int LocalPlayerId => _localPlayer();
    public double HostTime => _hostTime();
    public long WorldGeneration => _generation();
    private void RequireActive() { if (_stopped) throw new ObjectDisposedException(nameof(AddonContext)); }
    private void RequireHost() { RequireActive(); if (!IsHost) throw new InvalidOperationException("This addon operation requires the host."); }
    public void SendToHost(byte[] payload) { RequireActive(); if (IsHost) throw new InvalidOperationException("Host cannot send to itself."); _send(PlayerRosterMessage.HostPlayerId, new AddonMessage(_id, WorldGeneration, payload)); }
    public void SendToPlayer(int playerId, byte[] payload) { RequireHost(); _send(playerId, new AddonMessage(_id, WorldGeneration, payload)); }
    public void Broadcast(byte[] payload) { RequireHost(); _send(null, new AddonMessage(_id, WorldGeneration, payload)); }
    public void RequireNear(int playerId, NetVector3 point, float reach) { RequireHost(); HostPlayerAuthority.RequireNear(playerId, GatInterop.ToUnity(point), reach); }
    public void RequireOwnedAgent(int playerId, ushort agent) { RequireHost(); HostPlayerAuthority.RequireOwnedAgent(playerId, agent); }
    public string GetFamilyOwner(int playerId) { RequireHost(); return Families.HostFamilyService.Current?.OwnerOfPlayer(playerId) ?? "shared"; }
    private static readonly MethodInfo AcquireMethod = typeof(CommandOwnershipService).GetMethods().Single(method => method.Name == "TryAcquire" && (method.GetParameters().Length == 3 || method.GetParameters().Length == 4));
    private static bool Acquire(ushort agent, out CommandOwnershipToken token)
    {
        object?[] arguments = AcquireMethod.GetParameters().Length == 3
            ? new object?[] { ClaimKind(), new[] { GatInterop.ToHandle(agent) }, null }
            : new object?[] { ClaimKind(), new[] { GatInterop.ToHandle(agent) }, true, null };
        bool acquired = (bool)AcquireMethod.Invoke(World.commandOwnershipService, arguments)!;
        token = (CommandOwnershipToken)arguments[arguments.Length - 1]!;
        return acquired;
    }
    private static CommandOwnershipKind ClaimKind()
    {
        if (Enum.TryParse("MoveLease", out CommandOwnershipKind flightLease)) return flightLease;
        if (Enum.TryParse("Station", out CommandOwnershipKind stationLease)) return stationLease;
        throw new InvalidOperationException("This game build has no supported addon command ownership kind.");
    }
    public bool TryClaimAgent(int playerId, ushort agent)
    {
        RequireOwnedAgent(playerId, agent);
        if (_claims.TryGetValue((playerId, agent), out CommandOwnershipToken existing))
        {
            if (existing.IsActive && existing.StableIndices.Contains(agent)) return true;
            _claims.Remove((playerId, agent));
        }
        if (!Acquire(agent, out CommandOwnershipToken token)) return false;
        _claims.Add((playerId, agent), token); return true;
    }
    public void ReleaseAgent(int playerId, ushort agent)
    {
        RequireHost();
        if (!_claims.TryGetValue((playerId, agent), out CommandOwnershipToken token)) return;
        if (World.commandOwnershipService != null && token.IsActive) World.commandOwnershipService.ReleaseCommandOwnership(token);
        _claims.Remove((playerId, agent));
    }
    public void ReleasePlayer(int playerId) { foreach (var key in _claims.Keys.Where(key => key.Player == playerId).ToArray()) ReleaseAgent(key.Player, key.Agent); }
    public void ReleaseClaims() { foreach (var key in _claims.Keys.ToArray()) ReleaseAgent(key.Player, key.Agent); }
    public void Stop() { ReleaseClaims(); _stopped = true; }
}
