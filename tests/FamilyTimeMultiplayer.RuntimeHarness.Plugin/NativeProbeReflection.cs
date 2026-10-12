using System;
using System.Linq;
using System.Reflection;

namespace FamilyTimeMultiplayer.RuntimeHarness;

internal static class NativeProbeReflection
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static MethodInfo Method(object target, string name, int parameterCount) =>
        target.GetType().GetMethods(InstanceMembers)
            .Single(method => method.Name == name && method.GetParameters().Length == parameterCount);

    internal static object? Invoke(object target, string name, params object[] arguments) =>
        target.GetType().GetMethods(InstanceMembers)
            .Single(method => method.Name == name && method.GetParameters().Length == arguments.Length &&
                method.GetParameters().Select((parameter, index) => arguments[index] == null ||
                    parameter.ParameterType.IsInstanceOfType(arguments[index])).All(compatible => compatible))
            .Invoke(target, arguments);
}
