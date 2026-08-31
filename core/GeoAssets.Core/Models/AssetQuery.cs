namespace GeoAssets.Core.Models;

/// <summary>
/// Filter/sort/page parameters for <see cref="Interfaces.IAssetProvider.GetPageAsync"/>.
/// </summary>
public sealed record AssetQuery
{
    /// <summary><c>null</c> matches assets of any type.</summary>
    public string? AssetTypeId { get; init; }

    /// <summary><c>null</c>/empty matches all assets, i.e. no text filter is applied.</summary>
    public string? SearchText { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 50;

    /// <summary>One of <c>"name"</c>, <c>"createdAt"</c>, <c>"updatedAt"</c>. <c>null</c> sorts by <c>Id</c>.</summary>
    public string? SortBy { get; init; }

    public bool SortDescending { get; init; }
}
