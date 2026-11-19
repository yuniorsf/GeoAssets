using System.Reflection;

namespace GeoAssets.Workflow.TestKit;

/// <summary>A stand-in for a collaborator a test must be able to construct but never reaches: any call fails the test.</summary>
public class NeverCalledService : DispatchProxy
{
    /// <summary>A proxy of <typeparamref name="T"/> whose every member throws <see cref="NotSupportedException"/>.</summary>
    public static T Of<T>()
        where T : class => Create<T, NeverCalledService>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new NotSupportedException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name} must not be called here.");
}
