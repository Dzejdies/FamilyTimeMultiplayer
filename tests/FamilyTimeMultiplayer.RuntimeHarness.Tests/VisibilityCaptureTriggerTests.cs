using FamilyTimeMultiplayer.RuntimeHarness.Contracts;
using Xunit;

namespace FamilyTimeMultiplayer.RuntimeHarness.Tests;

public sealed class VisibilityCaptureTriggerTests
{
    [Fact]
    public void GetSample_uses_separate_modified_keys_for_visible_and_near_states()
    {
        Assert.Equal(1, VisibilityCaptureTrigger.GetSample(true, true, f8Pressed: true, f10Pressed: false));
        Assert.Equal(2, VisibilityCaptureTrigger.GetSample(true, true, f8Pressed: false, f10Pressed: true));
        Assert.Equal(0, VisibilityCaptureTrigger.GetSample(false, true, f8Pressed: true, f10Pressed: false));
        Assert.Equal(0, VisibilityCaptureTrigger.GetSample(true, false, f8Pressed: false, f10Pressed: true));
    }
}
