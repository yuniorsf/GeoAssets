using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Read port for another organization's features as a map reference (XD01-330): the features one named organization
/// owns inside a bounding box, projected to <see cref="ReferenceFeature"/>'s five fields. It decides nothing about who may
/// ask — the caller authorizes first — and reads under that organization's tenant scope for this call only, leaving the
/// request's own <see cref="ITenantContext"/> untouched.
///
/// <para><b>Contract.</b> Only features whose owning organization is exactly <c>organizationId</c> — never the shared
/// (<see cref="Guid.Empty"/>-owned) ones unless that is the organization asked for — with a geometry intersecting
/// <c>bbox</c> (edges inclusive, SRID 4326), narrowed by <see cref="ReferenceFeatureFilter"/>, ordered by id (ordinal) and
/// cut at <see cref="ReferenceFeatureFilter.MaxResults"/>. A <c>null</c> filter throws <see cref="ArgumentNullException"/>,
/// a non-positive <see cref="ReferenceFeatureFilter.MaxResults"/> <see cref="ArgumentOutOfRangeException"/>; a cancelled
/// token surfaces as <see cref="OperationCanceledException"/>. No match is an empty list.</para>
/// </summary>
public interface IReferenceFeatureQuery
{
    Task<IReadOnlyList<ReferenceFeature>> GetInBoundsAsync(
        Guid organizationId, BoundingBox bbox, ReferenceFeatureFilter filter, CancellationToken ct);
}
