using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FamilyTimeMultiplayer.Core.Protocol;
using GAT;
using HarmonyLib;
using UnityEngine;
using GatSimulation = global::GAT.GAT;

namespace FamilyTimeMultiplayer.Game;

public static class GatInterop
{
    private const float FlagSet = 1f;
    private const float FlagClear = 0f;

    public static T RequireAlive<T>(T? unityObject, string missingMessage) where T : UnityEngine.Object
    {
        if (unityObject == null)
        {
            throw new InvalidOperationException(missingMessage);
        }
        return unityObject;
    }

    public static GatSimulation RequireGat() =>
        RequireAlive(World.gat, "World.gat is not available; the world is not loaded.");

    public static NetVector3 ToNet(Vector3 value) => new NetVector3(value.x, value.y, value.z);

    public static Vector3 ToUnity(NetVector3 value) => new Vector3(value.X, value.Y, value.Z);

    public static Quaternion ToUnity(NetQuaternion value) => new Quaternion(value.X, value.Y, value.Z, value.W);

    public static NetPose ToNetPose(Matrix4x4 gpuMatrix)
    {
        Matrix4x4 affine = gpuMatrix;
        affine.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
        Quaternion rotation = affine.rotation;
        Vector3 scale = affine.lossyScale;
        Vector3 position = affine.GetColumn(3);
        return new NetPose(ToNet(position), new NetQuaternion(rotation.x, rotation.y, rotation.z, rotation.w), ToNet(scale));
    }

    public static Matrix4x4 ToAffineMatrix(NetPose pose) =>
        Matrix4x4.TRS(ToUnity(pose.Position), ToUnity(pose.Rotation), ToUnity(pose.Scale));

    public static Matrix4x4 ToPuppetGpuMatrix(NetPose pose, ushort localStableIndex)
    {
        Matrix4x4 matrix = ToAffineMatrix(pose);
        matrix.m30 = FlagSet;
        matrix.m31 = FlagClear;
        matrix.m32 = FlagSet;
        matrix.m33 = localStableIndex;
        return matrix;
    }

    public static bool TryReadStableIndex(Matrix4x4 gpuMatrix, out ushort stableIndex)
    {
        float raw = gpuMatrix.m33;
        if (raw < 0f || raw >= ushort.MaxValue || raw != Mathf.Floor(raw))
        {
            stableIndex = ushort.MaxValue;
            return false;
        }
        stableIndex = (ushort)raw;
        return true;
    }

    public static Handle ToHandle(ushort stableIndex) => new Handle { stableIndex = stableIndex };

    public static int RequirePlayerTemplateIndex()
    {
        Singleplayer singleplayer = RequireAlive(World.singleplayer, "World.singleplayer is missing; cannot find the player template.");
        Template template = RequireAlive(
            Traverse.Create(singleplayer).Field<Template>("playerTemplate").Value,
            "Singleplayer.playerTemplate is not assigned.");
        int index = RequireGat().GetTemplateIndex(template);
        if (index < 0)
        {
            throw new InvalidOperationException($"Player template '{template.name}' is not registered with GAT.");
        }
        return index;
    }

    public static Faction RequirePlayerFaction()
    {
        PlayerController controller = RequireAlive(World.playerController, "World.playerController is missing.");
        return Traverse.Create(controller).Field<Faction>("faction").Value;
    }

    public static Delegate SubscribeLayoutChanged(GatSimulation gat, Action callback)
    {
        EventInfo layoutEvent = typeof(GatSimulation).GetEvent("onGATInstanceLayoutChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(GatSimulation).FullName, "onGATInstanceLayoutChanged");
        Type handlerType = layoutEvent.EventHandlerType ?? throw new InvalidOperationException("GAT layout event has no handler type.");
        ParameterExpression[] parameters = handlerType.GetMethod("Invoke")!.GetParameters()
            .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
        Delegate handler = Expression.Lambda(handlerType, Expression.Invoke(Expression.Constant(callback)), parameters).Compile();
        layoutEvent.AddEventHandler(gat, handler);
        return handler;
    }

    public static void UnsubscribeLayoutChanged(GatSimulation gat, Delegate handler)
    {
        EventInfo layoutEvent = typeof(GatSimulation).GetEvent("onGATInstanceLayoutChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(GatSimulation).FullName, "onGATInstanceLayoutChanged");
        layoutEvent.RemoveEventHandler(gat, handler);
    }

    public static GATInstanceInfo CreateInstanceInfo(uint value)
    {
        ConstructorInfo[] constructors = typeof(GATInstanceInfo).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        ConstructorInfo? packed = constructors.FirstOrDefault(constructor => constructor.GetParameters().Length == 1 && constructor.GetParameters()[0].ParameterType == typeof(uint));
        if (packed != null) return (GATInstanceInfo)packed.Invoke(new object[] { value });
        ConstructorInfo expanded = constructors.FirstOrDefault(constructor => constructor.GetParameters().Length == 8)
            ?? throw new MissingMethodException(nameof(GATInstanceInfo), ".ctor");
        Vector2 movement = DecodeMovement((int)((value >> MovementShift) & MovementMask));
        object[] arguments =
        {
            (byte)(value & FlowFieldMask), (Faction)((value >> FactionShift) & FactionMask), movement,
            (byte)((value >> SpeedShift) & SpeedMask), (byte)((value >> HungerShift) & HungerMask),
            (byte)((value >> HealthShift) & HealthMask), (byte)((value >> IconShift) & IconMask),
            (value & ContinueFollowMask) != 0,
        };
        return (GATInstanceInfo)expanded.Invoke(arguments);
    }

    public static bool IsValidPose(Matrix4x4 matrix)
    {
        Vector3 position = matrix.GetColumn(3);
        Vector3 scale = matrix.lossyScale;
        return IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z) &&
            IsFinite(scale.x) && IsFinite(scale.y) && IsFinite(scale.z) &&
            Math.Abs(scale.x) > MinimumTransformScale && Math.Abs(scale.y) > MinimumTransformScale && Math.Abs(scale.z) > MinimumTransformScale;
    }

    private static Vector2 DecodeMovement(int index) => index switch
    {
        1 => Vector2.right,
        2 => Vector2.left,
        5 => Vector2.up,
        6 => Vector2.down,
        7 => new Vector2(1f, 1f),
        8 => new Vector2(1f, -1f),
        9 => new Vector2(-1f, 1f),
        10 => new Vector2(-1f, -1f),
        _ => Vector2.zero,
    };

    private const int MovementShift = 8;
    private const int FactionShift = 16;
    private const int SpeedShift = 12;
    private const int HungerShift = 19;
    private const int HealthShift = 20;
    private const int IconShift = 24;
    private const uint MovementMask = 0xF;
    private const uint FactionMask = 0x7;
    private const uint SpeedMask = 0x3;
    private const uint HungerMask = 0x1;
    private const uint HealthMask = 0xF;
    private const uint IconMask = 0xF;
    private const uint FlowFieldMask = 0xFF;
    private const uint ContinueFollowMask = 0x80000000;
    private const float MinimumTransformScale = 0.00001f;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public static GATInstanceInfo PlayerControlledInfo(Faction faction) =>
        new GATInstanceInfo(byte.MaxValue, faction, Vector2.zero, 0, 0, 15, 0);
}
