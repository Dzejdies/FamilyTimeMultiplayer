using System;
using System.Collections.Generic;
using UnityEngine;

namespace FamilyTimeMultiplayer.Avatars;

public readonly struct ProfilePoint
{
    public readonly float Height;
    public readonly float Radius;

    public ProfilePoint(float height, float radius)
    {
        Height = height;
        Radius = radius;
    }
}

public static class LatheMeshBuilder
{
    public static Mesh Build(string name, IReadOnlyList<ProfilePoint> profile, int segments, Vector3 radialScale)
    {
        if (profile.Count < 2)
        {
            throw new ArgumentException("A lathe profile needs at least two points.", nameof(profile));
        }
        if (segments < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(segments), "A lathe needs at least three segments.");
        }
        int ringSize = segments + 1;
        var vertices = new Vector3[profile.Count * ringSize];
        var normals = new Vector3[vertices.Length];
        var triangles = new List<int>((profile.Count - 1) * segments * 6);
        for (int ring = 0; ring < profile.Count; ring++)
        {
            ProfilePoint point = profile[ring];
            Vector2 slope = ProfileSlope(profile, ring);
            for (int segment = 0; segment <= segments; segment++)
            {
                float angle = segment / (float)segments * AvatarMath.FullCircleRadians;
                var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                int index = ring * ringSize + segment;
                vertices[index] = new Vector3(direction.x * point.Radius * radialScale.x, point.Height, direction.z * point.Radius * radialScale.z);
                Vector3 normal = new Vector3(direction.x * slope.y / radialScale.x, -slope.x, direction.z * slope.y / radialScale.z);
                normals[index] = normal.sqrMagnitude > 1e-8f ? normal.normalized : (point.Height > profile[0].Height ? Vector3.up : Vector3.down);
            }
        }
        for (int ring = 0; ring < profile.Count - 1; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int a = ring * ringSize + segment;
                int b = a + ringSize;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(a + 1);
                triangles.Add(a + 1);
                triangles.Add(b);
                triangles.Add(b + 1);
            }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(new List<Vector3>(vertices));
        mesh.SetNormals(new List<Vector3>(normals));
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh Ellipsoid(string name, Vector3 radii, int rings, int segments)
    {
        var profile = new List<ProfilePoint>(rings + 1);
        for (int i = 0; i <= rings; i++)
        {
            float theta = i / (float)rings * AvatarMath.Pi;
            profile.Add(new ProfilePoint(-Mathf.Cos(theta) * radii.y, Mathf.Sin(theta)));
        }
        return Build(name, profile, segments, new Vector3(radii.x, 1f, radii.z));
    }

    private static Vector2 ProfileSlope(IReadOnlyList<ProfilePoint> profile, int index)
    {
        ProfilePoint previous = profile[Mathf.Max(0, index - 1)];
        ProfilePoint next = profile[Mathf.Min(profile.Count - 1, index + 1)];
        var tangent = new Vector2(next.Radius - previous.Radius, next.Height - previous.Height);
        return tangent.sqrMagnitude > 1e-10f ? tangent.normalized : Vector2.up;
    }
}
