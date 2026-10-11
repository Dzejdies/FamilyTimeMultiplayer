using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using Unity.Mathematics;

namespace FamilyTimeMultiplayer.Game;

public static class PropReplication
{
    public static PropChunkManager RequireManager() =>
        GatInterop.RequireAlive(World.propChunkManager, "World.propChunkManager is missing.");

    public static EnvironmentMesh ResolveProp(string name)
    {
        foreach (EnvironmentMesh mesh in RequireManager().PropMeshes)
        {
            if (mesh != null && mesh.name == name)
            {
                return mesh;
            }
        }
        throw new System.IO.InvalidDataException($"Prop '{name}' from the host is not known on this client.");
    }

    public static void Apply(PropChangeMessage message)
    {
        PropChunkManager manager = RequireManager();
        using (ReplicationGate.AuthorizeReplication())
        {
            foreach (PropChange change in message.Changes)
            {
                var cell = new uint2(change.CellX, change.CellZ);
                if (change.Added)
                {
                    manager.AddProp(cell, ResolveProp(change.PropName));
                }
                else
                {
                    manager.RemoveProp(cell);
                }
            }
        }
    }
}
