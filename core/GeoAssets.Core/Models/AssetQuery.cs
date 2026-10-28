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

    /// <summary>Optional organization filter matching <see cref="GeoFeatureProperties.OrganizationId"/>.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Optional exact key=value matches against <see cref="GeoFeatureProperties.CustomAttributes"/>.</summary>
    public IReadOnlyDictionary<string, string>? AttributeFilters { get; init; }

    /// <summary>Optional batch filter restricting results to the specified feature IDs.</summary>
    public IReadOnlyList<string>? FeatureIds { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 50;

    /// <summary>
    /// One of <c>"name"</c>, <c>"createdAt"</c>, <c>"updatedAt"</c>. <c>null</c> or any other,
    /// unrecognized value falls back to sorting by <c>Id</c> — every <see cref="Interfaces.IAssetProvider"/>
    /// implementer must always produce a total order over the result set, since paging
    /// (<see cref="Skip"/>/<see cref="Take"/>) over an unordered query is undefined behavior.
    /// </summary>
    public string? SortBy { get; init; }

    public bool SortDescending { get; init; }
}
