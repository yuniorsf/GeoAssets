namespace GeoAssets.Server.Targets;

/// <summary>The bounding box of an order's positioned targets as <c>[minLon, minLat, maxLon, maxLat]</c>.</summary>
public sealed record TargetExtentResponse(double[] Bbox);
