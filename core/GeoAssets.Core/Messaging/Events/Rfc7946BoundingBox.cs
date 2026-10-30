namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Immutable RFC 7946 bounding box (<c>[minLongitude, minLatitude, maxLongitude, maxLatitude]</c>, SRID 4326).
/// </summary>
public sealed record Rfc7946BoundingBox(
    double MinLongitude,
    double MinLatitude,
    double MaxLongitude,
    double MaxLatitude)
{
    public double[] ToArray() => [MinLongitude, MinLatitude, MaxLongitude, MaxLatitude];
}
