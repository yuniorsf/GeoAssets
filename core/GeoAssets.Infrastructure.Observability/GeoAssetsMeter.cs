using System.Diagnostics.Metrics;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// Application-level metrics for GeoAssets, exported to Azure Monitor as
/// custom metrics visible in Metrics Explorer and usable in Alerts.
///
/// Inject <see cref="GeoAssetsMeter"/> where needed:
/// <code>
/// public class ServiceOrderCommandHandler(GeoAssetsMeter metrics, …)
/// {
///     public async Task TransitionAsync(…)
///     {
///         metrics.RecordOrderTransition(orderTypeId, previous, next);
///     }
/// }
/// </code>
/// </summary>
public sealed class GeoAssetsMeter : IDisposable
{
    public const string MeterName = "GeoAssets";

    private readonly Meter _meter;

    // ── Counters ─────────────────────────────────────────────────────────────

    /// <summary>Total number of service order state transitions since startup.</summary>
    private readonly Counter<long> _orderTransitions;

    /// <summary>Total GeoJSON import operations (success + failure).</summary>
    private readonly Counter<long> _importOperations;

    /// <summary>Total number of notification publish attempts.</summary>
    private readonly Counter<long> _notificationsPublished;

    // ── Histograms ────────────────────────────────────────────────────────────

    /// <summary>Size in bytes of imported GeoJSON payloads.</summary>
    private readonly Histogram<long> _importPayloadBytes;

    /// <summary>Duration of notification publish calls in milliseconds.</summary>
    private readonly Histogram<double> _notificationDurationMs;

    // ── Gauges ────────────────────────────────────────────────────────────────

    /// <summary>Current count of features held in the in-memory repository.</summary>
    private readonly ObservableGauge<int> _featureCount;

    private int _currentFeatureCount;

    public GeoAssetsMeter(string version)
    {
        _meter = new Meter(MeterName, version);

        _orderTransitions = _meter.CreateCounter<long>(
            "geoassets.orders.transitions",
            unit: "{transitions}",
            description: "Total service order state transitions.");

        _importOperations = _meter.CreateCounter<long>(
            "geoassets.import.operations",
            unit: "{operations}",
            description: "GeoJSON import operations (tag: outcome=success|failure).");

        _notificationsPublished = _meter.CreateCounter<long>(
            "geoassets.notifications.published",
            unit: "{messages}",
            description: "Notification messages published (tag: transport=servicebus|kafka|null).");

        _importPayloadBytes = _meter.CreateHistogram<long>(
            "geoassets.import.payload_bytes",
            unit: "By",
            description: "Size of imported GeoJSON payloads.");

        _notificationDurationMs = _meter.CreateHistogram<double>(
            "geoassets.notifications.duration",
            unit: "ms",
            description: "End-to-end duration of a notification publish call.");

        _featureCount = _meter.CreateObservableGauge(
            "geoassets.repository.feature_count",
            () => _currentFeatureCount,
            unit: "{features}",
            description: "Current number of features in the in-memory repository.");
    }

    // ── Recording helpers ─────────────────────────────────────────────────────

    public void RecordOrderTransition(string orderTypeId, string previousStatus, string newStatus) =>
        _orderTransitions.Add(1,
            new KeyValuePair<string, object?>("order.type",        orderTypeId),
            new KeyValuePair<string, object?>("order.prev_status", previousStatus),
            new KeyValuePair<string, object?>("order.new_status",  newStatus));

    public void RecordImport(bool success, long payloadBytes, int featureCount) =>
        _importOperations.Add(1,
            new KeyValuePair<string, object?>("outcome",        success ? "success" : "failure"),
            new KeyValuePair<string, object?>("feature_count",  featureCount));

    public void RecordImportPayload(long bytes) =>
        _importPayloadBytes.Record(bytes);

    public void RecordNotificationPublished(string transport) =>
        _notificationsPublished.Add(1,
            new KeyValuePair<string, object?>("transport", transport));

    public void RecordNotificationDuration(double milliseconds, string transport) =>
        _notificationDurationMs.Record(milliseconds,
            new KeyValuePair<string, object?>("transport", transport));

    /// <summary>Update the observable gauge value from the repository on each collection-changed event.</summary>
    public void UpdateFeatureCount(int count) =>
        _currentFeatureCount = count;

    public void Dispose() => _meter.Dispose();
}
