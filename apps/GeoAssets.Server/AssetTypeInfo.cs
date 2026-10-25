namespace GeoAssets.Server;

public sealed class AssetTypeInfo
{
    public required Guid   Id    { get; init; }
    public required string Name  { get; init; }
    public required string Color { get; init; }
}
