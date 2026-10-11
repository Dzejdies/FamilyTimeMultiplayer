using System;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Families;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Security;
using FamilyTimeMultiplayer.Core.Transport;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class IdentityScenario : ScenarioRunner
{
    private readonly ManualLogSource _log;
    private bool _seeded;
    private bool _approved;
    private PendingConnect? _approvalConnect;
    private INetConnection? _approvalConnection;
    private PlayerIdentity? _approvalIdentity;
    private HelloMessage? _approvalHello;
    private double _pendingAt = -1;
    private double _start = -1;
    private bool _rejoining;
    private bool _checked;
    private ushort _legacyChild;
    private string? _ownerKey;
    private PendingConnect? _roguePending;
    private INetConnection? _rogue;
    private PlayerIdentity? _stranger;
    private bool _forgedSent;
    private bool _forgedChecked;
    public IdentityScenario(ManualLogSource log) { _log = log; FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground = true; QualitySettings.vSyncCount = 0; FamilyTimeMultiplayer.Game.GameApiCompatibility.SetTargetFrameRate(60); }
    public override void Tick(double now, ISession? session)
    {
        if (session is HostSession host)
        {
            HostFamilyService families = HostFamilyService.Current!;
            if (!_seeded && LocalPlayer.IsWorldReady)
            {
                _seeded = true;
                FamilyWorldRecord original = families.ToRecord();
                _legacyChild = GameFamilyAccess.CurrentChildren().First().stableIndex;
                var records = original.Families.Select(family => new FamilyRecord(family.Owner, family.IsHostFamily, family.Members.Where(id => id != _legacyChild).ToArray(), family.MamaAgent)).ToList();
                records.Add(new FamilyRecord("Client", false, new[] { _legacyChild }, null));
                string hostOwner = families.HostOwner;
                families.DetachForWorldLoad();
                HostFamilyService.StartFromRecord(_log, hostOwner, new FamilyWorldRecord(records));
                _log.LogInfo("[IdentityTest] seeded an unbound legacy family");
                return;
            }
            if (!_approved && host.PendingFamilyRestores.Count > 0)
            {
                var request = host.PendingFamilyRestores[0];
                if (_pendingAt < 0)
                {
                    _pendingAt = now;
                    Check("legacy-waits-for-host", families.Identities.IsLegacy("Client") && families.Identities.PublicKeyFor("Client") == null && host.Roster.Count == 1);
                    _approvalIdentity = PlayerIdentity.Create();
                    _approvalHello = new HelloMessage(ProtocolConstants.Version, FamilyTimeMultiplayer.Game.GameBuildIdentity.Current, "Client", _approvalIdentity.PublicKey);
                    int port = Traverse.Create(host).Field<TcpNetListener>("_listener").Value.Port;
                    _approvalConnect = PendingConnect.Start("127.0.0.1", port, TimeSpan.FromSeconds(5));
                }
                if (_approvalConnect!.IsCompleted && _approvalConnection == null)
                {
                    _approvalConnection = _approvalConnect.Connection ?? throw new InvalidOperationException(_approvalConnect.Error);
                    _approvalConnection.Send(_approvalHello!);
                }
                if (_approvalConnection != null)
                {
                    while (_approvalConnection.TryReceive(out INetMessage incoming))
                    {
                        if (incoming is IdentityChallengeMessage challenge)
                        {
                            _approvalConnection.Send(new IdentityProofMessage(_approvalIdentity!.SignHandshake(challenge.Challenge, _approvalHello!)));
                            _approvalConnection = new SignedClientConnection(_approvalConnection, _approvalIdentity, challenge.Challenge);
                        }
                    }
                }
                if (now - _pendingAt > 3 && host.PendingFamilyRestores.Count == 2)
                {
                    HostSession.FamilyRestoreRequest other = host.PendingFamilyRestores.Single(candidate => candidate.PlayerId != request.PlayerId);
                    host.DecideFamilyRestore(request.PlayerId, HostSession.FamilyRestoreChoice.Restore);
                    host.DecideFamilyRestore(other.PlayerId, HostSession.FamilyRestoreChoice.Restore);
                    Check("stale-restore-request-rejected", families.Identities.OwnerOf(_approvalIdentity!.PublicKey) == null && !host.PendingFamilyRestores.Any(candidate => candidate.PlayerId == other.PlayerId));
                    _approvalConnection!.Dispose();
                    _approvalIdentity.Dispose();
                    _approved = true;
                    _ownerKey = families.Identities.PublicKeyFor("Client");
                    Check("host-approved-binding", _ownerKey != null && !families.Identities.IsLegacy("Client"));
                }
            }
            if (_approved && _ownerKey != null && _roguePending == null && HasLivePair(session))
            {
                _start = now;
                int port = Traverse.Create(host).Field<TcpNetListener>("_listener").Value.Port;
                _roguePending = PendingConnect.Start("127.0.0.1", port, TimeSpan.FromSeconds(5));
                _stranger = PlayerIdentity.Create();
            }
            if (_roguePending?.IsCompleted == true && _rogue == null)
            {
                _rogue = _roguePending.Connection ?? throw new InvalidOperationException(_roguePending.Error);
                _rogue.Send(new HelloMessage(ProtocolConstants.Version, FamilyTimeMultiplayer.Game.GameBuildIdentity.Current, "Client", _ownerKey!));
            }
            if (_rogue != null)
            {
                while (_rogue.TryReceive(out INetMessage message))
                {
                    if (message is IdentityChallengeMessage challenge) { _rogue.Send(new IdentityProofMessage(_stranger!.SignHandshake(challenge.Challenge, new HelloMessage(ProtocolConstants.Version, FamilyTimeMultiplayer.Game.GameBuildIdentity.Current, "Client", _stranger.PublicKey)))); _forgedSent = true; }
                }
                if (_forgedSent && !_forgedChecked && !_rogue.IsOpen)
                {
                    _forgedChecked = true;
                    Check("forged-owner-proof-rejected", families.Identities.OwnerOf(_ownerKey!) == "Client" && families.MembersOf("Client").Contains(_legacyChild));
                    _rogue.Dispose(); _stranger!.Dispose();
                }
            }
            if (!_checked && _forgedChecked && host.RemotePlayers.Any(entry => entry.Name == "RenamedClient"))
            {
                _checked = true;
                int player = host.Roster.Single(entry => entry.Name == "RenamedClient").PlayerId;
                Check("renamed-rejoin-keeps-family", families.OwnerOfPlayer(player) == "Client" && families.MembersOf("Client").SequenceEqual(new[] { _legacyChild }));
                using var saved = new MemoryStream(); families.ToRecord().Write(saved); saved.Position = 0;
                Check("saved-binding-restored", FamilyWorldRecord.Read(saved).Families.Single(family => family.Owner == "Client").PublicKey == _ownerKey);
            }
        }
        else if (session is ClientSession client && client.IsLive)
        {
            if (_start < 0) { _start = now; Check("authenticated-join-live", true); }
            if (!_rejoining && now - _start > 8)
            {
                _rejoining = true;
                MultiplayerController.Current!.Leave();
                MultiplayerController.Current.Settings.OverridePlayerNameForThisRun("RenamedClient");
                MultiplayerController.Current.Join("127.0.0.1");
            }
            else if (_rejoining && !_checked && MultiplayerController.Current!.Settings.PlayerName == "RenamedClient" && now - _start > 10)
            {
                _checked = true;
                Check("authenticated-rejoin-live", client.Roster.Any(entry => entry.Name == "RenamedClient"));
            }
        }
    }
    private void Check(string name, bool success) => _log.LogInfo($"[IdentityTest] {(success ? "PASS" : "FAIL")} {name}");
}
