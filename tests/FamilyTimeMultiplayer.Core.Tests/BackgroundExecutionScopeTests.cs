using FamilyTimeMultiplayer.Core.Launch;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class BackgroundExecutionScopeTests
{
    [Fact]
    public void InactiveScopePreservesNativeAndModSettings()
    {
        bool value = false;
        int writes = 0;
        using var scope = new BackgroundExecutionScope(() => value, next => { value = next; writes++; });
        scope.Update(false);
        value = true;
        scope.Update(false);
        Assert.True(value);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void ActiveScopeAppliesOnceAndRestoresItsChange()
    {
        bool value = false;
        int writes = 0;
        using var scope = new BackgroundExecutionScope(() => value, next => { value = next; writes++; });
        scope.Update(true);
        scope.Update(true);
        Assert.True(value);
        Assert.Equal(1, writes);
        scope.Update(false);
        scope.Dispose();
        Assert.False(value);
        Assert.Equal(2, writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveScopeDoesNotOverwriteAnotherOwnersLaterChange(bool originallyEnabled)
    {
        bool value = originallyEnabled;
        int writes = 0;
        using var scope = new BackgroundExecutionScope(() => value, next => { value = next; writes++; });
        scope.Update(true);
        int activationWrites = writes;
        value = false;
        scope.Update(true);
        scope.Update(false);
        Assert.False(value);
        Assert.Equal(activationWrites, writes);
    }
}
