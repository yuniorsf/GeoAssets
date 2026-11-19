using System.Reflection;

namespace GeoAssets.Server.Tests;

/// <summary>A service the identity API's other endpoints depend on: it only has to be resolvable in a test host, never called.</summary>
internal class NeverCalledProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new NotSupportedException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name} is not exercised by these tests.");
}
