using System.Diagnostics.Metrics;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// The service-order target-selection instruments of <see cref="GeoAssetsMeter"/> (XD01-308),
/// created on that meter. None of them is ever tagged with an organization, order or feature id.
/// </summary>
internal sealed class TargetSelectionInstruments(Meter meter)
{
    /// <summary>Duration of one target-selection run (tags: strategy, mode, outcome).</summary>
    public Histogram<double> DurationMs { get; } = meter.CreateHistogram<double>(
        "geoassets.orders.targets.selection.duration",
        unit: "ms",
        description: "Duration of one service-order target selection (tags: strategy, mode=preview|apply, outcome).");

    /// <summary>Features matched by one target-selection run (tag: strategy).</summary>
    public Histogram<long> Matched { get; } = meter.CreateHistogram<long>(
        "geoassets.orders.targets.selection.matched",
        unit: "{features}",
        description: "Features matched by one service-order target selection (tag: strategy).");

    /// <summary>Target-selection runs that did not fit under the order type's selection limit (tag: strategy).</summary>
    public Counter<long> Truncated { get; } = meter.CreateCounter<long>(
        "geoassets.orders.targets.selection.truncated",
        unit: "{selections}",
        description: "Target selections that did not fit under the order type's selection limit (tag: strategy).");

    /// <summary>Targets added with a conflict (tags: level, order.type).</summary>
    public Counter<long> Conflicts { get; } = meter.CreateCounter<long>(
        "geoassets.orders.targets.conflicts",
        unit: "{targets}",
        description: "Targets added to an order while another open order holds the same feature (tags: level=warning|blocked, order.type).");
}
