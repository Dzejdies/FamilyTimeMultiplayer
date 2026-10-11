using System;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Interpolation;
using GAT;
using UnityEngine;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Game;

public sealed class AgentPuppeteer
{
    private Matrix4x4[] _matrices = Array.Empty<Matrix4x4>();
    private Vector3[] _velocities = Array.Empty<Vector3>();
    private bool[] _written = Array.Empty<bool>();
    private readonly Dictionary<ushort, Matrix4x4> _lastAppliedByLocalIndex = new Dictionary<ushort, Matrix4x4>();

    public int LastAppliedCount { get; private set; }

    public HashSet<ushort> HeldLocally { get; } = new HashSet<ushort>();

    public int LastUnmappedCount { get; private set; }

    public void Apply(IReadOnlyList<InterpolatedEntry<ushort>> agents, AgentIdentityMap identities, ushort ownLocalStableIndex)
    {
        GatSimulation gat = GatInterop.RequireGat();
        int count = gat.instanceCount;
        ComputeBuffer? matrixBuffer = gat.GetInstanceMatricesBuffer();
        ComputeBuffer? velocityBuffer = gat.GetInstanceVelocityBuffer();
        if (count <= 0 || matrixBuffer == null || velocityBuffer == null)
        {
            LastAppliedCount = 0;
            return;
        }
        EnsureCapacity(count);
        Array.Clear(_written, 0, count);
        int applied = 0;
        int unmapped = 0;
        for (int i = 0; i < agents.Count; i++)
        {
            InterpolatedEntry<ushort> agent = agents[i];
            if (!identities.TryGetLocal(agent.Key, out ushort localIndex))
            {
                unmapped++;
                continue;
            }
            if (localIndex == ownLocalStableIndex || HeldLocally.Contains(localIndex))
            {
                continue;
            }
            ushort physical = gat.ResolveHandle(GatInterop.ToHandle(localIndex));
            if (physical >= count)
            {
                continue;
            }
            Matrix4x4 matrix = GatInterop.ToPuppetGpuMatrix(agent.Pose, localIndex);
            _matrices[physical] = matrix;
            _velocities[physical] = GatInterop.ToUnity(agent.Velocity);
            _written[physical] = true;
            _lastAppliedByLocalIndex[localIndex] = matrix;
            applied++;
        }
        UploadContiguousRuns(matrixBuffer, velocityBuffer, count);
        LastAppliedCount = applied;
        LastUnmappedCount = unmapped;
    }

    public void Forget(ushort localStableIndex) => _lastAppliedByLocalIndex.Remove(localStableIndex);

    public void ReleaseAll()
    {
        HeldLocally.Clear();
        GatSimulation? gat = World.gat;
        if (gat != null)
        {
            foreach (KeyValuePair<ushort, Matrix4x4> entry in _lastAppliedByLocalIndex)
            {
                Matrix4x4 affine = entry.Value;
                affine.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
                gat.SetInstanceMatrix(GatInterop.ToHandle(entry.Key), affine, movementEnabled: true, collisionEnabled: true, rigidPushEnabled: true);
            }
        }
        _lastAppliedByLocalIndex.Clear();
    }

    private void EnsureCapacity(int count)
    {
        if (_matrices.Length >= count)
        {
            return;
        }
        int capacity = Mathf.NextPowerOfTwo(count);
        _matrices = new Matrix4x4[capacity];
        _velocities = new Vector3[capacity];
        _written = new bool[capacity];
    }

    private void UploadContiguousRuns(ComputeBuffer matrixBuffer, ComputeBuffer velocityBuffer, int count)
    {
        int runStart = -1;
        for (int i = 0; i <= count; i++)
        {
            bool written = i < count && _written[i];
            if (written && runStart < 0)
            {
                runStart = i;
            }
            else if (!written && runStart >= 0)
            {
                int length = i - runStart;
                matrixBuffer.SetData(_matrices, runStart, runStart, length);
                velocityBuffer.SetData(_velocities, runStart, runStart, length);
                runStart = -1;
            }
        }
    }
}
