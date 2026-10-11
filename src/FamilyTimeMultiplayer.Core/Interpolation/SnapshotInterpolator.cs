using System;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Protocol;

namespace FamilyTimeMultiplayer.Core.Interpolation;

public readonly struct InterpolatedEntry<TKey>
{
    public readonly TKey Key;
    public readonly NetPose Pose;
    public readonly NetVector3 Velocity;

    public InterpolatedEntry(TKey key, NetPose pose, NetVector3 velocity)
    {
        Key = key;
        Pose = pose;
        Velocity = velocity;
    }
}

public sealed class SnapshotInterpolator<TKey> where TKey : notnull
{
    private readonly struct Snapshot
    {
        public readonly double HostTime;
        public readonly Dictionary<TKey, NetPose> Poses;

        public Snapshot(double hostTime, Dictionary<TKey, NetPose> poses)
        {
            HostTime = hostTime;
            Poses = poses;
        }
    }

    private const int MaxBufferedSnapshots = 32;

    private readonly List<Snapshot> _snapshots = new List<Snapshot>();
    private readonly double _interpolationDelaySeconds;
    private double _hostClockOffset;
    private bool _hasClockOffset;

    public SnapshotInterpolator(double interpolationDelaySeconds)
    {
        if (interpolationDelaySeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interpolationDelaySeconds), "Interpolation delay cannot be negative.");
        }
        _interpolationDelaySeconds = interpolationDelaySeconds;
    }

    public int BufferedSnapshotCount => _snapshots.Count;

    public void Clear()
    {
        _snapshots.Clear();
        _hasClockOffset = false;
    }

    public void Push(double localTimeSeconds, double hostTimeSeconds, IEnumerable<KeyValuePair<TKey, NetPose>> poses)
    {
        if (_snapshots.Count > 0 && hostTimeSeconds <= _snapshots[_snapshots.Count - 1].HostTime)
        {
            return;
        }
        double offset = hostTimeSeconds - localTimeSeconds;
        if (!_hasClockOffset || offset > _hostClockOffset)
        {
            _hostClockOffset = offset;
            _hasClockOffset = true;
        }
        var byKey = new Dictionary<TKey, NetPose>();
        foreach (KeyValuePair<TKey, NetPose> pose in poses)
        {
            byKey[pose.Key] = pose.Value;
        }
        _snapshots.Add(new Snapshot(hostTimeSeconds, byKey));
        if (_snapshots.Count > MaxBufferedSnapshots)
        {
            _snapshots.RemoveAt(0);
        }
    }

    public void Sample(double localTimeSeconds, List<InterpolatedEntry<TKey>> output)
    {
        output.Clear();
        if (_snapshots.Count == 0)
        {
            return;
        }
        double renderTime = localTimeSeconds + _hostClockOffset - _interpolationDelaySeconds;
        while (_snapshots.Count > 2 && _snapshots[1].HostTime <= renderTime)
        {
            _snapshots.RemoveAt(0);
        }
        Snapshot older = _snapshots[0];
        if (_snapshots.Count == 1 || renderTime <= older.HostTime)
        {
            EmitStatic(older, output);
            return;
        }
        Snapshot newer = _snapshots[1];
        double span = newer.HostTime - older.HostTime;
        float t = (float)Math.Min(1.0, Math.Max(0.0, (renderTime - older.HostTime) / span));
        float inverseSpan = (float)(1.0 / span);
        foreach (KeyValuePair<TKey, NetPose> entry in newer.Poses)
        {
            if (older.Poses.TryGetValue(entry.Key, out NetPose from))
            {
                NetVector3 velocity = (entry.Value.Position - from.Position) * inverseSpan;
                output.Add(new InterpolatedEntry<TKey>(entry.Key, NetPose.Interpolate(from, entry.Value, t), velocity));
            }
            else
            {
                output.Add(new InterpolatedEntry<TKey>(entry.Key, entry.Value, NetVector3.Zero));
            }
        }
    }

    private static void EmitStatic(Snapshot snapshot, List<InterpolatedEntry<TKey>> output)
    {
        foreach (KeyValuePair<TKey, NetPose> entry in snapshot.Poses)
        {
            output.Add(new InterpolatedEntry<TKey>(entry.Key, entry.Value, NetVector3.Zero));
        }
    }
}
