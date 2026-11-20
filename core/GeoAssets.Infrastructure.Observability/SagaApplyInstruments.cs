using System.Diagnostics.Metrics;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// The asset-mutation apply saga instruments of <see cref="GeoAssetsMeter"/> (XD01-383), created on that meter: how
/// many approvals wait for the kernel to apply their batch, how old the oldest is, how many outlived the saga timeout,
/// and how each one ended. Vertical-agnostic and low-cardinality: the tags are <c>outcome</c>, from a fixed set, and the
/// compensation conflicts' <c>order.type</c>; none is ever tagged with a batch, order, organization or feature id.
/// </summary>
internal sealed class SagaApplyInstruments
{
    private long _pending;
    private double _oldestPendingAgeSeconds;

    public SagaApplyInstruments(Meter meter)
    {
        Timeouts = meter.CreateCounter<long>(
            "geoassets.saga.apply.timeouts",
            unit: "{sagas}",
            description: "Asset-mutation apply sagas found still pending after the saga timeout (each counted once).");

        Outcomes = meter.CreateCounter<long>(
            "geoassets.saga.apply.outcomes",
            unit: "{sagas}",
            description: "Asset-mutation apply saga replies handled (tag: outcome=committed|reverted|duplicate|applied-after-revert|ignored).");

        CompensationConflicts = meter.CreateCounter<long>(
            "geoassets.saga.apply.compensation_conflicts",
            unit: "{sagas}",
            description: "Reverted asset-mutation apply sagas whose compensation left targets untouched because a later change had moved them on (tag: order.type).");

        Duration = meter.CreateHistogram<double>(
            "geoassets.saga.apply.duration",
            unit: "s",
            description: "Time from an approval staging its asset-mutation batch to the saga's end (tag: outcome=committed|reverted).");

        meter.CreateObservableGauge(
            "geoassets.saga.apply.pending",
            () => Volatile.Read(ref _pending),
            unit: "{sagas}",
            description: "Asset-mutation apply sagas waiting for the kernel's reply, as last measured by the timeout sweeper.");

        meter.CreateObservableGauge(
            "geoassets.saga.apply.oldest_pending_age",
            () => Volatile.Read(ref _oldestPendingAgeSeconds),
            unit: "s",
            description: "Age of the oldest asset-mutation apply saga still waiting for the kernel's reply, as last measured by the timeout sweeper (0 when none).");
    }

    /// <summary>Sagas found pending past the timeout.</summary>
    public Counter<long> Timeouts { get; }

    /// <summary>Saga replies handled (tag: outcome).</summary>
    public Counter<long> Outcomes { get; }

    /// <summary>Reverted sagas whose compensation could not restore every target (tag: order.type).</summary>
    public Counter<long> CompensationConflicts { get; }

    /// <summary>Saga duration, staged to settled (tag: outcome).</summary>
    public Histogram<double> Duration { get; }

    /// <summary>Records the backlog the gauges report.</summary>
    public void SetBacklog(long pending, double oldestPendingAgeSeconds)
    {
        Volatile.Write(ref _pending, Math.Max(0, pending));
        Volatile.Write(ref _oldestPendingAgeSeconds, Math.Max(0d, oldestPendingAgeSeconds));
    }
}
