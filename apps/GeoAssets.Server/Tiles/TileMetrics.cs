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
///   <item><c>geoassets.tiles.versions.peer_changes</c> — another replica's changes of the shared tile versions applied here
///   (XD01-458); tag <c>outcome</c> (<see cref="Evicted"/>: its dirty tiles evicted | <see cref="Flushed"/> | <see cref="Resynchronized"/>:
///   announced incomplete, so the cache was emptied and the versions re-read).</item>
///   <item><c>geoassets.tiles.versions.reconnects</c> — subscriptions to the shared tile versions lost and retried (XD01-458);
///   each is followed, once subscribed again, by an emptied cache and the versions re-read.</item>
///   <item><c>geoassets.tiles.lod_calibration.runs</c> / <c>.duration</c> (ms) — <c>GET /tiles/lod-calibration</c> requests
///   (XD01-439); tag <c>outcome</c> (<see cref="Completed"/>|<see cref="Cancelled"/>|<see cref="TimedOut"/>|<see cref="Failed"/>).</item>
///   <item><c>geoassets.tiles.lod_calibration.recommendations</c> — asset types calibrated; tag <c>recommendation</c>
///   (<see cref="Changed"/>|<see cref="Unchanged"/>|<see cref="NoRecommendation"/>). The thresholds themselves, per asset type,
///   are logged, not tagged.</item>
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

    public const string PeerChangesName = "geoassets.tiles.versions.peer_changes";

    public const string ReconnectsName = "geoassets.tiles.versions.reconnects";

    public const string Resynchronized = "resynchronized";

    public const string CalibrationRunsName = "geoassets.tiles.lod_calibration.runs";

    public const string CalibrationDurationName = "geoassets.tiles.lod_calibration.duration";

    public const string CalibrationRecommendationsName = "geoassets.tiles.lod_calibration.recommendations";

    public const string Completed = "completed";

    public const string Cancelled = "cancelled";

    public const string TimedOut = "timed_out";

    public const string Failed = "failed";

    /// <summary>A recommendation that would change the tiers the asset type is drawn as.</summary>
    public const string Changed = "changed";

    /// <summary>A recommendation that keeps the tiers the asset type is drawn as.</summary>
    public const string Unchanged = "unchanged";

    /// <summary>No recommendation: nothing in scope to measure.</summary>
    public const string NoRecommendation = "none";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        RequestsName, unit: "{tiles}", description: "Tile responses (tags: format=mvt|png, cache=hit|miss|uncached).");

    private static readonly Histogram<double> RenderDuration = Meter.CreateHistogram<double>(
        RenderDurationName, unit: "ms", description: "Time a tile source took to draw a tile the cache did not hold (tag: format).");

    private static readonly Histogram<long> Size = Meter.CreateHistogram<long>(
        SizeName, unit: "By", description: "Bytes of each tile body sent, as encoded on the wire (tag: format).");

    private static readonly Counter<long> Invalidations = Meter.CreateCounter<long>(
        InvalidationsName, unit: "{writes}", description: "Committed writes whose tiles were invalidated (tag: outcome=evicted|flushed).");

    private static readonly Counter<long> PeerChanges = Meter.CreateCounter<long>(
        PeerChangesName, unit: "{changes}", description: "Other replicas' tile version changes applied (tag: outcome=evicted|flushed|resynchronized).");

    private static readonly Counter<long> Reconnects = Meter.CreateCounter<long>(
        ReconnectsName, unit: "{reconnects}", description: "Subscriptions to the shared tile versions lost and retried.");

    private static readonly Counter<long> CalibrationRuns = Meter.CreateCounter<long>(
        CalibrationRunsName, unit: "{runs}", description: "LOD calibration requests (tag: outcome=completed|cancelled|timed_out|failed).");

    private static readonly Histogram<double> CalibrationDuration = Meter.CreateHistogram<double>(
        CalibrationDurationName, unit: "ms", description: "Time a LOD calibration request took (tag: outcome).");

    private static readonly Counter<long> CalibrationRecommendations = Meter.CreateCounter<long>(
        CalibrationRecommendationsName, unit: "{asset_types}", description: "Asset types calibrated (tag: recommendation=changed|unchanged|none).");

    internal static void Served(string format, string cache, long bytes)
    {
        Requests.Add(1, new KeyValuePair<string, object?>("format", format), new KeyValuePair<string, object?>("cache", cache));
        Size.Record(bytes, new KeyValuePair<string, object?>("format", format));
    }

    internal static void Rendered(string format, double milliseconds) =>
        RenderDuration.Record(milliseconds, new KeyValuePair<string, object?>("format", format));

    internal static void Invalidated(string outcome) =>
        Invalidations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    internal static void PeerChanged(string outcome) =>
        PeerChanges.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    internal static void Reconnected() => Reconnects.Add(1);

    internal static void Calibrated(string outcome, double milliseconds)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        CalibrationRuns.Add(1, tag);
        CalibrationDuration.Record(milliseconds, tag);
    }

    internal static void Recommended(string recommendation) =>
        CalibrationRecommendations.Add(1, new KeyValuePair<string, object?>("recommendation", recommendation));
}
