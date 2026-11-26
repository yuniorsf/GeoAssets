namespace GeoAssets.Bench.Render;

/// <summary>
/// The viewports measured at each zoom: centers drawn from the dataset's own density (users look where the assets are),
/// from a sequence seeded by the dataset seed and the zoom, so every run measures the same viewports.
/// </summary>
internal static class ViewportPlan
{
    /// <summary>ADR-002 Track 0's standard zooms: overview, mid-zoom, street/edit zoom.</summary>
    public static readonly IReadOnlyList<int> StandardZooms = [8, 12, 16];

    /// <summary>The map area of a 1920×1080 browser (the gateway load profile's, docs/perf/gateway-rate-limits.md).</summary>
    public const int DefaultWidthPx = 1580;
    public const int DefaultHeightPx = 1020;

    public static IReadOnlyList<MapViewport> For(DatasetLayout layout, ulong seed, int zoom, int count, int widthPx, int heightPx)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var rng = new SplitMix64(seed ^ (0x5EED_0000UL + (ulong)zoom));
        var viewports = new List<MapViewport>(count);
        for (var i = 0; i < count; i++)
        {
            var (lon, lat) = layout.SamplePosition(rng);
            viewports.Add(new MapViewport(lon, lat, zoom, widthPx, heightPx));
        }

        return viewports;
    }
}
