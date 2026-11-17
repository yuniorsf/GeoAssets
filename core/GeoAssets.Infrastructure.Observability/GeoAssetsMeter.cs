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

    /// <summary>The <c>geoassets.orders.targets.*</c> instruments (XD01-308).</summary>
    private readonly TargetSelectionInstruments _targetSelection;

    private readonly FanOutInstruments _fanOut;

    /// <summary>The <c>geoassets.isp.pole_occupancy.*</c> commit instruments (XD01-318).</summary>
    private readonly OccupancyCommitInstruments _occupancyCommit;

    /// <summary>The <c>geoassets.catalog.mutation_batch*</c> instruments (XD01-337).</summary>
    private readonly AssetMutationBatchInstruments _assetMutationBatch;

    public GeoAssetsMeter(string version)
    {
        _meter = new Meter(MeterName, version);

        _targetSelection = new TargetSelectionInstruments(_meter);
        _fanOut = new FanOutInstruments(_meter);
        _occupancyCommit = new OccupancyCommitInstruments(_meter);
        _assetMutationBatch = new AssetMutationBatchInstruments(_meter);

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

    /// <summary>
    /// Records one target-selection run. Tags are low-cardinality by construction: callers pass
    /// a registered strategy id (or a fixed placeholder), never a client-supplied string, and
    /// never an organization, order or feature id.
    /// </summary>
    public void RecordTargetSelectionDuration(double milliseconds, string strategy, string mode, string outcome) =>
        _targetSelection.DurationMs.Record(milliseconds,
            new KeyValuePair<string, object?>("strategy", strategy),
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordTargetSelectionMatched(int matched, string strategy) =>
        _targetSelection.Matched.Record(matched,
            new KeyValuePair<string, object?>("strategy", strategy));

    public void RecordTargetSelectionTruncated(string strategy) =>
        _targetSelection.Truncated.Add(1,
            new KeyValuePair<string, object?>("strategy", strategy));

    public void RecordTargetConflicts(int targets, string level, string orderTypeId)
    {
        if (targets <= 0) return;

        _targetSelection.Conflicts.Add(targets,
            new KeyValuePair<string, object?>("level", level),
            new KeyValuePair<string, object?>("order.type", orderTypeId));
    }

    /// <summary>Records how long one fan-out dispatch took (XD01-316), by outcome.</summary>
    public void RecordFanOutDuration(double milliseconds, string outcome) =>
        _fanOut.DurationMs.Record(milliseconds,
            new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Counts the child orders one fan-out dispatch created (XD01-316). Never tagged with an order or organization id.</summary>
    public void RecordFanOutChildrenSpawned(int children, string orderTypeId)
    {
        if (children <= 0) return;

        _fanOut.ChildrenSpawned.Add(children,
            new KeyValuePair<string, object?>("order.type", orderTypeId));
    }

    /// <summary>Records how long one occupancy approve-and-commit took (XD01-318), by outcome.</summary>
    public void RecordOccupancyCommitDuration(double milliseconds, string outcome) =>
        _occupancyCommit.DurationMs.Record(milliseconds,
            new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Counts the occupancy changes of one kind an approval committed (XD01-318). Never tagged with an order, pole or organization id.</summary>
    public void RecordOccupancyChangesCommitted(int changes, string kind)
    {
        if (changes <= 0) return;

        _occupancyCommit.ChangesCommitted.Add(changes,
            new KeyValuePair<string, object?>("kind", kind));
    }

    /// <summary>Counts one approval refused because a pole or a staged set changed after staging (XD01-318).</summary>
    public void RecordOccupancyCommitConflict() => _occupancyCommit.Conflicts.Add(1);

    /// <summary>
    /// Records one attempt to apply an asset mutation batch to the catalog (XD01-337): counted and
    /// timed by outcome. Never tagged with a batch, organization or feature id.
    /// </summary>
    public void RecordAssetMutationBatch(double milliseconds, string outcome)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        _assetMutationBatch.Batches.Add(1, tag);
        _assetMutationBatch.DurationMs.Record(milliseconds, tag);
    }

    public void Dispose() => _meter.Dispose();
}
