namespace GeoAssets.Core.Services;

/// <summary>
/// One mid-route point along a cable where extra fiber is coiled rather than running straight to
/// the next point — a technical reserve loop or a splice closure (XD01-282). Ordered along the
/// route by <see cref="RouteDistanceMeters"/>; <see cref="OtdrFaultCorrelationEngine"/> sorts
/// defensively rather than trusting caller order.
/// </summary>
public sealed record OtdrCableSlackPoint(double RouteDistanceMeters, double CoiledLengthMeters, FaultLocationType Kind);
