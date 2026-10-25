namespace GeoAssets.Server;

public sealed class FeatureInfoRow
{
    public required string Id          { get; init; }
    public required string Name        { get; init; }
    public required string AssetTypeId { get; init; }
    public required string Description { get; init; }
}
