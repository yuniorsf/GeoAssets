namespace GeoAssets.Core.Rendering;

/// <summary>What a render query returns (XD01-372): all features, or only those of <see cref="AssetTypeId"/>.</summary>
public sealed record RenderQueryOptions
{
    /// <summary><c>null</c> (the default) for every asset type.</summary>
    public string? AssetTypeId { get; init; }
}
