using System;
using System.Collections.Generic;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Game;

public enum SessionRole
{
    Offline,
    Host,
    Client,
}

public interface IHostReplicationObserver
{
    void OnRigidSpawned(RigidTransform rigid);

    void OnRigidDeleting(RigidTransform rigid);

    void OnPropAdded(uint cellX, uint cellZ, EnvironmentMesh prop);

    void OnPropRemoved(uint cellX, uint cellZ);

    void OnAgentsCreated(IReadOnlyList<Handle> handles, IReadOnlyList<int> templateIndices, IReadOnlyList<Matrix4x4> matrices);

    void OnAgentsDeleted(IReadOnlyList<Handle> handles);

    void OnHostWorldReloaded();
}

public static class ReplicationGate
{
    private static int _authorizedDepth;

    public static SessionRole Role { get; private set; }

    public static IHostReplicationObserver? HostObserver { get; private set; }

    public static bool BlocksLocalSimulation => Role == SessionRole.Client && _authorizedDepth == 0;

    public static bool BlocksLocalAgentLifecycle =>
        BlocksLocalSimulation && !(World.worldSerializer != null && World.worldSerializer.isLoadingInProgress);

    public static void EnterHost(IHostReplicationObserver observer)
    {
        RequireOffline(SessionRole.Host);
        HostObserver = observer ?? throw new ArgumentNullException(nameof(observer));
        Role = SessionRole.Host;
    }

    public static void EnterClient()
    {
        if (Role == SessionRole.Client)
        {
            return;
        }
        RequireOffline(SessionRole.Client);
        Role = SessionRole.Client;
    }

    public static void Leave()
    {
        Role = SessionRole.Offline;
        HostObserver = null;
        _authorizedDepth = 0;
    }

    public static AuthorizedReplicationScope AuthorizeReplication()
    {
        _authorizedDepth++;
        return default;
    }

    private static void RequireOffline(SessionRole requested)
    {
        if (Role != SessionRole.Offline)
        {
            throw new InvalidOperationException($"Cannot enter {requested} while already {Role}.");
        }
    }

    public readonly struct AuthorizedReplicationScope : IDisposable
    {
        public void Dispose()
        {
            if (_authorizedDepth <= 0)
            {
                throw new InvalidOperationException("Replication scope disposed more times than it was opened.");
            }
            _authorizedDepth--;
        }
    }
}
