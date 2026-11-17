using System.Diagnostics.Metrics;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// The pole-occupancy commit instruments of <see cref="GeoAssetsMeter"/> (XD01-318), created on
/// that meter. None is ever tagged with an organization, order, pole or occupant id.
/// </summary>
internal sealed class OccupancyCommitInstruments(Meter meter)
{
    /// <summary>Occupancy changes committed into poles' live occupancy (tag: kind).</summary>
    public Counter<long> ChangesCommitted { get; } = meter.CreateCounter<long>(
        "geoassets.isp.pole_occupancy.changes_committed",
        unit: "{changes}",
        description: "Staged occupancy changes committed into poles' live occupancy by an approval (tag: kind).");

    /// <summary>Duration of one approve-and-commit (tag: outcome).</summary>
    public Histogram<double> DurationMs { get; } = meter.CreateHistogram<double>(
        "geoassets.isp.pole_occupancy.commit.duration",
        unit: "ms",
        description: "Duration of one occupancy approve-and-commit (tag: outcome=ok|conflict|rejected|cancelled|error).");

    /// <summary>Approvals refused because a pole or a staged set changed after staging.</summary>
    public Counter<long> Conflicts { get; } = meter.CreateCounter<long>(
        "geoassets.isp.pole_occupancy.commit.conflicts",
        unit: "{commits}",
        description: "Occupancy approvals refused because a pole's occupancy or a staged set changed after staging.");
}
