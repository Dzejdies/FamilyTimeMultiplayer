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

internal sealed class AddonContext : IReplicatedSessionAddonContext
{
    private readonly string _id;
    private readonly Func<int> _localPlayer;
    private readonly Func<long> _generation;
    private readonly Func<double> _hostTime;
    private readonly Action<int?, AddonMessage> _send;
    private readonly Dictionary<(int Player, ushort Agent), CommandOwnershipToken> _claims = new Dictionary<(int, ushort), CommandOwnershipToken>();
    private bool _stopped;
    public AddonContext(string id, bool host, Func<int> localPlayer, Func<long> generation, Action<int?, AddonMessage> send, Func<double> hostTime, IAddonReplicationEndpoint replication) { Replication = new AddonReplicationContext(id, replication, RequireActive); _id = id; IsHost = host; _localPlayer = localPlayer; _generation = generation; _send = send; _hostTime = hostTime; }
    public IAddonReplicationContext Replication { get; }
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
