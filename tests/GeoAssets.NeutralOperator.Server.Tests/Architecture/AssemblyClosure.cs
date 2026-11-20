using System.Reflection;

namespace GeoAssets.NeutralOperator.Server.Tests.Architecture;

/// <summary>The <c>GeoAssets.*</c> assemblies an assembly references, directly and through each other, by simple name.</summary>
internal static class AssemblyClosure
{
    public static IReadOnlySet<string> Of(Assembly root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Assembly>([root]);
        while (pending.Count > 0)
        {
            foreach (var reference in pending.Pop().GetReferencedAssemblies())
            {
                if (reference.Name is not { } name || !name.StartsWith("GeoAssets.", StringComparison.Ordinal) || !seen.Add(name)) continue;
                pending.Push(Assembly.Load(reference));
            }
        }

        return seen;
    }
}
