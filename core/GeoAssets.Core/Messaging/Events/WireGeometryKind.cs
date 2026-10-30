namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// RFC 7946 wire geometry discriminator for serialized asset geometry snapshots.
/// </summary>
public enum WireGeometryKind
{
    Unspecified = 0,
    Point = 1,
    LineString = 2,
    Polygon = 3,
}
