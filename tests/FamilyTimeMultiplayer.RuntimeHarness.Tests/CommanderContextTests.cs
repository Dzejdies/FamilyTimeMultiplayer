using System;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Commands;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class CommanderContextTests : IDisposable
{
    private readonly RecordingHook _hook = new RecordingHook();

    public CommanderContextTests()
    {
        CommanderContext.ResetAfterSession();
        CommanderContext.Hook = _hook;
    }

    [Fact]
    public void NativeHostOutsideRemoteCommandNeedsNoScopeOrHookChanges()
    {
        Assert.Null(CommanderContext.EnterNativeHost());
        Assert.Empty(_hook.Events);
    }

    [Fact]
    public void NativeHostCallbackRestoresOuterPlayerAndMamaAfterNestedRemoteWork()
    {
        var outer = new TestCommander(1);
        var inner = new TestCommander(2);
        using (CommanderContext.Enter(outer))
        {
            using (CommanderContext.EnterNativeHost())
            {
                Assert.Null(CommanderContext.Active);
                using (CommanderContext.Enter(inner)) Assert.Same(inner, CommanderContext.Active);
                Assert.Null(CommanderContext.Active);
            }
            Assert.Same(outer, CommanderContext.Active);
        }
        Assert.Null(CommanderContext.Active);
        Assert.Equal(new[] { "enter1", "exit1", "enter2", "exit2", "enter1", "exit1" }, _hook.Events);
    }

    [Fact]
    public void NativeCallbackFailureRestoresRemoteOwner()
    {
        var commander = new TestCommander(1);
        using (CommanderContext.Enter(commander))
        {
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using (CommanderContext.EnterNativeHost()) throw new InvalidOperationException("Native callback failed.");
            }));
            Assert.Same(commander, CommanderContext.Active);
        }
        Assert.Null(CommanderContext.Active);
    }

    [Fact]
    public void FailedHostHookRestoresOuterContextAndDoesNotCreateScope()
    {
        var commander = new TestCommander(1);
        using (CommanderContext.Enter(commander))
        {
            _hook.FailNextExit = true;
            Assert.Throws<InvalidOperationException>(() => CommanderContext.EnterNativeHost());
            Assert.Same(commander, CommanderContext.Active);
        }
        Assert.Null(CommanderContext.Active);
    }

    public void Dispose()
    {
        CommanderContext.Hook = null;
        CommanderContext.ResetAfterSession();
    }

    private sealed class TestCommander : IRemoteCommanderIdentity
    {
        public TestCommander(int playerId) => PlayerId = playerId;
        public int PlayerId { get; }
        public GAT.Handle AvatarHandle => default;
        public UnityEngine.Vector3 AvatarPosition => default;
        public UnityEngine.Transform Eye => new UnityEngine.Transform();
    }

    private sealed class RecordingHook : ICommanderScopeHook
    {
        public List<string> Events { get; } = new List<string>();
        public bool FailNextExit { get; set; }
        public void OnEnter(IRemoteCommanderIdentity commander) => Events.Add("enter" + commander.PlayerId);
        public void OnExit(IRemoteCommanderIdentity commander)
        {
            if (FailNextExit)
            {
                FailNextExit = false;
                throw new InvalidOperationException("Mama restore failed.");
            }
            Events.Add("exit" + commander.PlayerId);
        }
    }
}
