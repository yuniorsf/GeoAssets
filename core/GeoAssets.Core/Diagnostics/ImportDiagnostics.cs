using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GeoAssets.Core.Diagnostics;

/// <summary>
/// Lightweight telemetry primitives for the GeoJSON import pipeline.
///
/// Defined in <c>GeoAssets.Core</c> (pure BCL — no NuGet packages required)
/// so it is reachable from both the Blazor WASM Shared RCL and the
/// server-side <c>GeoAssets.Infrastructure.Observability</c> project.
///
/// When no OpenTelemetry listener is registered (e.g. Blazor WASM without
/// an exporter), <see cref="ActivitySource"/> returns <c>null</c> activities
/// and all metric recordings are no-ops — zero runtime overhead.
/// </summary>
public static class ImportDiagnostics
{
    // ── Source / meter names (registered in ObservabilityServiceExtensions) ──

    public const string ActivitySourceName = "GeoAssets.Import";
    public const string MeterName          = "GeoAssets.Import";

    // ── Activity source ───────────────────────────────────────────────────────

    /// <summary>
    /// Source for the import pipeline spans.
    /// Registered in OTel with <c>.AddSource(ImportDiagnostics.ActivitySourceName)</c>.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0");

    // ── Metrics ───────────────────────────────────────────────────────────────

    private static readonly Meter _meter = new(MeterName, "1.0");

    /// <summary>Total import pipeline duration (start → map rendered), in milliseconds.</summary>
    public static readonly Histogram<double> ImportDurationMs =
        _meter.CreateHistogram<double>(
            "geoassets.import.duration_ms", "ms",
            "End-to-end import pipeline duration.");

    /// <summary>Duration of the <c>AssetService.ImportAsync</c> call, in milliseconds.</summary>
    public static readonly Histogram<double> ParseDurationMs =
        _meter.CreateHistogram<double>(
            "geoassets.import.parse_duration_ms", "ms",
            "Duration of GeoJSON parse + repository merge (AssetService.ImportAsync).");

    /// <summary>Duration of the <c>Repository.GetAll</c> call, in milliseconds.</summary>
    public static readonly Histogram<double> GetAllDurationMs =
        _meter.CreateHistogram<double>(
            "geoassets.repository.getall_duration_ms", "ms",
            "Duration of IAssetProvider.GetAll.");

    /// <summary>Duration of the <c>MapInterop.RenderAllFeaturesAsync</c> JS interop call, in milliseconds.</summary>
    public static readonly Histogram<double> RenderDurationMs =
        _meter.CreateHistogram<double>(
            "geoassets.map.render_duration_ms", "ms",
            "Duration of MapInterop.RenderAllFeaturesAsync (JS interop).");

    /// <summary>Size of GeoJSON payloads at import time, in bytes.</summary>
    public static readonly Histogram<long> PayloadBytes =
        _meter.CreateHistogram<long>(
            "geoassets.import.payload_bytes", "By",
            "GeoJSON import payload size.");

    /// <summary>Total number of import operations (tags: <c>outcome=success|failure</c>).</summary>
    public static readonly Counter<long> ImportCount =
        _meter.CreateCounter<long>(
            "geoassets.import.count", "{imports}",
            "Total GeoJSON import operations.");

    /// <summary>Cumulative count of features imported across all operations.</summary>
    public static readonly Counter<long> FeatureImportCount =
        _meter.CreateCounter<long>(
            "geoassets.import.feature_count", "{features}",
            "Cumulative features merged into the repository via import.");
}
