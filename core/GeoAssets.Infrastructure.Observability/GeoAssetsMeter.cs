using System.Diagnostics.Metrics;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// Application-level metrics for GeoAssets, exported via OpenTelemetry's
/// OTLP metrics exporter to the configured backend (New Relic as of XD01-30).
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

    /// <summary>Total number of notification publish attempts.</summary>
    private readonly Counter<long> _notificationsPublished;

    // ── Histograms ────────────────────────────────────────────────────────────

    /// <summary>Duration of notification publish calls in milliseconds.</summary>
    private readonly Histogram<double> _notificationDurationMs;

    public GeoAssetsMeter(string version)
    {
        _meter = new Meter(MeterName, version);

        _orderTransitions = _meter.CreateCounter<long>(
            "geoassets.orders.transitions",
            unit: "{transitions}",
            description: "Total service order state transitions.");

        _notificationsPublished = _meter.CreateCounter<long>(
            "geoassets.notifications.published",
            unit: "{messages}",
            description: "Notification messages published (tag: transport=servicebus|kafka|null).");

        _notificationDurationMs = _meter.CreateHistogram<double>(
            "geoassets.notifications.duration",
            unit: "ms",
            description: "End-to-end duration of a notification publish call.");
    }

    // ── Recording helpers ─────────────────────────────────────────────────────

    public void RecordOrderTransition(string orderTypeId, string previousStatus, string newStatus) =>
        _orderTransitions.Add(1,
            new KeyValuePair<string, object?>("order.type",        orderTypeId),
            new KeyValuePair<string, object?>("order.prev_status", previousStatus),
            new KeyValuePair<string, object?>("order.new_status",  newStatus));

    public void RecordNotificationPublished(string transport) =>
        _notificationsPublished.Add(1,
            new KeyValuePair<string, object?>("transport", transport));

    public void RecordNotificationDuration(double milliseconds, string transport) =>
        _notificationDurationMs.Record(milliseconds,
            new KeyValuePair<string, object?>("transport", transport));

    public void Dispose() => _meter.Dispose();
}
