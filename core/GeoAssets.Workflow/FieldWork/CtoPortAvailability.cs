using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// A candidate CTO's location and splitter-port occupancy, as consulted by
/// <see cref="NearestAvailableCtoAssigner"/>. A thin, repository-agnostic projection of a CTO
/// <c>GeoFeature</c> (see <c>GeoAssets.Core.Isp.IspAssetTypes.Cto</c>) — callers build this from
/// whatever asset-provider query they already have.
/// </summary>
/// <param name="CtoFeatureId">The CTO's <c>GeoFeature.Id</c>.</param>
/// <param name="Location">The CTO's point location.</param>
/// <param name="TotalPorts">Total splitter ports on the CTO.</param>
/// <param name="OccupiedPorts">Currently occupied splitter ports on the CTO.</param>
public sealed record CtoPortAvailability(string CtoFeatureId, GeoPoint Location, int TotalPorts, int OccupiedPorts)
{
    /// <summary>True when at least one splitter port is free.</summary>
    public bool HasAvailablePort => OccupiedPorts < TotalPorts;
}
