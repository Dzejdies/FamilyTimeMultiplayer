using System;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeMultiplayer.Core.Security;

namespace FamilyTimeMultiplayer.Core.Families;

public sealed class FamilyIdentityRegistry
{
    private readonly Dictionary<string, string?> _keys = new Dictionary<string, string?>(StringComparer.Ordinal);
    private readonly string _hostOwner;
    public FamilyIdentityRegistry(FamilyWorldRecord record, string hostOwner)
    {
        _hostOwner = hostOwner;
        foreach (FamilyRecord family in record.Families) _keys.Add(family.IsHostFamily ? hostOwner : family.Owner, family.IsHostFamily ? null : family.PublicKey);
        _keys[hostOwner] = null;
    }
    public IEnumerable<string> Owners => _keys.Keys;
    public string? PublicKeyFor(string owner) => _keys.TryGetValue(owner, out string? key) ? key : null;
    public string? OwnerOf(string publicKey)
    {
        PlayerIdentity.ValidatePublicKey(publicKey);
        return _keys.FirstOrDefault(entry => entry.Value == publicKey && entry.Key != _hostOwner).Key;
    }
    public bool IsLegacy(string owner) => owner != _hostOwner && _keys.TryGetValue(owner, out string? key) && key == null;
    public string CreateFamily(string publicKey, string displayName)
    {
        PlayerIdentity.ValidatePublicKey(publicKey);
        if (OwnerOf(publicKey) != null) throw new InvalidOperationException("Identity already owns a family.");
        string owner = displayName;
        for (int suffix = 2; _keys.ContainsKey(owner); suffix++) owner = $"{displayName} ({suffix})";
        _keys.Add(owner, publicKey);
        return owner;
    }
    public void BindLegacy(string publicKey, string owner)
    {
        PlayerIdentity.ValidatePublicKey(publicKey);
        if (!IsLegacy(owner) || OwnerOf(publicKey) != null) throw new InvalidOperationException("Legacy family cannot be assigned to this identity.");
        _keys[owner] = publicKey;
    }
}
