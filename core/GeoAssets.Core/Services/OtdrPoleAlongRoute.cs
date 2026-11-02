namespace GeoAssets.Core.Services;

/// <summary>A pole's position along a cable's route, for upstream/downstream fault correlation (XD01-282).</summary>
public sealed record OtdrPoleAlongRoute(string PoleFeatureId, double RouteDistanceMeters);
