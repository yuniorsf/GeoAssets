using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

/// <summary>
/// Output of <see cref="IOtdrFaultCorrelationEngine.Correlate"/> (XD01-282). <see cref="Coordinate"/>,
/// <see cref="UpstreamPoleFeatureId"/>, and <see cref="DownstreamPoleFeatureId"/> are all <c>null</c>
/// for <see cref="FaultLocationType.StationSlack"/> — the fault is inside the central office, which
/// has no outside-plant coordinate to correlate against.
/// </summary>
public sealed record OtdrFaultCorrelationResult(
    FaultLocationType LocationType,
    double RouteDistanceMeters,
    GeoPoint? Coordinate,
    string? UpstreamPoleFeatureId,
    string? DownstreamPoleFeatureId,
    string DiagnosticSummary);
