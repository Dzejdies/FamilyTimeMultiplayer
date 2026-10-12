using System;
using System.Collections.Generic;

namespace FamilyTimeMultiplayer.Core.Families;

public sealed class FamilyLedger
{
    private readonly Dictionary<ushort, string> _ownerByMember = new Dictionary<ushort, string>();
    private readonly Dictionary<string, HashSet<ushort>> _membersByOwner = new Dictionary<string, HashSet<ushort>>(StringComparer.Ordinal);
    private static readonly IReadOnlyCollection<ushort> NoMembers = Array.Empty<ushort>();

    public IEnumerable<string> Owners => _membersByOwner.Keys;

    public void Assign(ushort member, string owner)
    {
        if (string.IsNullOrEmpty(owner))
        {
            throw new ArgumentException("A family owner needs a non-empty key.", nameof(owner));
        }
        Release(member);
        _ownerByMember.Add(member, owner);
        if (!_membersByOwner.TryGetValue(owner, out HashSet<ushort> members))
        {
            members = new HashSet<ushort>();
            _membersByOwner.Add(owner, members);
        }
        members.Add(member);
    }

    public bool Release(ushort member)
    {
        if (!_ownerByMember.TryGetValue(member, out string owner))
        {
            return false;
        }
        _ownerByMember.Remove(member);
        _membersByOwner[owner].Remove(member);
        return true;
    }

    public bool TryGetOwner(ushort member, out string owner) => _ownerByMember.TryGetValue(member, out owner);

    public bool Belongs(ushort member, string owner) =>
        _ownerByMember.TryGetValue(member, out string actual) && string.Equals(actual, owner, StringComparison.Ordinal);

    public IReadOnlyCollection<ushort> MembersOf(string owner) =>
        _membersByOwner.TryGetValue(owner, out HashSet<ushort> members) ? members : NoMembers;

    public int CountOf(string owner) => MembersOf(owner).Count;

    public bool HasFamily(string owner) => CountOf(owner) > 0;

    public ushort[] ExcludedWorkersFor(string owner, IEnumerable<ushort> workers, IEnumerable<ushort> unavailable)
    {
        var exclusions = new HashSet<ushort>(unavailable);
        foreach (ushort worker in workers)
        {
            if (!Belongs(worker, owner))
            {
                exclusions.Add(worker);
            }
        }
        var result = new ushort[exclusions.Count];
        exclusions.CopyTo(result);
        return result;
    }

    public void Clear()
    {
        _ownerByMember.Clear();
        _membersByOwner.Clear();
    }
}
