using System;
using BepInEx.Logging;
using UnityEngine;

namespace FamilyTimeMultiplayer.Avatars;

public static class AvatarShaderResolver
{
    private static readonly string[] Candidates =
    {
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Unlit",
        "Unlit/Color",
        "Sprites/Default",
    };

    public static Shader Resolve(ManualLogSource log)
    {
        foreach (string name in Candidates)
        {
            Shader shader = Shader.Find(name);
            if (shader != null && shader.isSupported)
            {
                log.LogInfo($"[Avatar] using shader '{name}' (Shader.Find)");
                return shader;
            }
        }
        foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
        {
            Shader shader = material.shader;
            if (shader == null || !shader.isSupported)
            {
                continue;
            }
            foreach (string name in Candidates)
            {
                if (shader.name == name)
                {
                    log.LogInfo($"[Avatar] using shader '{name}' borrowed from material '{material.name}'");
                    return shader;
                }
            }
        }
        throw new InvalidOperationException($"No usable avatar shader in this build; tried: {string.Join(", ", Candidates)}.");
    }
}
