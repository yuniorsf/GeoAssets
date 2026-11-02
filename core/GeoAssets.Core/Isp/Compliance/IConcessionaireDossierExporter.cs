using GeoAssets.Core.Models;

namespace GeoAssets.Core.Isp.Compliance;

/// <summary>
/// A pluggable per-distributor pole-sharing compliance dossier export template (XD01-284's AC3:
/// adding a third distributor's format means registering another implementation of this
/// interface, not changing the first two). See <see cref="ConcessionaireDossierExporterRegistry"/>
/// for how a distributor name resolves to its exporter.
/// </summary>
public interface IConcessionaireDossierExporter
{
    string DistributorName { get; }

    /// <summary>
    /// Builds this distributor's dossier from <paramref name="poleFeatures"/>. Throws
    /// <see cref="InvalidOperationException"/> if none of them are usable (e.g. no
    /// <c>PONNOT</c>-typed feature with a <c>COD_ID</c> attribute).
    /// </summary>
    ConcessionaireDossierResult Export(IReadOnlyList<GeoFeature> poleFeatures);
}
