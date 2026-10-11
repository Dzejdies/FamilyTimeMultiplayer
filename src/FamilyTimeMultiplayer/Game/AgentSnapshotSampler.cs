using System;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Game;

public sealed class AgentSnapshotSampler
{
    private const int MatrixStrideBytes = 64;

    private bool _inFlight;

    public bool InFlight => _inFlight;

    public int LastRejectedEntries { get; private set; }

    public void Request(Action<IReadOnlyList<AgentState>> onSampled, Action<string> onFailed)
    {
        if (_inFlight)
        {
            throw new InvalidOperationException("An agent snapshot readback is already in flight.");
        }
        GatSimulation gat = GatInterop.RequireGat();
        int count = gat.instanceCount;
        ComputeBuffer? matrices = gat.GetInstanceMatricesBuffer();
        if (count <= 0 || matrices == null)
        {
            onSampled(Array.Empty<AgentState>());
            return;
        }
        _inFlight = true;
        AsyncGPUReadback.Request(matrices, count * MatrixStrideBytes, 0, request =>
        {
            _inFlight = false;
            if (World.isShuttingDown)
            {
                return;
            }
            if (request.hasError)
            {
                onFailed("GPU readback of GAT instance matrices failed.");
                return;
            }
            onSampled(Decode(request.GetData<Matrix4x4>()));
        });
    }

    private List<AgentState> Decode(NativeArray<Matrix4x4> data)
    {
        GatSimulation? gat = World.gat;
        var agents = new List<AgentState>(data.Length);
        int rejected = 0;
        for (int i = 0; i < data.Length; i++)
        {
            Matrix4x4 matrix = data[i];
            if (!GatInterop.TryReadStableIndex(matrix, out ushort stableIndex)
                || gat == null
                || !gat.IsHandleValid(GatInterop.ToHandle(stableIndex)))
            {
                rejected++;
                continue;
            }
            NetPose pose = GatInterop.ToNetPose(matrix);
            if (HostPlayerAuthority.TryGetAddonPose(stableIndex, out ClientPoseMessage authoritative))
            {
                Quaternion rotation = Quaternion.Euler(0, authoritative.YawDegrees, 0);
                pose = new NetPose(authoritative.Position, new NetQuaternion(rotation.x, rotation.y, rotation.z, rotation.w), pose.Scale);
            }
            if (!pose.IsFinite)
            {
                rejected++;
                continue;
            }
            agents.Add(new AgentState(stableIndex, pose));
        }
        LastRejectedEntries = rejected;
        return agents;
    }
}
