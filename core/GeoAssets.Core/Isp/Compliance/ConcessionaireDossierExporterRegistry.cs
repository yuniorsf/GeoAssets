using System.Diagnostics.CodeAnalysis;

namespace GeoAssets.Core.Isp.Compliance;

/// <summary>
/// Resolves a distributor name to its <see cref="IConcessionaireDossierExporter"/> (XD01-284's
/// AC3) — adding a third distributor means registering another exporter in DI, nothing here or
/// in either existing exporter changes.
/// </summary>
public sealed class ConcessionaireDossierExporterRegistry
{
    private readonly Dictionary<string, IConcessionaireDossierExporter> _byDistributorName;

    public ConcessionaireDossierExporterRegistry(IEnumerable<IConcessionaireDossierExporter> exporters)
    {
        ArgumentNullException.ThrowIfNull(exporters);
        _byDistributorName = exporters.ToDictionary(e => e.DistributorName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> RegisteredDistributors => _byDistributorName.Keys;

    public bool TryResolve(string distributorName, [NotNullWhen(true)] out IConcessionaireDossierExporter? exporter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distributorName);
        return _byDistributorName.TryGetValue(distributorName, out exporter);
    }
}
