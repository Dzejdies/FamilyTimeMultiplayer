using System;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Protocol;

public readonly struct NetVector3 : IEquatable<NetVector3>
{
    public static readonly NetVector3 Zero = new NetVector3(0f, 0f, 0f);
    public static readonly NetVector3 One = new NetVector3(1f, 1f, 1f);

    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    public NetVector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static NetVector3 Lerp(NetVector3 from, NetVector3 to, float t) =>
        new NetVector3(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, from.Z + (to.Z - from.Z) * t);

    public static NetVector3 operator -(NetVector3 a, NetVector3 b) => new NetVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static NetVector3 operator *(NetVector3 a, float scalar) => new NetVector3(a.X * scalar, a.Y * scalar, a.Z * scalar);

    public float Magnitude => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    public void Write(BinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Z);
    }

    public static NetVector3 Read(BinaryReader reader) =>
        new NetVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public bool Equals(NetVector3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is NetVector3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public override string ToString() => $"({X:0.00}, {Y:0.00}, {Z:0.00})";
}

public readonly struct NetQuaternion : IEquatable<NetQuaternion>
{
    public static readonly NetQuaternion Identity = new NetQuaternion(0f, 0f, 0f, 1f);

    public readonly float X;
    public readonly float Y;
    public readonly float Z;
    public readonly float W;

    public NetQuaternion(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public static NetQuaternion Nlerp(NetQuaternion from, NetQuaternion to, float t)
    {
        float dot = from.X * to.X + from.Y * to.Y + from.Z * to.Z + from.W * to.W;
        float sign = dot < 0f ? -1f : 1f;
        float x = from.X + (to.X * sign - from.X) * t;
        float y = from.Y + (to.Y * sign - from.Y) * t;
        float z = from.Z + (to.Z * sign - from.Z) * t;
        float w = from.W + (to.W * sign - from.W) * t;
        float length = MathF.Sqrt(x * x + y * y + z * z + w * w);
        if (length < 1e-6f)
        {
            return to;
        }
        return new NetQuaternion(x / length, y / length, z / length, w / length);
    }

    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) && float.IsFinite(W);

    public void Write(BinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Z);
        writer.Write(W);
    }

    public static NetQuaternion Read(BinaryReader reader) =>
        new NetQuaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public bool Equals(NetQuaternion other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    public override bool Equals(object? obj) => obj is NetQuaternion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
}

public readonly struct NetPose : IEquatable<NetPose>
{
    public readonly NetVector3 Position;
    public readonly NetQuaternion Rotation;
    public readonly NetVector3 Scale;

    public NetPose(NetVector3 position, NetQuaternion rotation, NetVector3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public static NetPose Interpolate(NetPose from, NetPose to, float t) =>
        new NetPose(
            NetVector3.Lerp(from.Position, to.Position, t),
            NetQuaternion.Nlerp(from.Rotation, to.Rotation, t),
            NetVector3.Lerp(from.Scale, to.Scale, t));

    public bool IsFinite => Position.IsFinite && Rotation.IsFinite && Scale.IsFinite;

    public void Write(BinaryWriter writer)
    {
        Position.Write(writer);
        Rotation.Write(writer);
        Scale.Write(writer);
    }

    public static NetPose Read(BinaryReader reader) =>
        new NetPose(NetVector3.Read(reader), NetQuaternion.Read(reader), NetVector3.Read(reader));

    public bool Equals(NetPose other) =>
        Position.Equals(other.Position) && Rotation.Equals(other.Rotation) && Scale.Equals(other.Scale);

    public override bool Equals(object? obj) => obj is NetPose other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, Rotation, Scale);
}
