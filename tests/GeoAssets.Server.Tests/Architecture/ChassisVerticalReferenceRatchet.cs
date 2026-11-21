using System.Xml.Linq;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// Chassis vertical-reference ratchet (XD01-388, ADR-001): no chassis library — the generic order-type engine and its
/// hosting, persistence, REST and messaging adapters — may reference a vertical, neither as an assembly nor as a
/// <c>ProjectReference</c> in its project file. A vertical depends on the chassis, never the reverse. The allowlist
/// started empty and must stay empty.
/// </summary>
internal static class ChassisVerticalReferenceRatchet
{
    /// <summary>
    /// The chassis assemblies the ratchet covers; each is also the name of its project under <c>core/</c> or <c>workflow/</c>,
    /// and every <c>GeoAssets.Workflow*</c> project there is one of them — none is out of scope since XD01-399 moved the
    /// FieldService dispatch agents (formerly <c>GeoAssets.Workflow.Agents</c>) into <c>verticals/FieldService</c>.
    /// </summary>
    public static IReadOnlyList<string> ChassisAssemblies { get; } =
    [
        "GeoAssets.Workflow",
        "GeoAssets.Workflow.EFCore",
        "GeoAssets.Workflow.Rest",
        "GeoAssets.Workflow.Hosting",
        "GeoAssets.Workflow.Messaging.Kafka",
        "GeoAssets.Workflow.Messaging.ServiceBus",
        "GeoAssets.Workflow.Messaging.Protobuf",
        "GeoAssets.Workflow.SlaGuard",
    ];

    /// <summary>Assembly-name prefixes that name a vertical (or a vertical's integration).</summary>
    public static IReadOnlyList<string> ForbiddenAssemblyPrefixes { get; } =
    [
        "GeoAssets.NeutralOperator",
        "GeoAssets.FieldService",
        "GeoAssets.IpBilling",
        "GeoAssets.Integrations",
    ];

    /// <summary>Directories of the repository that hold verticals; a <c>ProjectReference</c> into one is a vertical reference.</summary>
    public static IReadOnlyList<string> ForbiddenProjectDirectories { get; } = ["verticals", "ipbilling"];

    /// <summary>
    /// Today's tolerated references, keyed <c>"{assembly} -&gt; {reference}"</c>. Empty since XD01-388 — never add an entry.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Allowlist { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
        };

    /// <summary>The referenced assembly names that name a vertical.</summary>
    public static IReadOnlyList<string> FindAssemblyViolations(IEnumerable<string> referencedAssemblyNames)
    {
        ArgumentNullException.ThrowIfNull(referencedAssemblyNames);

        return [.. referencedAssemblyNames
            .Where(name => ForbiddenAssemblyPrefixes.Any(prefix => IsInNamespace(name, prefix)))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The <c>ProjectReference</c> paths of a project file that point into a vertical.</summary>
    public static IReadOnlyList<string> FindProjectReferenceViolations(string projectFileText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFileText);

        return [.. XDocument.Parse(projectFileText)
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Replace('\\', '/'))
            .Where(IsVerticalProject)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Every violation of <paramref name="assemblyName"/> — by assembly reference or by project reference — as
    /// <c>"{assembly} -&gt; {reference}"</c>, minus <paramref name="allowlist"/>. The production check and the
    /// failure-proof test run this same function.
    /// </summary>
    public static IReadOnlyList<string> Evaluate(
        string assemblyName,
        IEnumerable<string> referencedAssemblyNames,
        string projectFileText,
        IReadOnlyDictionary<string, string> allowlist)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(allowlist);

        return [.. FindAssemblyViolations(referencedAssemblyNames)
            .Select(reference => $"{assemblyName} -> {reference} (assembly reference)")
            .Concat(FindProjectReferenceViolations(projectFileText)
                .Select(reference => $"{assemblyName} -> {reference} (ProjectReference)"))
            .Where(violation => !allowlist.Keys.Any(key => violation.StartsWith(key, StringComparison.Ordinal)))];
    }

    private static bool IsVerticalProject(string include)
    {
        var segments = include.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var project = Path.GetFileNameWithoutExtension(segments[^1]);

        return ForbiddenAssemblyPrefixes.Any(prefix => IsInNamespace(project, prefix))
            || segments[..^1].Any(segment => ForbiddenProjectDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsInNamespace(string name, string prefix) =>
        string.Equals(name, prefix, StringComparison.Ordinal)
        || name.StartsWith(prefix + ".", StringComparison.Ordinal);
}
