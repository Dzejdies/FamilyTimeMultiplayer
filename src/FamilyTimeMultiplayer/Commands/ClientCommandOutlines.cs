using System;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Commands;

public static class ClientCommandOutlines
{
    private static IReadOnlyList<CommandOutline> _outlines = Array.Empty<CommandOutline>();
    private static ClientRigidRegistry? _rigids;
    private static readonly List<RigidTransform> Selection = new List<RigidTransform>();
    private static readonly List<CommandVisuals.PrimaryOutlineEntry> Primary = new List<CommandVisuals.PrimaryOutlineEntry>();
    private static int _bindings = -1;
    private static bool _dirty;
    private static bool _needsPaint;
    private static CommandVisuals? _painted;
    private static OutlineManager? _manager;

    public static bool IsActive => ClientCommandBridge.IsActive && _rigids != null;

    public static bool Owns(CommandVisuals visuals) => IsActive && World.commandService != null &&
        ReferenceEquals(visuals, Traverse.Create(World.commandService).Field<CommandVisuals>("_visuals").Value);

    public static ICollection<RigidTransform> Selected
    {
        get { Resolve(); return Selection; }
    }

    public static List<CommandVisuals.PrimaryOutlineEntry> PrimaryEntries
    {
        get { Resolve(); return Primary; }
    }

    public static void Invalidate() => _needsPaint = true;

    public static void Apply(CommandOutlinesMessage message, ClientRigidRegistry rigids)
    {
        _outlines = message.Outlines;
        _rigids = rigids;
        _dirty = true;
        _needsPaint = true;
    }

    public static void Refresh(CommandService service)
    {
        if (!IsActive || OutlineManager.Instance == null) return;
        Resolve();
        CommandVisuals visuals = Traverse.Create(service).Field<CommandVisuals>("_visuals").Value;
        if (!_needsPaint && ReferenceEquals(_painted, visuals) && _manager == OutlineManager.Instance) return;
        visuals.RefreshSelectionOutline(Selection);
        visuals.RefreshPrimaryOutline(Primary);
        _painted = visuals;
        _manager = OutlineManager.Instance;
        _needsPaint = false;
    }

    public static void Clear()
    {
        _outlines = Array.Empty<CommandOutline>();
        _rigids = null;
        Selection.Clear();
        Primary.Clear();
        _bindings = -1;
        _dirty = false;
        _needsPaint = false;
        if (_painted != null)
        {
            _painted.RefreshSelectionOutline(Selection);
            _painted.RefreshPrimaryOutline(Primary);
            if (World.Instance != null && !World.isShuttingDown) _painted.UpdateHoverOutline(Selection);
        }
        _painted = null;
        _manager = null;
    }

    private static void Resolve()
    {
        if (_rigids == null) return;
        if (!_dirty && _bindings == _rigids.BindingVersion && !Selection.Any(rigid => !RigidTransformApi.IsAlive(rigid)) &&
            !Primary.Any(entry => !RigidTransformApi.IsAlive(GameApiCompatibility.PrimaryOutlineRigid(entry)))) return;
        Selection.Clear();
        Primary.Clear();
        foreach (CommandOutline entry in _outlines)
        {
            if (!_rigids.TryResolve(entry.RigidNetId, out RigidTransform rigid)) continue;
            if (entry.Kind == CommandOutlineKind.Selected) Selection.Add(rigid);
            else Primary.Add(GameApiCompatibility.CreatePrimaryOutlineEntry(rigid, entry.Kind switch
            {
                CommandOutlineKind.Primary => CommandVisuals.PrimaryOutlineKind.Primary,
                CommandOutlineKind.Dropoff => CommandVisuals.PrimaryOutlineKind.Dropoff,
                CommandOutlineKind.Construction => CommandVisuals.PrimaryOutlineKind.Construction,
                _ => throw new ArgumentOutOfRangeException(nameof(entry.Kind)),
            }));
        }
        _bindings = _rigids.BindingVersion;
        _dirty = false;
        _needsPaint = true;
    }
}
