using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Holds;

public sealed class HostHoldRegistry : IDisposable
{
    private sealed class Hold
    {
        public Hold(int playerId, string playerName, RigidTransform? rigid, WolfMama? mama, bool wasFrozen, Handle agent)
        {
            Agent = agent;
            PlayerId = playerId;
            PlayerName = playerName;
            Rigid = rigid;
            Mama = mama;
            WasFrozen = wasFrozen;
        }

        public int PlayerId { get; }
        public string PlayerName { get; }
        public RigidTransform? Rigid { get; }
        public Handle Agent { get; }
        public WolfMama? Mama { get; }
        public bool WasFrozen { get; }
        public Core.Safety.MotionBudget Motion { get; set; } = null!;
        public double ReleasedAt { get; set; } = -1;
        public NetVector3 ReleasedOrigin { get; set; }
    }

    private readonly ManualLogSource _log;
    private readonly HostRigidRegistry _rigids;
    private readonly Dictionary<HoldTarget, Hold> _holds = new Dictionary<HoldTarget, Hold>();

    public HostHoldRegistry(ManualLogSource log, HostRigidRegistry rigids)
    {
        if (Current != null)
        {
            throw new InvalidOperationException("A HostHoldRegistry is already active.");
        }
        _log = log;
        _rigids = rigids;
        Current = this;
    }

    public static HostHoldRegistry? Current { get; private set; }

    private static readonly System.Reflection.MethodInfo CollectCarriedHandles =
        HarmonyLib.AccessTools.Method(typeof(HandService), "CollectAllCarriedHandles")
        ?? throw new MissingMethodException(nameof(HandService), "CollectAllCarriedHandles");

    public bool IsHeldBy(int playerId, RigidTransform rigid) => _holds.Values.Any(hold => hold.PlayerId == playerId && hold.Rigid == rigid);

    public void Tick(double now)
    {
        foreach (var entry in _holds.Where(entry => IsGone(entry.Value) || entry.Value.ReleasedAt >= 0 && now - entry.Value.ReleasedAt > 8).ToArray())
            Release(entry.Key, entry.Value, Vector3.zero);
    }

    public string? HolderOf(RigidTransform rigid) =>
        _holds.Values.FirstOrDefault(h => h.Rigid == rigid)?.PlayerName;

    public string? HolderOf(Handle agent) =>
        _holds.Values.FirstOrDefault(h => !h.Agent.isNull && h.Agent == agent)?.PlayerName;

    public string? TryBegin(int playerId, string playerName, HoldMessage message)
    {
        ValidateShape(message);
        HostPlayerAuthority.RequireNear(playerId, GatInterop.ToUnity(message.Position), message.CarriedInHand ? 10 : HostPlayerAuthority.HandReach + 3);
        if (_holds.Values.Count(hold => hold.PlayerId == playerId) >= 8) return "too many held targets";
        if (message.Target.Kind == HoldTargetKind.Agent)
        {
            return TryBeginAgent(playerId, playerName, message);
        }
        if (!TryResolve(message.Target, out RigidTransform rigid, out WolfMama? mama))
        {
            return "that is gone on the host";
        }
        if (_holds.TryGetValue(message.Target, out Hold existing))
        {
            if (existing.PlayerId != playerId) return $"{existing.PlayerName} is holding it";
            HostPlayerAuthority.RequireRigidNear(playerId, rigid, HostPlayerAuthority.HandReach);
            HostPlayerAuthority.RequireNear(playerId, GatInterop.ToUnity(message.Position), message.CarriedInHand ? 10 : HostPlayerAuthority.HandReach + 3);
            if (!existing.Motion.TryMove(message.Position, FamilyTimeMultiplayer.Game.GameClock.Now)) throw new System.IO.InvalidDataException("Regrab displacement exceeds its motion budget.");
            existing.ReleasedAt = -1;
            ApplyPose(existing, message);
            return null;
        }
        HandService? hostHand = PlayerController.hand;
        if (hostHand != null && hostHand.CollectAllCarriedRigids().Contains(rigid))
        {
            return "the host is holding it";
        }
        HostPlayerAuthority.RequireRigidNear(playerId, rigid, HostPlayerAuthority.HandReach);
        if (mama != null) HostPlayerAuthority.RequireOwnedAgent(playerId, mama.FakeGATHandle.stableIndex);
        var hold = new Hold(playerId, playerName, rigid, mama, rigid.IsFrozen, Handle.Null);
        hold.Motion = new Core.Safety.MotionBudget(GatInterop.ToNet(rigid.position), FamilyTimeMultiplayer.Game.GameClock.Now, 100, HostPlayerAuthority.HandReach + 3);
        if (!hold.Motion.TryMove(message.Position, FamilyTimeMultiplayer.Game.GameClock.Now)) return "hold begin displacement is too large";
        _holds.Add(message.Target, hold);
        rigid.SetFrozen(true);
        rigid.SetCarriedState(carried: true);
        if (mama != null)
        {
            mama.OnPlayerGrabbed();
            World.gat?.SetInstanceFlag(mama.FakeGATHandle, global::GAT.GAT.InstanceFlags.Collision, enable: false);
        }
        ApplyPose(hold, message);
        _log.LogInfo($"[Hold] '{playerName}' picked up {Describe(message.Target, rigid)}");
        return null;
    }

    public void Update(int playerId, HoldMessage message)
    {
        if (!_holds.TryGetValue(message.Target, out Hold hold) || hold.PlayerId != playerId)
        {
            return;
        }
        if (IsGone(hold))
        {
            _holds.Remove(message.Target);
            _log.LogInfo($"[Hold] {message.Target} held by '{hold.PlayerName}' no longer exists (used up or deleted); hold dropped");
            return;
        }
        ValidateUpdate(playerId, hold, message);
        ApplyPose(hold, message);
    }

    public void Settle(int playerId, HoldMessage message)
    {
        if (!_holds.TryGetValue(message.Target, out Hold hold) || hold.PlayerId != playerId || hold.ReleasedAt >= 0) throw new System.IO.InvalidDataException("Invalid hold release transition.");
        ValidateUpdate(playerId, hold, message);
        hold.ReleasedAt = FamilyTimeMultiplayer.Game.GameClock.Now;
        hold.ReleasedOrigin = message.Position;
        ApplyPose(hold, message);
    }

    private static void ValidateShape(HoldMessage message)
    {
        if (!Core.Safety.MotionBudget.ValidHold(message)) throw new System.IO.InvalidDataException("Invalid held-object transform or velocity.");
    }
    private static void ValidateUpdate(int playerId, Hold hold, HoldMessage message)
    {
        ValidateShape(message);
        if (hold.ReleasedAt < 0) HostPlayerAuthority.RequireNear(playerId, GatInterop.ToUnity(message.Position), message.CarriedInHand ? 10 : HostPlayerAuthority.HandReach + 3);
        else if (message.CarriedInHand || FamilyTimeMultiplayer.Game.GameClock.Now - hold.ReleasedAt > 8) throw new System.IO.InvalidDataException("Released hold authority expired.");
        if (hold.ReleasedAt >= 0 && hold.Rigid != null && hold.Rigid.HandCarryInfo != null &&
            !Core.Safety.ReleaseEnvelope.Accepts(hold.ReleasedOrigin, message.Position, message.Velocity, FamilyTimeMultiplayer.Game.GameClock.Now - hold.ReleasedAt, ThrowSpeed()))
            throw new System.IO.InvalidDataException("Released object exceeds its native throw and gravity envelope.");
        if (hold.ReleasedAt < 0 && message.Phase == HoldPhase.End && (!hold.Agent.isNull || hold.Rigid?.HandCarryInfo != null) && message.Velocity.Magnitude > ThrowSpeed() + 5)
            throw new System.IO.InvalidDataException("Release velocity exceeds the native throw limit.");
        if (!hold.Motion.TryMove(message.Position, FamilyTimeMultiplayer.Game.GameClock.Now)) throw new System.IO.InvalidDataException("Held-object movement exceeds its motion budget.");
    }

    private static float ThrowSpeed() => PlayerController.hand == null ? 3 : HarmonyLib.Traverse.Create(PlayerController.hand).Field<float>("maxThrowSpeed").Value;

    private static bool IsGone(Hold hold) =>
        hold.Agent.isNull ? hold.Rigid == null : World.gat == null || !World.gat.IsHandleValid(hold.Agent);

    public void End(int playerId, HoldMessage message)
    {
        if (!_holds.TryGetValue(message.Target, out Hold hold) || hold.PlayerId != playerId)
        {
            return;
        }
        if (IsGone(hold))
        {
            _holds.Remove(message.Target);
            return;
        }
        ValidateUpdate(playerId, hold, message);
        ApplyPose(hold, message);
        Release(message.Target, hold, GatInterop.ToUnity(message.Velocity));
        _log.LogInfo($"[Hold] '{hold.PlayerName}' let go of {(hold.Rigid != null ? Describe(message.Target, hold.Rigid) : $"child {hold.Agent}")} at {GatInterop.ToUnity(message.Position)}");
    }

    public void ReleaseAll(int playerId)
    {
        foreach (KeyValuePair<HoldTarget, Hold> entry in _holds.Where(e => e.Value.PlayerId == playerId).ToArray())
        {
            Release(entry.Key, entry.Value, Vector3.zero);
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<HoldTarget, Hold> entry in _holds.ToArray())
        {
            Release(entry.Key, entry.Value, Vector3.zero);
        }
        if (Current == this)
        {
            Current = null;
        }
    }

    private string? TryBeginAgent(int playerId, string playerName, HoldMessage message)
    {
        if (message.Target.Id < 0 || message.Target.Id >= ushort.MaxValue || World.gat == null)
        {
            return "that is gone on the host";
        }
        Handle agent = GatInterop.ToHandle((ushort)message.Target.Id);
        if (!World.gat.IsHandleValid(agent))
        {
            return "that is gone on the host";
        }
        if (_holds.TryGetValue(message.Target, out Hold existing))
        {
            return existing.PlayerId == playerId ? null : $"{existing.PlayerName} is holding them";
        }
        HandService? hostHand = PlayerController.hand;
        if (hostHand != null && ((List<Handle>)CollectCarriedHandles.Invoke(hostHand, null)).Contains(agent))
        {
            return "the host is holding them";
        }
        if (HostPlayerAuthority.IsAvatar(agent) || World.gat.GetTemplateFromHandle(agent)?.carryProxy == null) return "that agent cannot be carried";
        HostPlayerAuthority.RequireOwnedAgent(playerId, agent.stableIndex);
        Vector3 origin = HostPlayerAuthority.AgentPosition(agent.stableIndex);
        HostPlayerAuthority.RequireNear(playerId, origin, HostPlayerAuthority.HandReach);
        var hold = new Hold(playerId, playerName, null, null, false, agent);
        hold.Motion = new Core.Safety.MotionBudget(GatInterop.ToNet(origin), FamilyTimeMultiplayer.Game.GameClock.Now, 100, 5);
        if (!hold.Motion.TryMove(message.Position, FamilyTimeMultiplayer.Game.GameClock.Now)) return "hold begin displacement is too large";
        _holds.Add(message.Target, hold);
        HandActions.ApplyHostGrabState(agent);
        Commands.HostCommandHub.Current?.NotifyAgentGrabbed(agent);
        ApplyPose(hold, message);
        _log.LogInfo($"[Hold] '{playerName}' picked up child {agent}");
        return null;
    }

    private void Release(HoldTarget target, Hold hold, Vector3 velocity)
    {
        _holds.Remove(target);
        if (!hold.Agent.isNull)
        {
            ReleaseAgent(hold, velocity);
            return;
        }
        if (hold.Rigid == null)
        {
            return;
        }
        hold.Rigid.SetCarriedState(carried: false);
        hold.Rigid.SetFrozen(hold.WasFrozen);
        hold.Rigid.SetLinearVelocity(velocity);
        if (hold.Mama != null && World.gat != null && World.gat.IsHandleValid(hold.Mama.FakeGATHandle))
        {
            Handle proxy = hold.Mama.FakeGATHandle;
            World.fakeGATManager?.ClearImmediatePose(proxy);
            World.gat.SetInstanceFlag(proxy, global::GAT.GAT.InstanceFlags.Collision, enable: true);
            World.gat.SetInstanceVelocity(proxy, velocity);
            hold.Mama.OnPlayerDropped();
        }
    }

    private static void ReleaseAgent(Hold hold, Vector3 velocity)
    {
        global::GAT.GAT? gat = World.gat;
        if (gat == null || !gat.IsHandleValid(hold.Agent))
        {
            return;
        }
        Matrix4x4? matrix = gat.GetInstanceMatrixImmediate(hold.Agent);
        gat.SetInstanceMatrix(hold.Agent, matrix ?? Matrix4x4.identity, movementEnabled: true, collisionEnabled: true, rigidPushEnabled: true);
        gat.SetInstanceVelocity(hold.Agent, velocity);
        World.fakeGATManager?.ClearImmediatePose(hold.Agent);
        int template = gat.GetTemplateIndexFromPhysical(gat.ResolveHandle(hold.Agent));
        bool thrown = velocity.sqrMagnitude > 0.25f;
        if (template >= 0 && World.gatAnimationManager != null)
        {
            World.gatAnimationManager.SetInstanceMotionSets(new[] { hold.Agent }, 0, template, !thrown);
            if (thrown)
            {
                World.gatAnimationManager.PlayAnimations(new[] { hold.Agent }, AnimationEnum.THROW, null, filterOnlyWhenIdling: false, null, gat.GetTemplate(template));
            }
        }
    }

    private static void ApplyPose(Hold hold, HoldMessage message)
    {
        Vector3 position = GatInterop.ToUnity(message.Position);
        Quaternion rotation = GatInterop.ToUnity(message.Rotation);
        if (!hold.Agent.isNull)
        {
            if (World.gat != null && World.gat.IsHandleValid(hold.Agent))
            {
                World.gat.SetInstanceMatrix(hold.Agent, Matrix4x4.TRS(position, rotation, Vector3.one), movementEnabled: false, collisionEnabled: false, rigidPushEnabled: false);
                World.gat.SetInstanceVelocity(hold.Agent, Vector3.zero);
            }
            return;
        }
        RigidTransform rigid = hold.Rigid!;
        rigid.SetPosition(position);
        rigid.SetRotation(rotation);
        if (hold.Mama != null && World.gat != null && World.gat.IsHandleValid(hold.Mama.FakeGATHandle))
        {
            Handle proxy = hold.Mama.FakeGATHandle;
            World.gat.SetInstanceMatrix(proxy, Matrix4x4.TRS(position, rotation, Vector3.one));
            World.gat.SetInstanceVelocity(proxy, Vector3.zero);
            World.fakeGATManager?.SetImmediatePose(proxy, position, rotation * Vector3.forward, rotation * Vector3.up);
        }
    }

    private bool TryResolve(HoldTarget target, out RigidTransform rigid, out WolfMama? mama)
    {
        mama = null;
        rigid = null!;
        if (target.Kind == HoldTargetKind.Rigid)
        {
            return _rigids.TryResolve(target.Id, out rigid);
        }
        if (target.Id < 0 || target.Id > ushort.MaxValue || !MamaLookup.TryFind((ushort)target.Id, out WolfMama found))
        {
            return false;
        }
        mama = found;
        rigid = RigidTransformApi.FromMama(found)!;
        return RigidTransformApi.IsAlive(rigid);
    }

    private static string Describe(HoldTarget target, RigidTransform rigid) =>
        target.Kind == HoldTargetKind.Mama ? RigidTransformApi.Name(rigid) : $"{RigidTransformApi.Name(rigid)} (item {target.Id})";
}
