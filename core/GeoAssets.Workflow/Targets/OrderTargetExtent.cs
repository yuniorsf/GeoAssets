namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Bounding box of an order's positioned targets, in RFC 7946 degrees (SRID 4326). A single
/// positioned target yields a degenerate box whose minimum and maximum coincide.
/// </summary>
public sealed record OrderTargetExtent(double MinLongitude, double MinLatitude, double MaxLongitude, double MaxLatitude);
