using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;

namespace FamilyTimeMultiplayer.Commands;

public sealed class RemoteCommandOutlines : IDisposable
{
    private static readonly ConditionalWeakTable<CommandVisuals, RemoteCommandOutlines> Owners = new ConditionalWeakTable<CommandVisuals, RemoteCommandOutlines>();
    private RigidTransform[] _selected = Array.Empty<RigidTransform>();
    private CommandVisuals.PrimaryOutlineEntry[] _primary = Array.Empty<CommandVisuals.PrimaryOutlineEntry>();
    private bool _disposed;

    public RemoteCommandOutlines(CommandVisuals visuals) => Owners.Add(visuals, this);

    public static bool TryGet(CommandVisuals visuals, out RemoteCommandOutlines owner) => Owners.TryGetValue(visuals, out owner);

    public void Select(ICollection<RigidTransform> selected)
    {
        if (!_disposed) _selected = selected.Where(rigid => rigid != null).ToArray();
    }

    public void SetPrimary(List<CommandVisuals.PrimaryOutlineEntry> entries)
    {
        if (!_disposed) _primary = entries.ToArray();
    }

    public void Clear()
    {
        _selected = Array.Empty<RigidTransform>();
        _primary = Array.Empty<CommandVisuals.PrimaryOutlineEntry>();
    }

    public CommandOutlinesMessage Capture(HostRigidRegistry rigids)
    {
        var entries = new HashSet<(CommandOutlineKind Kind, int Id)>();
        foreach (RigidTransform rigid in _selected)
        {
            if (rigids.TryGetNetId(rigid, out int id)) entries.Add((CommandOutlineKind.Selected, id));
        }
        foreach (CommandVisuals.PrimaryOutlineEntry entry in _primary)
        {
            RigidTransform? rigid = GameApiCompatibility.PrimaryOutlineRigid(entry);
            if (!RigidTransformApi.IsAlive(rigid) || !rigids.TryGetNetId(rigid!, out int id)) continue;
            CommandOutlineKind kind = entry.kind switch
            {
                CommandVisuals.PrimaryOutlineKind.Primary => CommandOutlineKind.Primary,
                CommandVisuals.PrimaryOutlineKind.Dropoff => CommandOutlineKind.Dropoff,
                CommandVisuals.PrimaryOutlineKind.Construction => CommandOutlineKind.Construction,
                _ => throw new ArgumentOutOfRangeException(nameof(entry.kind)),
            };
            entries.Add((kind, id));
        }
        return new CommandOutlinesMessage(entries.OrderBy(entry => entry.Kind).ThenBy(entry => entry.Id)
            .Select(entry => new CommandOutline(entry.Kind, entry.Id)).ToArray());
    }

    public void Dispose()
    {
        _disposed = true;
        Clear();
    }
}
