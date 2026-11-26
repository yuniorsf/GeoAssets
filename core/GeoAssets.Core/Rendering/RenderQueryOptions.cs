namespace GeoAssets.Core.Rendering;

/// <summary>What a render query returns (XD01-372): all features, or only those of <see cref="AssetTypeId"/>.</summary>
public sealed record RenderQueryOptions
{
    /// <summary>The most features a render query returns unless told otherwise (XD01-430) — the bounds read's cap.</summary>
    public const int DefaultMaxCount = 10_000;

    /// <summary><c>null</c> (the default) for every asset type.</summary>
    public string? AssetTypeId { get; init; }

    /// <summary>
    /// The map zoom drawn (XD01-430): features whose asset type's <c>MinZoom</c>/<c>MaxZoom</c> exclude it are left out
    /// (<see cref="Models.AssetTypeZoomRange"/>; features of an unknown type stay). <c>null</c> (the default) leaves
    /// every zoom range out of it.
    /// </summary>
    public int? Zoom { get; init; }

    /// <summary>
    /// At most this many features, the first by ordinal id, so a capped answer is always the same one (XD01-430);
    /// zero or less returns none. Defaults to <see cref="DefaultMaxCount"/>: a render query is always bounded.
    /// </summary>
    public int MaxCount { get; init; } = DefaultMaxCount;
}
