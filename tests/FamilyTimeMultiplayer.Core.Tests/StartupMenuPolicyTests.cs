using System;
using System.Reflection;
using FamilyTimeMultiplayer.Core.Launch;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public sealed class StartupMenuPolicyTests
{
    [Fact]
    public void NativeTitleFlowKeepsTheModStartupPageClosed()
    {
        MethodInfo policy = RequirePolicy();

        Assert.False(ShouldShow(policy, nativeTitleAvailable: true, menuEnabled: true, normalLaunch: true));
        Assert.False(ShouldShow(policy, nativeTitleAvailable: false, menuEnabled: true, normalLaunch: false));
        Assert.False(ShouldShow(policy, nativeTitleAvailable: false, menuEnabled: false, normalLaunch: true));
        Assert.True(ShouldShow(policy, nativeTitleAvailable: false, menuEnabled: true, normalLaunch: true));
    }

    private static MethodInfo RequirePolicy()
    {
        Type? policyType = typeof(LaunchOptions).Assembly.GetType("FamilyTimeMultiplayer.Core.Launch.StartupMenuPolicy");
        Assert.NotNull(policyType);
        MethodInfo? method = policyType!.GetMethod("ShouldShowStartupMenu", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return method!;
    }

    private static bool ShouldShow(MethodInfo policy, bool nativeTitleAvailable, bool menuEnabled, bool normalLaunch) =>
        (bool)policy.Invoke(null, new object[] { nativeTitleAvailable, menuEnabled, normalLaunch })!;
}
