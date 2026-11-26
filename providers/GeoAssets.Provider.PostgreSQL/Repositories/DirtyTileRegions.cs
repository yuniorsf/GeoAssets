using GeoAssets.Core.Rendering;
using NetTopologySuite.Geometries;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The <see cref="DirtyTileRegion"/>s of one write (XD01-432): one per distinct longitude/latitude box a written feature
/// had before or has after the write, at every zoom; a write of more than <see cref="MaxRegionsPerWrite"/> distinct boxes
/// (a bulk import, a catalog load) is one region of their union instead, so the invalidation stays bounded. A feature
/// without a geometry, an empty one or a box with a non-finite coordinate draws nothing and adds none.
/// </summary>
internal static class DirtyTileRegions
{
    public const int MaxRegionsPerWrite = 256;

    /// <summary>The box of <paramref name="envelope"/>, or <c>null</c> when it draws nothing.</summary>
    public static BoundingBox? BoundsOf(Envelope? envelope) =>
        envelope is null || envelope.IsNull ? null : BoundsOf(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);

    /// <summary>The box from (<paramref name="minLon"/>, <paramref name="minLat"/>) to (<paramref name="maxLon"/>, <paramref name="maxLat"/>), or <c>null</c> when it is not a finite, ordered box.</summary>
    public static BoundingBox? BoundsOf(double minLon, double minLat, double maxLon, double maxLat) =>
        double.IsFinite(minLon) && double.IsFinite(minLat) && double.IsFinite(maxLon) && double.IsFinite(maxLat)
            && minLon <= maxLon && minLat <= maxLat
            ? new BoundingBox(minLon, minLat, maxLon, maxLat)
            : null;

    /// <summary>The regions of <paramref name="bounds"/>, first seen first.</summary>
    public static IReadOnlyList<DirtyTileRegion> Of(IEnumerable<BoundingBox?> bounds)
    {
        var seen = new HashSet<BoundingBox>();
        var distinct = new List<BoundingBox>();
        foreach (var box in bounds)
        {
            if (box is { } b && seen.Add(b))
                distinct.Add(b);
        }

        if (distinct.Count <= MaxRegionsPerWrite)
            return [.. distinct.Select(b => new DirtyTileRegion(b))];

        var union = new BoundingBox(
            distinct.Min(b => b.MinLon), distinct.Min(b => b.MinLat), distinct.Max(b => b.MaxLon), distinct.Max(b => b.MaxLat));
        return [new DirtyTileRegion(union)];
    }
}
