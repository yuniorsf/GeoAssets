using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The asset types' zoom ranges (<see cref="AssetType.MinZoom"/>/<see cref="AssetType.MaxZoom"/>) in SQL (XD01-430), for
/// the viewport and render reads: one small query for the types a zoom hides, then a cheap
/// <c>NOT ("AssetTypeId" = ANY(@hidden))</c> on the features — evaluated before <c>ST_Intersects</c>, so a hidden type's
/// geometry is never read. Same rule as <see cref="AssetTypeZoomRange.TypeIdsHiddenAt"/>: bounds inclusive, an open bound
/// excludes nothing, a feature whose type is not a stored one is drawn.
/// </summary>
internal static class ZoomRangeQueries
{
    /// <summary>The ids, as features name their type, of the asset types not drawn at <paramref name="zoom"/>; none without a zoom.</summary>
    public static async Task<string[]> HiddenAssetTypeIdsAsync(GeoAssetsDbContext db, int? zoom, CancellationToken ct)
    {
        if (zoom is not { } z) return [];

        var ids = await db.AssetTypes
            .AsNoTracking()
            .Where(t => t.MinZoom > z || t.MaxZoom < z)
            .Select(t => t.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. ids.Select(id => id.ToString())];
    }

    /// <summary><paramref name="rows"/> without those of the <paramref name="hiddenAssetTypeIds"/>.</summary>
    public static IQueryable<GeoEntityRow> WithoutAssetTypes(this IQueryable<GeoEntityRow> rows, string[] hiddenAssetTypeIds) =>
        hiddenAssetTypeIds.Length == 0 ? rows : rows.Where(e => !hiddenAssetTypeIds.Contains(e.AssetTypeId));
}
