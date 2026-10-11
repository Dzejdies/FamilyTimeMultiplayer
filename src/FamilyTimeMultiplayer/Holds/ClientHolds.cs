using System;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.UI;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Holds;

public interface IClientHoldSink
{
    void SendHold(HoldMessage message);

    void SendMamaToggle(ushort agent);

    bool TryGetRigidNetId(RigidTransform rigid, out int netId);

    bool TryGetHostAgent(ushort localStableIndex, out ushort hostStableIndex);

    void SetAgentLocallyHeld(ushort localStableIndex, bool held);

    void SetRigidLocallyHeld(int netId, bool held);
}

public static class ClientHolds
{
    private const float SettledSpeed = 0.15f;
    private const double SettledSeconds = 0.4;
    private const double MaxSettleSeconds = 6.0;

    private sealed class LocalHold
    {
        public LocalHold(HoldTarget target, RigidTransform rigid)
        {
            Target = target;
            Rigid = rigid;
        }

        public HoldTarget Target { get; }
        public RigidTransform Rigid { get; }
        public double DroppedAt { get; set; } = -1;
        public double SlowSince { get; set; } = -1;
    }

    private sealed class AgentHold
    {
        public AgentHold(ushort local, HoldTarget target)
        {
            Local = local;
            Target = target;
        }

        public ushort Local { get; }
        public HoldTarget Target { get; }
        public Matrix4x4 Pose { get; set; } = Matrix4x4.identity;
        public Vector3 Velocity { get; set; }
        public bool Dropped { get; set; }
    }

    private static readonly Dictionary<RigidTransform, LocalHold> Holds = new Dictionary<RigidTransform, LocalHold>();
    private static readonly Dictionary<ushort, AgentHold> AgentHolds = new Dictionary<ushort, AgentHold>();
    private static IClientHoldSink? _sink;
    private static double _nextUpdate;

    public static bool IsActive => _sink != null;

    public static double UpdateInterval { get; set; } = 1.0 / 30.0;

    public static void Attach(IClientHoldSink sink)
    {
        _sink = sink;
        Holds.Clear();
        AgentHolds.Clear();
    }

    public static void Detach()
    {
        foreach (LocalHold hold in Holds.Values)
        {
            if (hold.Target.Kind == HoldTargetKind.Rigid)
            {
                _sink?.SetRigidLocallyHeld(hold.Target.Id, false);
            }
        }
        Holds.Clear();
        foreach (AgentHold hold in AgentHolds.Values)
        {
            _sink?.SetAgentLocallyHeld(hold.Local, false);
        }
        AgentHolds.Clear();
        _sink = null;
    }

    public static bool IsHeldAgent(Handle handle) => !handle.isNull && AgentHolds.ContainsKey(handle.stableIndex);

    public static void OnAgentGrabbed(Handle handle)
    {
        IClientHoldSink sink = _sink ?? throw new InvalidOperationException("No client session for holds.");
        if (handle.isNull || MamaLookup.All().Any(m => m.FakeGATHandle == handle))
        {
            return;
        }
        if (AgentHolds.TryGetValue(handle.stableIndex, out AgentHold existing))
        {
            existing.Dropped = false;
            return;
        }
        if (!sink.TryGetHostAgent(handle.stableIndex, out ushort host))
        {
            return;
        }
        var hold = new AgentHold(handle.stableIndex, HoldTarget.Agent(host));
        Matrix4x4? matrix = World.gat?.GetInstanceMatrixImmediate(handle);
        if (matrix.HasValue)
        {
            hold.Pose = matrix.Value;
        }
        AgentHolds.Add(handle.stableIndex, hold);
        sink.SetAgentLocallyHeld(handle.stableIndex, true);
        sink.SendHold(CaptureAgent(hold, HoldPhase.Begin));
    }

    public static void OnAgentMatrix(Handle handle, Matrix4x4 matrix)
    {
        if (AgentHolds.TryGetValue(handle.stableIndex, out AgentHold hold))
        {
            hold.Pose = matrix;
        }
    }

    public static void OnAgentVelocity(Handle handle, Vector3 velocity)
    {
        if (AgentHolds.TryGetValue(handle.stableIndex, out AgentHold hold))
        {
            hold.Velocity = velocity;
        }
    }

    public static void OnAgentDropped(Handle handle)
    {
        if (AgentHolds.TryGetValue(handle.stableIndex, out AgentHold hold))
        {
            hold.Dropped = true;
        }
    }

    private static void TickAgents(bool sendUpdates)
    {
        foreach (AgentHold hold in AgentHolds.Values.ToArray())
        {
            if (hold.Dropped)
            {
                _sink!.SendHold(CaptureAgent(hold, HoldPhase.End));
                AgentHolds.Remove(hold.Local);
                _sink.SetAgentLocallyHeld(hold.Local, false);
            }
            else if (sendUpdates)
            {
                _sink!.SendHold(CaptureAgent(hold, HoldPhase.Update));
            }
        }
    }

    private static HoldMessage CaptureAgent(AgentHold hold, HoldPhase phase)
    {
        Vector3 position = hold.Pose.GetColumn(3);
        Quaternion rotation = GatInterop.IsValidPose(hold.Pose) ? hold.Pose.rotation : Quaternion.identity;
        return new HoldMessage(hold.Target, phase, true, ToNet(position),
            new NetQuaternion(rotation.x, rotation.y, rotation.z, rotation.w), ToNet(hold.Velocity));
    }

    public static void OnGrabbed(RigidTransform rigid)
    {
        IClientHoldSink sink = _sink ?? throw new InvalidOperationException("No client session for holds.");
        if (Holds.TryGetValue(rigid, out LocalHold existing))
        {
            if (existing.DroppedAt >= 0) sink.SendHold(Capture(existing.Target, rigid, HoldPhase.Begin));
            existing.DroppedAt = -1;
            existing.SlowSince = -1;
            return;
        }
        HoldTarget target;
        WolfMama? mama = MamaLookup.TryFind(rigid, out WolfMama foundMama) ? foundMama : null;
        if (mama != null)
        {
            if (mama.FakeGATHandle.isNull)
            {
                return;
            }
            target = HoldTarget.Mama(mama.FakeGATHandle.stableIndex);
        }
        else if (sink.TryGetRigidNetId(rigid, out int netId))
        {
            target = HoldTarget.Rigid(netId);
            sink.SetRigidLocallyHeld(netId, true);
            rigid.SetFrozen(rigid.IsCarried);
        }
        else
        {
            return;
        }
        Holds.Add(rigid, new LocalHold(target, rigid));
        sink.SendHold(Capture(target, rigid, HoldPhase.Begin));
    }

    public static void OnDropped(RigidTransform rigid, double now)
    {
        if (Holds.TryGetValue(rigid, out LocalHold hold))
        {
            hold.DroppedAt = now;
            _sink?.SendHold(Capture(hold.Target, hold.Rigid, HoldPhase.Settle));
        }
    }

    public static void Tick(double now)
    {
        if (_sink == null || Holds.Count + AgentHolds.Count == 0)
        {
            return;
        }
        bool sendUpdates = now >= _nextUpdate;
        if (sendUpdates)
        {
            _nextUpdate = now + UpdateInterval;
        }
        TickAgents(sendUpdates);
        foreach (LocalHold hold in Holds.Values.ToArray())
        {
            if (hold.Rigid == null)
            {
                Forget(hold);
                continue;
            }
            if (hold.DroppedAt >= 0 && IsSettled(hold, now))
            {
                _sink.SendHold(Capture(hold.Target, hold.Rigid, HoldPhase.End));
                Forget(hold);
                continue;
            }
            if (sendUpdates)
            {
                _sink.SendHold(Capture(hold.Target, hold.Rigid, HoldPhase.Update));
            }
        }
    }

    public static void ApplyResult(HoldResultMessage result)
    {
        if (result.Granted)
        {
            return;
        }
        AgentHold? agent = AgentHolds.Values.FirstOrDefault(h => h.Target.Equals(result.Target));
        if (agent != null)
        {
            AgentHolds.Remove(agent.Local);
            _sink?.SetAgentLocallyHeld(agent.Local, false);
            HandActions.DropAgent(Game.GatInterop.ToHandle(agent.Local));
            PlayerNotice.Show($"Can't take them: {result.Reason}");
            return;
        }
        LocalHold? hold = Holds.Values.FirstOrDefault(h => h.Target.Equals(result.Target));
        if (hold == null)
        {
            return;
        }
        Forget(hold);
        if (hold.Rigid != null && HandActions.IsHolding(hold.Rigid))
        {
            HandActions.Drop(hold.Rigid);
        }
        PlayerNotice.Show($"Can't take it: {result.Reason}");
    }

    public static void ToggleMama(WolfMama mama)
    {
        IClientHoldSink sink = _sink ?? throw new InvalidOperationException("No client session for mama toggles.");
        if (!mama.FakeGATHandle.isNull)
        {
            sink.SendMamaToggle(mama.FakeGATHandle.stableIndex);
        }
    }

    public static void ApplyMamaState(MamaStateMessage state)
    {
        foreach (MamaStateEntry entry in state.Mamas)
        {
            if (MamaLookup.TryFind(entry.Agent, out WolfMama mama) && mama.AllowFollow != entry.AllowFollow)
            {
                HarmonyLib.Traverse.Create(mama).Field<bool>("allowFollow").Value = entry.AllowFollow;
                mama.RefreshFollowScreenIcon();
            }
        }
    }

    private static bool IsSettled(LocalHold hold, double now)
    {
        if (hold.Target.Kind == HoldTargetKind.Mama || now - hold.DroppedAt > MaxSettleSeconds)
        {
            return true;
        }
        if (hold.Rigid.LinearVelocity.magnitude > SettledSpeed)
        {
            hold.SlowSince = -1;
            return false;
        }
        if (hold.SlowSince < 0)
        {
            hold.SlowSince = now;
        }
        return now - hold.SlowSince >= SettledSeconds;
    }

    private static void Forget(LocalHold hold)
    {
        Holds.Remove(hold.Rigid);
        if (hold.Target.Kind == HoldTargetKind.Rigid)
        {
            _sink?.SetRigidLocallyHeld(hold.Target.Id, false);
        }
    }

    private static HoldMessage Capture(HoldTarget target, RigidTransform rigid, HoldPhase phase)
    {
        Vector3 position = rigid.position;
        Quaternion rotation = rigid.rotation;
        return new HoldMessage(target, phase, rigid.IsCarried,
            new NetVector3(position.x, position.y, position.z),
            new NetQuaternion(rotation.x, rotation.y, rotation.z, rotation.w),
            ToNet(rigid.LinearVelocity));
    }

    private static NetVector3 ToNet(Vector3 v) => new NetVector3(v.x, v.y, v.z);
}
