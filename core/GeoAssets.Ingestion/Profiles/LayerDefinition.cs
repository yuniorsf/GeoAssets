using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Profiles;

/// <summary>
/// A layer a profile reads. <see cref="FileStem"/> is matched case-insensitively against file names in each region
/// folder (the folder's name is the region; files directly under the root form region <c>""</c>).
/// <see cref="IndexKeys"/> feed the reference index; <see cref="SnapTarget"/> also indexes the layer's points for
/// spatial snapping. <see cref="ProcessingOrder"/> orders pass 2 (lower first); duplicates of an element's key across
/// regions keep the first one read.
/// </summary>
public sealed record LayerDefinition
{
    public required string Name { get; init; }
    public required string FileStem { get; init; }
    public required ElementKind Kind { get; init; }
    public IReadOnlyList<IndexKeyDefinition> IndexKeys { get; init; } = [];
    public bool SnapTarget { get; init; }
    public double SnapToleranceMeters { get; init; } = 1.0;
    public double DrawingOffsetX { get; init; }
    public double DrawingOffsetY { get; init; }
    public double MaxEndpointDistanceMeters { get; init; } = 5.0;
    public int ProcessingOrder { get; init; }
}
