using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Narrow, cancellable read port for selecting assets by area or by attribute values. It exists so
/// selection strategies do not have to go through <see cref="IAssetProvider"/>'s synchronous,
/// cache-backed spatial members, and so a server host can push the work down to its store.
///
/// <para><b>Bounded-result contract (instead of paging).</b> Both members return at most
/// <see cref="AssetSpatialFilter.MaxResults"/> features, ordered by <see cref="GeoFeature.Id"/>
/// ascending with ordinal comparison; the cut happens after ordering, so the same inputs over the
/// same data always yield the same list. There is no continuation token: a caller that needs to
/// know whether the result was truncated asks for one more than it intends to keep.</para>
///
/// <para><b>Shared guards.</b> A <c>null</c> argument throws <see cref="ArgumentNullException"/>;
/// <see cref="AssetSpatialFilter.MaxResults"/> of zero or less throws
/// <see cref="ArgumentOutOfRangeException"/>; a cancelled token surfaces as
/// <see cref="OperationCanceledException"/>. A match never yields <c>null</c> — no match is an
/// empty list.</para>
/// </summary>
public interface IAssetSpatialQuery
{
    /// <summary>
    /// Returns the features whose geometry is covered by <paramref name="area"/>. The boundary is
    /// inclusive: a point lying exactly on the area's edge matches (unlike the strict
    /// <c>Within</c> used by <see cref="IAssetProvider.GetWithin"/>). Features without geometry
    /// never match. Coordinates are RFC 7946 (longitude, latitude).
    /// </summary>
    Task<IReadOnlyList<GeoFeature>> FindCoveredByAsync(
        GeoPolygon area, AssetSpatialFilter filter, CancellationToken ct);

    /// <summary>
    /// Returns the features whose custom attribute <paramref name="attributeKey"/> equals any of
    /// <paramref name="values"/> (ordinal, exact). The reserved key <c>"id"</c>
    /// (<see cref="AssetSpatialQueryKeys.FeatureId"/>) matches <see cref="GeoFeature.Id"/> instead
    /// of a custom attribute. A blank key throws <see cref="ArgumentException"/>; an empty
    /// <paramref name="values"/> collection returns an empty list. One value matching several
    /// features returns all of them.
    /// </summary>
    Task<IReadOnlyList<GeoFeature>> FindByAttributeValuesAsync(
        string attributeKey, IReadOnlyCollection<string> values, AssetSpatialFilter filter, CancellationToken ct);
}
