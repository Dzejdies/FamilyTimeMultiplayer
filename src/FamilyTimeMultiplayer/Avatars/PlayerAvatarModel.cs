using System;
using System.Collections.Generic;
using UnityEngine;

namespace FamilyTimeMultiplayer.Avatars;

public sealed class PlayerAvatarRig
{
    public PlayerAvatarRig(GameObject root, Transform body, Transform leftLeg, Transform rightLeg, Transform leftArm, Transform rightArm, Transform torso)
    {
        Root = root;
        Body = body;
        LeftLeg = leftLeg;
        RightLeg = rightLeg;
        LeftArm = leftArm;
        RightArm = rightArm;
        Torso = torso;
    }

    public GameObject Root { get; }
    public Transform Body { get; }
    public Transform LeftLeg { get; }
    public Transform RightLeg { get; }
    public Transform LeftArm { get; }
    public Transform RightArm { get; }
    public Transform Torso { get; }
}

public sealed class PlayerAvatarPalette
{
    public static readonly PlayerAvatarPalette Default = new PlayerAvatarPalette(
        skin: new Color(0.93f, 0.91f, 0.87f),
        sweater: new Color(0.55f, 0.30f, 0.14f));

    public PlayerAvatarPalette(Color skin, Color sweater)
    {
        Skin = skin;
        Sweater = sweater;
    }

    public Color Skin { get; }
    public Color Sweater { get; }
}

public sealed class PlayerAvatarModelFactory
{
    private const int Segments = 16;
    private const float HipHeight = 0.80f;
    private const float HipHalfWidth = 0.075f;
    private const float ShoulderHeight = 1.44f;
    private const float ShoulderHalfWidth = 0.185f;
    private const float FlattenDepth = 0.78f;

    private readonly Shader _shader;
    private readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();

    public PlayerAvatarModelFactory(Shader shader)
    {
        _shader = shader ? shader : throw new ArgumentNullException(nameof(shader));
    }

    public PlayerAvatarRig Create(string name, PlayerAvatarPalette palette)
    {
        Material skin = CreateMaterial("ftmp_skin", palette.Skin);
        Material sweater = CreateMaterial("ftmp_sweater", palette.Sweater);

        var root = new GameObject($"FTMP Avatar ({name})");
        Transform body = CreateNode("Body", root.transform, Vector3.zero);

        Transform torso = CreatePart("Sweater", body, Vector3.zero, Mesh("sweater", BuildSweater), sweater);
        CreatePart("Neck", torso, new Vector3(0f, 1.52f, 0f), Mesh("neck", () => Limb("neck", 0.10f, 0.050f, 0.045f)), skin);
        CreatePart("Head", torso, new Vector3(0f, 1.72f, 0.01f), Mesh("head", () => LatheMeshBuilder.Ellipsoid("head", new Vector3(0.125f, 0.145f, 0.135f), 12, Segments)), skin);

        Transform leftLeg = CreateLeg("LeftLeg", body, -HipHalfWidth, skin);
        Transform rightLeg = CreateLeg("RightLeg", body, HipHalfWidth, skin);
        Transform leftArm = CreateArm("LeftArm", body, -ShoulderHalfWidth, skin, sweater);
        Transform rightArm = CreateArm("RightArm", body, ShoulderHalfWidth, skin, sweater);

        return new PlayerAvatarRig(root, body, leftLeg, rightLeg, leftArm, rightArm, torso);
    }

    private Transform CreateLeg(string name, Transform parent, float x, Material skin)
    {
        Transform hip = CreateNode(name, parent, new Vector3(x, HipHeight, 0f));
        CreatePart("Thigh", hip, Vector3.zero, Mesh("leg", () => Limb("leg", HipHeight - 0.03f, 0.048f, 0.032f)), skin, flipDown: true);
        CreatePart("Foot", hip, new Vector3(0f, -HipHeight + 0.025f, 0.045f), Mesh("foot", () => LatheMeshBuilder.Ellipsoid("foot", new Vector3(0.045f, 0.028f, 0.115f), 6, 10)), skin);
        return hip;
    }

    private Transform CreateArm(string name, Transform parent, float x, Material skin, Material sweater)
    {
        Transform shoulder = CreateNode(name, parent, new Vector3(x, ShoulderHeight, 0f));
        shoulder.localRotation = Quaternion.Euler(0f, 0f, x < 0f ? -6f : 6f);
        CreatePart("Sleeve", shoulder, Vector3.zero, Mesh("sleeve", () => Limb("sleeve", 0.58f, 0.058f, 0.050f)), sweater, flipDown: true);
        CreatePart("Hand", shoulder, new Vector3(0f, -0.63f, 0f), Mesh("hand", () => LatheMeshBuilder.Ellipsoid("hand", new Vector3(0.040f, 0.055f, 0.032f), 8, 10)), skin);
        return shoulder;
    }

    private static Mesh BuildSweater()
    {
        var profile = new[]
        {
            new ProfilePoint(0.60f, 0.000f),
            new ProfilePoint(0.60f, 0.205f),
            new ProfilePoint(0.66f, 0.200f),
            new ProfilePoint(0.95f, 0.170f),
            new ProfilePoint(1.25f, 0.190f),
            new ProfilePoint(1.42f, 0.180f),
            new ProfilePoint(1.49f, 0.120f),
            new ProfilePoint(1.51f, 0.070f),
            new ProfilePoint(1.60f, 0.062f),
            new ProfilePoint(1.60f, 0.000f),
        };
        return LatheMeshBuilder.Build("sweater", profile, Segments, new Vector3(1f, 1f, FlattenDepth));
    }

    private static Mesh Limb(string name, float length, float topRadius, float bottomRadius)
    {
        var profile = new[]
        {
            new ProfilePoint(0f, 0f),
            new ProfilePoint(0.005f, topRadius * 0.8f),
            new ProfilePoint(0.03f, topRadius),
            new ProfilePoint(length - 0.02f, bottomRadius),
            new ProfilePoint(length, bottomRadius * 0.6f),
            new ProfilePoint(length + 0.005f, 0f),
        };
        return LatheMeshBuilder.Build(name, profile, 10, Vector3.one);
    }

    private Mesh Mesh(string key, Func<Mesh> build)
    {
        if (!_meshes.TryGetValue(key, out Mesh mesh) || mesh == null)
        {
            mesh = build();
            _meshes[key] = mesh;
        }
        return mesh;
    }

    private Material CreateMaterial(string name, Color color)
    {
        var material = new Material(_shader) { name = name, color = color };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor(Shader.PropertyToID("_BaseColor"), color);
        }
        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.1f);
        }
        return material;
    }

    private static Transform CreateNode(string name, Transform parent, Vector3 localPosition)
    {
        var node = new GameObject(name).transform;
        node.SetParent(parent, worldPositionStays: false);
        node.localPosition = localPosition;
        return node;
    }

    private static Transform CreatePart(string name, Transform parent, Vector3 localPosition, Mesh mesh, Material material, bool flipDown = false)
    {
        Transform part = CreateNode(name, parent, localPosition);
        if (flipDown)
        {
            part.localRotation = Quaternion.Euler(180f, 0f, 0f);
        }
        part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        return part;
    }
}
