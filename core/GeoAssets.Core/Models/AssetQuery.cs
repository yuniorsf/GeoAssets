using GeoAssets.Core.Rendering;

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

    /// <summary>
    /// Optional bounding box (SRID 4326, edges inclusive, XD01-478): only features whose geometry intersects it match;
    /// features without a geometry never do. <c>null</c> applies no spatial filter. A store-backed provider runs it in its
    /// query (PostgreSQL: <c>ST_Intersects</c> over the GiST index) together with <see cref="Skip"/>/<see cref="Take"/>,
    /// so a large box is never materialized.
    /// </summary>
    public BoundingBox? Bounds { get; init; }

    /// <summary>
    /// Optional bound on counting (XD01-478): when set, <see cref="PagedResult{T}.TotalCount"/> is the exact count while at
    /// most <see cref="CountLimit"/> features match, and <see cref="CountLimit"/> + 1 when more do — the store stops counting
    /// there instead of counting a whole large box. A negative limit counts as zero. <c>null</c> (the default) counts
    /// exactly, as before.
    /// </summary>
    public int? CountLimit { get; init; }

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
