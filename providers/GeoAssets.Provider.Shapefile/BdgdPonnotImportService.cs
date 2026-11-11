using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Interfaces;

namespace GeoAssets.Provider.Shapefile;

/// <summary>
/// <see cref="IBdgdPonnotImportService"/> implementation (XD01-257) — reuses
/// <see cref="ShapefileParser"/> for the generic archive/geometry/attribute/CRS-reprojection work
/// (it already reprojects any <c>.prj</c>-declared source CRS, including SIRGAS 2000 UTM, to the
/// WGS84 this codebase stores everything in), then maps each raw feature through
/// <see cref="BdgdPonnotFeatureMapper"/> before upserting it into <paramref name="target"/>.
/// </summary>
public sealed class BdgdPonnotImportService : IBdgdPonnotImportService
{
    public Task<BdgdPonnotImportResult> ImportAsync(
        byte[] sourceArchiveBytes,
        IAssetProvider target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceArchiveBytes);
        ArgumentNullException.ThrowIfNull(target);

        var rawFeatures = ShapefileParser.ParseZipBytes(sourceArchiveBytes);
        var existingIdByCodId = IndexExistingByCodId(target);

        int inserted = 0, updated = 0, skipped = 0;
        var warnings = new List<string>();

        foreach (var raw in rawFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (mapped, reason) = BdgdPonnotFeatureMapper.TryMap(raw);
            if (mapped is null)
            {
                skipped++;
                if (reason is not null) warnings.Add(reason);
                continue;
            }

            var codId = mapped.Properties.CustomAttributes["COD_ID"];
            if (existingIdByCodId.TryGetValue(codId, out var existingId))
            {
                mapped.Id = existingId;
                target.Update(mapped);
                updated++;
            }
            else
            {
                target.Add(mapped);
                inserted++;
            }
        }

        return Task.FromResult(new BdgdPonnotImportResult(inserted, updated, skipped, warnings));
    }

    private static Dictionary<string, string> IndexExistingByCodId(IAssetProvider target) =>
        target.GetByAssetType(BdgdAssetTypeIds.Ponnot.ToString())
            .Where(f => f.Properties.CustomAttributes.ContainsKey("COD_ID"))
            .ToDictionary(
                f => f.Properties.CustomAttributes["COD_ID"],
                f => f.Id,
                StringComparer.OrdinalIgnoreCase);
}
