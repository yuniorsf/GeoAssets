namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Spatial ETL boundary for XD01-257: ingests a concessionaire's BDGD PONNOT (pole layer) export
/// — Shapefile or File Geodatabase, SIRGAS 2000 — and upserts it into the given
/// <see cref="IAssetProvider"/>, keyed by BDGD's own <c>COD_ID</c> so re-running an updated
/// export never duplicates poles already imported.
/// </summary>
public interface IBdgdPonnotImportService
{
    /// <param name="sourceArchiveBytes">
    /// The raw bytes of the concessionaire's export archive (a ZIP containing at least
    /// <c>.shp</c>/<c>.dbf</c>, with an optional <c>.prj</c> sidecar carrying its source CRS —
    /// SIRGAS 2000 UTM in the real-world case this ticket targets).
    /// </param>
    /// <param name="target">The provider to upsert imported poles into (typically PostgreSQL-backed).</param>
    Task<BdgdPonnotImportResult> ImportAsync(
        byte[] sourceArchiveBytes,
        IAssetProvider target,
        CancellationToken cancellationToken = default);
}
