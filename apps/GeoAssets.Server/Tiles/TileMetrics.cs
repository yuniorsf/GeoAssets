using System.Diagnostics.Metrics;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The tile endpoints' instruments (XD01-434), on the domain metrics' <c>GeoAssets</c> meter the host's OpenTelemetry
/// pipeline exports. Tags never carry an organization, a layer or a tile address (XD01-219: bounded series).
/// <list type="bullet">
///   <item><c>geoassets.tiles.requests</c> — tile responses; tags <c>format</c> (<c>mvt</c>|<c>png</c>) and <c>cache</c>
///   (<see cref="Hit"/>|<see cref="Miss"/>|<see cref="Uncached"/>; a miss drew the tile and kept it, an uncached response
///   drew it above the cached zooms, for a stale <c>{v}</c>, or with no layer to draw).</item>
///   <item><c>geoassets.tiles.render.duration</c> — milliseconds a tile source took to draw a missed tile (a raster
///   metatile counts once); tag <c>format</c>.</item>
///   <item><c>geoassets.tiles.size</c> — bytes of each tile body sent (as encoded on the wire); tag <c>format</c>.</item>
///   <item><c>geoassets.tiles.cache.invalidations</c> — committed writes invalidated; tag <c>outcome</c>
///   (<see cref="Evicted"/>|<see cref="Flushed"/>).</item>
/// </list>
/// </summary>
public static class TileMetrics
{
    public const string MeterName = "GeoAssets";

    public const string RequestsName = "geoassets.tiles.requests";

    public const string RenderDurationName = "geoassets.tiles.render.duration";

    public const string SizeName = "geoassets.tiles.size";

    public const string InvalidationsName = "geoassets.tiles.cache.invalidations";

    public const string Hit = "hit";

    public const string Miss = "miss";

    public const string Uncached = "uncached";

    public const string Evicted = "evicted";

    public const string Flushed = "flushed";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        RequestsName, unit: "{tiles}", description: "Tile responses (tags: format=mvt|png, cache=hit|miss|uncached).");

    private static readonly Histogram<double> RenderDuration = Meter.CreateHistogram<double>(
        RenderDurationName, unit: "ms", description: "Time a tile source took to draw a tile the cache did not hold (tag: format).");

    private static readonly Histogram<long> Size = Meter.CreateHistogram<long>(
        SizeName, unit: "By", description: "Bytes of each tile body sent, as encoded on the wire (tag: format).");

    private static readonly Counter<long> Invalidations = Meter.CreateCounter<long>(
        InvalidationsName, unit: "{writes}", description: "Committed writes whose tiles were invalidated (tag: outcome=evicted|flushed).");

    internal static void Served(string format, string cache, long bytes)
    {
        Requests.Add(1, new KeyValuePair<string, object?>("format", format), new KeyValuePair<string, object?>("cache", cache));
        Size.Record(bytes, new KeyValuePair<string, object?>("format", format));
    }

    internal static void Rendered(string format, double milliseconds) =>
        RenderDuration.Record(milliseconds, new KeyValuePair<string, object?>("format", format));

    internal static void Invalidated(string outcome) =>
        Invalidations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
