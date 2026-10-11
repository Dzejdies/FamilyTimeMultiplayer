using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Sessions;
using UnityEngine;

namespace FamilyTimeMultiplayer.Avatars;

public sealed class RemoteAvatarPresenter
{
    private sealed class Presented
    {
        public Presented(PlayerAvatarRig rig)
        {
            Rig = rig;
            Animator = new PlayerAvatarAnimator(rig);
        }

        public PlayerAvatarRig Rig { get; }
        public PlayerAvatarAnimator Animator { get; }
    }

    private readonly ManualLogSource _log;
    private readonly Dictionary<int, Presented> _avatars = new Dictionary<int, Presented>();
    private PlayerAvatarModelFactory? _factory;
    private bool _factoryUnavailable;

    public RemoteAvatarPresenter(ManualLogSource log)
    {
        _log = log;
    }

    public void Present(ISession? session, float deltaTime)
    {
        if (session == null)
        {
            Clear();
            return;
        }
        var seen = new HashSet<int>();
        foreach (RosterEntry entry in session.Roster)
        {
            if (entry.PlayerId == session.LocalPlayerId
                || !session.TryGetRemotePlayerPosition(entry, out Vector3 position)
                || !session.TryGetRemotePlayerYaw(entry, out float yaw))
            {
                continue;
            }
            Presented? avatar = GetOrCreate(entry);
            if (avatar == null)
            {
                return;
            }
            seen.Add(entry.PlayerId);
            avatar.Rig.Root.SetActive(true);
            avatar.Animator.Advance(position, yaw, deltaTime);
        }
        foreach (int stale in _avatars.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            Object.Destroy(_avatars[stale].Rig.Root);
            _avatars.Remove(stale);
        }
    }

    public void Clear()
    {
        foreach (Presented avatar in _avatars.Values)
        {
            Object.Destroy(avatar.Rig.Root);
        }
        _avatars.Clear();
    }

    private Presented? GetOrCreate(RosterEntry entry)
    {
        if (_avatars.TryGetValue(entry.PlayerId, out Presented existing))
        {
            return existing;
        }
        PlayerAvatarModelFactory? factory = RequireFactory();
        if (factory == null)
        {
            return null;
        }
        var avatar = new Presented(factory.Create(entry.Name, PlayerAvatarPalette.Default));
        Object.DontDestroyOnLoad(avatar.Rig.Root);
        _avatars.Add(entry.PlayerId, avatar);
        _log.LogInfo($"[Avatar] created model for '{entry.Name}' (#{entry.PlayerId})");
        return avatar;
    }

    private PlayerAvatarModelFactory? RequireFactory()
    {
        if (_factory != null || _factoryUnavailable)
        {
            return _factory;
        }
        try
        {
            _factory = new PlayerAvatarModelFactory(AvatarShaderResolver.Resolve(_log));
        }
        catch (System.InvalidOperationException exception)
        {
            _factoryUnavailable = true;
            _log.LogError($"[Avatar] player models disabled: {exception.Message}");
        }
        return _factory;
    }
}
