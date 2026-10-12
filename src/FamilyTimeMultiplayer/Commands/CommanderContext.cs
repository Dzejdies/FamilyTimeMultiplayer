using System;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.Commands;

public interface IRemoteCommanderIdentity
{
    int PlayerId { get; }

    Handle AvatarHandle { get; }

    Vector3 AvatarPosition { get; }

    Transform Eye { get; }
}

public interface ICommanderScopeHook
{
    void OnEnter(IRemoteCommanderIdentity commander);

    void OnExit(IRemoteCommanderIdentity commander);
}

public static class CommanderContext
{
    public static IRemoteCommanderIdentity? Active { get; private set; }

    public static ICommanderScopeHook? Hook { get; set; }

    public static void ResetAfterSession() => Active = null;

    public static IDisposable? EnterNativeHost()
    {
        IRemoteCommanderIdentity? previous = Active;
        if (previous == null) return null;
        Active = null;
        try
        {
            Hook?.OnExit(previous);
            return new NativeHostScope(previous);
        }
        catch
        {
            Active = previous;
            throw;
        }
    }

    public static Scope Enter(IRemoteCommanderIdentity commander)
    {
        if (Active != null && !ReferenceEquals(Active, commander))
        {
            throw new InvalidOperationException($"Commander context for player {Active.PlayerId} is still active; cannot enter player {commander.PlayerId}.");
        }
        IRemoteCommanderIdentity? previous = Active;
        Active = commander;
        if (previous == null && Hook != null)
        {
            try
            {
                Hook.OnEnter(commander);
            }
            catch
            {
                Active = previous;
                throw;
            }
        }
        return new Scope(previous, commander);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly IRemoteCommanderIdentity? _previous;
        private readonly IRemoteCommanderIdentity _commander;

        public Scope(IRemoteCommanderIdentity? previous, IRemoteCommanderIdentity commander)
        {
            _previous = previous;
            _commander = commander;
        }

        public void Dispose()
        {
            Active = _previous;
            if (_previous == null)
            {
                Hook?.OnExit(_commander);
            }
        }
    }

    private sealed class NativeHostScope : IDisposable
    {
        private IRemoteCommanderIdentity? _previous;

        public NativeHostScope(IRemoteCommanderIdentity previous) => _previous = previous;

        public void Dispose()
        {
            IRemoteCommanderIdentity? previous = _previous;
            if (previous == null) return;
            _previous = null;
            Active = previous;
            Hook?.OnEnter(previous);
        }
    }
}
