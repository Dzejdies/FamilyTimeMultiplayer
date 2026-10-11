using System.Text;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using UnityEngine;
using Movie3D = GAT.Movie3D;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class SessionProbe
{
    private const double Interval = 2.0;

    private readonly ManualLogSource _log;
    private double _nextProbe;
    private bool _templatesDumped;
    private int _shotIndex;

    public SessionProbe(ManualLogSource log)
    {
        _log = log;
    }

    public void Tick(double now, ISession? session)
    {
        DumpTemplatesOnce();
        if (now < _nextProbe || session == null || !LocalPlayer.IsWorldReady)
        {
            return;
        }
        _nextProbe = now + Interval;
        global::GAT.GAT gat = GatInterop.RequireGat();
        var line = new StringBuilder();
        line.Append($"[Probe] role={session.Role} self={LocalPlayer.Position} possessed={LocalPlayer.PossessedHandle.stableIndex} agents={gat.instanceCount}");
        if (session is ClientSession client)
        {
            line.Append($" mapped={client.Identities.Count} puppets={client.LastPuppetCount}");
        }
        foreach (RosterEntry entry in session.Roster)
        {
            if (entry.PlayerId == session.LocalPlayerId)
            {
                continue;
            }
            string netPosition = session.TryGetRemotePlayerPosition(entry, out Vector3 position) ? position.ToString() : "unknown";
            line.Append($" | {entry.Name}#{entry.PlayerId} net={netPosition}");
            ProbeGpuPosition(session, entry);
        }
        _log.LogInfo(line.ToString());
        CaptureShot(session);
    }

    private void CaptureShot(ISession session)
    {
        string directory = System.IO.Path.Combine(Application.persistentDataPath, "ftmp_shots");
        System.IO.Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, $"{session.Role}_{_shotIndex++ % 30:00}.png");
        if (!FamilyTimeMultiplayer.Game.GameApiCompatibility.CaptureScreenshot(path))
        {
            _log.LogWarning("[Probe] this game build does not expose Unity screenshot capture.");
        }
    }

    private void DumpTemplatesOnce()
    {
        if (_templatesDumped || !LocalPlayer.IsWorldReady)
        {
            return;
        }
        _templatesDumped = true;
        global::GAT.GAT gat = GatInterop.RequireGat();
        int playerTemplate = GatInterop.RequirePlayerTemplateIndex();
        var entries = gat.GetTemplateEntries();
        for (int i = 0; i < entries.Count; i++)
        {
            Template template = gat.GetTemplate(i);
            if (template == null)
            {
                _log.LogInfo($"[Templates] #{i} <null> count={entries[i]?.count}");
                continue;
            }
            var meshes = new StringBuilder();
            foreach (Template.TemplateMesh lod in template.templateMeshes)
            {
                meshes.Append(lod?.mesh == null ? "[none]" : $"[{lod.mesh.name} v={lod.mesh.vertexCount} d={lod.distance}]");
            }
            string marker = i == playerTemplate ? " <PLAYER>" : string.Empty;
            _log.LogInfo($"[Templates] #{i} {template.name}{marker} faction={template.faction} count={entries[i]?.count} material={(template.material != null ? template.material.name : "none")} lods={meshes}");
        }
        RigidTransformManager? rigids = World.rigidTransformManager;
        if (rigids != null)
        {
            for (int i = 0; i < rigids.PrefabCount; i++)
            {
                if (RigidTransformApi.TryGetPrefab(rigids, i, out RigidPrefab prefab))
                {
                    GATPickupBehavior.CommandShape commandShape = RigidTransformApi.PrefabCommandShape(prefab);
                    string shape = $"{commandShape} needs={GATPickupBehavior.RequiredAgents(commandShape)}";
                    _log.LogInfo($"[Rigids] prefab #{i} {RigidTransformApi.PrefabName(prefab)} {shape}");
                }
            }
            _log.LogInfo($"[Rigids] managed in world: {RigidTransformApi.Count(rigids)}");
        }
        foreach (GAT.PauseMenuBook book in Resources.FindObjectsOfTypeAll<GAT.PauseMenuBook>())
        {
            GameObject? mainMenu = HarmonyLib.Traverse.Create(book).Field<GameObject>("mainMenu").Value;
            _log.LogInfo($"[PauseMenu] book '{book.name}' mainMenu='{(mainMenu != null ? mainMenu.name : "none")}'");
            if (mainMenu != null)
            {
                DumpUi(mainMenu.transform, 0);
            }
        }
        new MovieArtExporter(_log).ExportSleepMovies(System.IO.Path.Combine(Application.persistentDataPath, "ftmp_art_dump"));
        foreach (Movie3D movie in Resources.FindObjectsOfTypeAll<Movie3D>())
        {
            DumpHierarchy(movie.transform, 0);

        }
    }

    private void DumpUi(Transform node, int depth)
    {
        if (depth > 5)
        {
            return;
        }
        var info = new StringBuilder();
        foreach (Component component in node.GetComponents<Component>())
        {
            if (component is Transform)
            {
                continue;
            }
            info.Append($" {component.GetType().Name}");

        }
        RectTransform? rect = node as RectTransform;
        string layout = rect != null ? $" pos={rect.anchoredPosition} size={rect.sizeDelta}" : string.Empty;
        _log.LogInfo($"[PauseMenu] {new string(' ', depth * 2)}{node.name}{layout}{info}");
        for (int i = 0; i < node.childCount; i++)
        {
            DumpUi(node.GetChild(i), depth + 1);
        }
    }

    private void DumpHierarchy(Transform node, int depth)
    {
        if (depth > 6)
        {
            return;
        }
        var components = new StringBuilder();
        foreach (Component component in node.GetComponents<Component>())
        {
            switch (component)
            {
                case SkinnedMeshRenderer skinned:
                    components.Append($" SMR(mats={skinned.sharedMaterials.Length})");
                    break;
                case MeshFilter filter:
                    components.Append(" MF");
                    break;
                case Transform _:
                    break;
                default:
                    components.Append($" {component.GetType().Name}");
                    break;
            }
        }
        bool interesting = depth < 2 || components.Length > 0;
        if (interesting)
        {
            _log.LogInfo($"[Movies] {new string(' ', depth * 2)}{node.name}{components}");
        }
        for (int i = 0; i < node.childCount; i++)
        {
            DumpHierarchy(node.GetChild(i), depth + 1);
        }
    }

    private void ProbeGpuPosition(ISession session, RosterEntry entry)
    {
        ushort localIndex = entry.AvatarStableIndex;
        if (session is ClientSession client && !client.Identities.TryGetLocal(entry.AvatarStableIndex, out localIndex))
        {
            return;
        }
        Handle handle = GatInterop.ToHandle(localIndex);
        string name = entry.Name;
        GatInterop.RequireGat().GetInstanceMatrixAsync(handle, matrix =>
        {
            string gpu = matrix.HasValue ? ((Vector3)matrix.Value.GetColumn(3)).ToString() : "missing";
            _log.LogInfo($"[Probe] {name} avatar {handle} gpu={gpu}");
        });
    }
}
