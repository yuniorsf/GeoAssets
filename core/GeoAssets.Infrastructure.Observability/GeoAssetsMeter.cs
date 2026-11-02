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
///
/// Never add <c>organization.id</c>/tenant id as a tag on any instrument here (XD01-219): a
/// metric series is keyed by its full tag set, so a per-tenant tag multiplies every metric's
/// series count by the tenant count and keeps growing as tenants are onboarded — unlike a span
/// attribute, which costs nothing extra per trace. Per-tenant drill-down goes through
/// <see cref="GeoAssetsActivitySource"/>/<see cref="TelemetryEnrichmentMiddleware"/>'s
/// <c>organization.id</c> span attribute and trace search instead.
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

    /// <summary>ISP rollout KPI #1: FTTH drop-install elapsed time, CrmSale to Activated (XD01-287).</summary>
    private readonly Histogram<double> _ftthDropInstallDurationSeconds;

    /// <summary>ISP rollout KPI #2: optical-budget verification outcomes (tag: outcome) (XD01-287).</summary>
    private readonly Counter<long> _opticalBudgetOutcomes;

    /// <summary>ISP rollout KPI #3: utility-cut/fault outages successfully dispatched (XD01-287).</summary>
    private readonly Counter<long> _utilityCutOutagesDispatched;

    /// <summary>ISP rollout KPI #4: OTDR fault-dispatch processing latency (XD01-287).</summary>
    private readonly Histogram<double> _faultDispatchLatencyMs;

    /// <summary>ISP rollout KPI #5: CTO splitter-port occupancy discrepancy magnitude (XD01-287).</summary>
    private readonly Histogram<double> _ctoOccupancyDiscrepancy;

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

        _ftthDropInstallDurationSeconds = _meter.CreateHistogram<double>(
            "geoassets.isp.ftth_drop_install.duration",
            unit: "s",
            description: "Elapsed time from CrmSale to Activated for a completed FTTH drop-install order.");

        _opticalBudgetOutcomes = _meter.CreateCounter<long>(
            "geoassets.isp.optical_budget.outcomes",
            unit: "{readings}",
            description: "Optical-budget verification outcomes observed while transitioning a FTTH drop into PtoPower (tag: outcome).");

        _utilityCutOutagesDispatched = _meter.CreateCounter<long>(
            "geoassets.isp.utility_cut.outages_dispatched",
            unit: "{outages}",
            description: "OTDR-correlated fault/outage events successfully dispatched to a pole pair.");

        _faultDispatchLatencyMs = _meter.CreateHistogram<double>(
            "geoassets.isp.fault.dispatch_latency",
            unit: "ms",
            description: "Processing latency of OTDR fault correlation/dispatch — a lower bound on real-world MTTR; this codebase has no fault-resolution timestamp to compute true detection-to-repair MTTR from.");

        _ctoOccupancyDiscrepancy = _meter.CreateHistogram<double>(
            "geoassets.isp.cto.occupancy_discrepancy",
            unit: "{ports}",
            description: "Absolute difference between a BSS-reported and GeoAssets-tracked CTO splitter-port occupancy count.");
    }

    // ── Recording helpers ─────────────────────────────────────────────────────

    public void RecordOrderTransition(string orderTypeId, string previousStatus, string newStatus) =>
        _orderTransitions.Add(1,
            new KeyValuePair<string, object?>("order.type", orderTypeId),
            new KeyValuePair<string, object?>("order.prev_status", previousStatus),
            new KeyValuePair<string, object?>("order.new_status", newStatus));

    public void RecordNotificationPublished(string transport) =>
        _notificationsPublished.Add(1,
            new KeyValuePair<string, object?>("transport", transport));

    public void RecordNotificationDuration(double milliseconds, string transport) =>
        _notificationDurationMs.Record(milliseconds,
            new KeyValuePair<string, object?>("transport", transport));

    public void RecordFtthDropInstallDuration(double seconds) =>
        _ftthDropInstallDurationSeconds.Record(seconds);

    public void RecordOpticalBudgetOutcome(string outcome) =>
        _opticalBudgetOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordUtilityCutOutageDispatched() =>
        _utilityCutOutagesDispatched.Add(1);

    public void RecordFaultDispatchLatency(double milliseconds) =>
        _faultDispatchLatencyMs.Record(milliseconds);

    public void RecordCtoOccupancyDiscrepancy(int difference) =>
        _ctoOccupancyDiscrepancy.Record(Math.Abs(difference));

    public void Dispose() => _meter.Dispose();
}
