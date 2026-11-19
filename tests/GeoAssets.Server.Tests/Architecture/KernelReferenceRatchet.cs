namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// Kernel reference ratchet (XD01-352, ADR-001 invariant I1 / smell O): no <c>GeoAssets.Server</c> type may
/// reference a provider, domain or vertical namespace unless it is on <see cref="Allowlist"/>, and the allowlist
/// may only shrink — an entry whose owner no longer violates fails the build until it is removed.
/// </summary>
internal static class KernelReferenceRatchet
{
    public static IReadOnlyList<string> ForbiddenNamespacePrefixes { get; } =
    [
        "GeoAssets.Provider",
        "GeoAssets.Domain",
        "GeoAssets.IpBilling",
        "GeoAssets.NeutralOperator.Domain",
        "GeoAssets.Core.Bdgd",
        "GeoAssets.Workflow.Contractors",
        "GeoAssets.Workflow.Erp",
        "GeoAssets.Workflow.FieldWork",
        "GeoAssets.Workflow.Infrastructure",
        "GeoAssets.NeutralOperator.Application",
        "GeoAssets.NeutralOperator.Application.Occupancy",
        "GeoAssets.Workflow.Scada",
        "GeoAssets.Workflow.SlaGuard",
    ];

    /// <summary>
    /// The ADR-001 Phase 1 quarantine (<c>apps/GeoAssets.Server/Modules/</c>): vertical modules hosted in the Server
    /// assembly until Phase 7 lifts them out. Types in it are not kernel types, so moving an offender into a module
    /// removes its allowlist entry instead of renaming it.
    /// </summary>
    public const string ModuleQuarantineNamespace = "GeoAssets.Server.Modules";

    /// <summary>
    /// Today's offenders, keyed by top-level type full name, each annotated with the epic that removes it from the
    /// kernel (by deleting it, moving it into the module quarantine, or replacing the reference with a port).
    /// Remove an entry in the same change that eliminates its violation. Never add one.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Allowlist { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program"] = "XD01-345 (P1) — XD01-358/XD01-359 reduce Program.cs to kernel wiring + module bootstrap",
            ["GeoAssets.Server.GeoAssetsRestApiExtensions"] = "XD01-345 (P1) — NeutralOperator GIS export route (NeutralOperator.Domain.GisExport); not yet in any child ticket's scope",
        };

    public static bool IsKernelType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return IsKernelNamespace(type.Namespace);
    }

    public static bool IsKernelNamespace(string? ns) => !IsInNamespace(ns, ModuleQuarantineNamespace);

    public static bool IsForbidden(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return ForbiddenNamespacePrefixes.Any(prefix => IsInNamespace(type.Namespace, prefix));
    }

    private static bool IsInNamespace(string? ns, string prefix) =>
        ns is not null
        && (string.Equals(ns, prefix, StringComparison.Ordinal)
            || ns.StartsWith(prefix + ".", StringComparison.Ordinal));

    public static KernelRatchetResult Evaluate(
        IReadOnlyDictionary<string, IReadOnlySet<string>> violations,
        IReadOnlyDictionary<string, string> allowlist)
    {
        ArgumentNullException.ThrowIfNull(violations);
        ArgumentNullException.ThrowIfNull(allowlist);

        var newOffenders = violations.Keys
            .Where(owner => !allowlist.ContainsKey(owner))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var stale = allowlist.Keys
            .Where(owner => !violations.ContainsKey(owner))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new KernelRatchetResult(newOffenders, stale);
    }
}
