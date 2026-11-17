using System.Diagnostics.Metrics;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// The service-order fan-out instruments of <see cref="GeoAssetsMeter"/> (XD01-316), created on
/// that meter. Neither is ever tagged with an organization, order, pole or occupant id.
/// </summary>
internal sealed class FanOutInstruments(Meter meter)
{
    /// <summary>Child orders created by fan-out dispatches (tag: order.type).</summary>
    public Counter<long> ChildrenSpawned { get; } = meter.CreateCounter<long>(
        "geoassets.orders.fanout.children_spawned",
        unit: "{orders}",
        description: "Child orders created by service-order fan-out dispatches (tag: order.type of the parent).");

    /// <summary>Duration of one fan-out dispatch (tag: outcome).</summary>
    public Histogram<double> DurationMs { get; } = meter.CreateHistogram<double>(
        "geoassets.orders.fanout.duration",
        unit: "ms",
        description: "Duration of one service-order fan-out dispatch (tag: outcome=ok|blocked|rejected|cancelled|error).");
}
