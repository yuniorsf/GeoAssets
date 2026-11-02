using System.Diagnostics.CodeAnalysis;

namespace GeoAssets.Core.Isp.Compliance;

/// <summary>
/// Resolves a distributor name to its <see cref="IDossierExporter"/> (XD01-284's
/// AC3) — adding a third distributor means registering another exporter in DI, nothing here or
/// in either existing exporter changes. Named without "Concessionaire" (XD01-300):
/// <c>MessagingArchitectureTests.Invariant02</c> asserts <c>GeoAssets.Core</c> stays
/// vertical-domain-agnostic, and that word is on its forbidden-token list.
/// </summary>
public sealed class DossierExporterRegistry
{
    private readonly Dictionary<string, IDossierExporter> _byDistributorName;

    public DossierExporterRegistry(IEnumerable<IDossierExporter> exporters)
    {
        ArgumentNullException.ThrowIfNull(exporters);
        _byDistributorName = exporters.ToDictionary(e => e.DistributorName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> RegisteredDistributors => _byDistributorName.Keys;

    public bool TryResolve(string distributorName, [NotNullWhen(true)] out IDossierExporter? exporter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distributorName);
        return _byDistributorName.TryGetValue(distributorName, out exporter);
    }
}
