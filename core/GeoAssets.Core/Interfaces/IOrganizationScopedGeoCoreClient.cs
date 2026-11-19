using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Feature reads against the Core GeoAssets Server on behalf of a named organization (XD01-386) —
/// the client side of the core's service-caller-only
/// <c>/api/geoassets/organizations/{organizationId}/features/…</c> routes. A vertical host whose
/// service principal carries no organization uses it to read assets exactly as one of its
/// requests' organizations sees them (that organization's features plus the shared ones);
/// <see cref="IGeoCoreClient"/> stays the shared (organization-less) view.
/// </summary>
public interface IOrganizationScopedGeoCoreClient
{
    /// <summary>The feature with <paramref name="id"/> as <paramref name="organizationId"/> sees it, or <c>null</c> when it isn't visible to it.</summary>
    Task<GeoFeature?> GetFeatureAsync(Guid organizationId, string id, CancellationToken ct = default);

    /// <summary>
    /// Every feature reachable downstream of <paramref name="id"/> over <paramref name="edgeKind"/>
    /// edges, as <paramref name="organizationId"/> sees them, in ordinal id order — empty when the
    /// root isn't visible to it. Implementations page through the server's results and stop at a
    /// documented cap.
    /// </summary>
    Task<IReadOnlyList<GeoFeature>> GetDescendantsAsync(Guid organizationId, string id, string edgeKind, CancellationToken ct = default);
}
