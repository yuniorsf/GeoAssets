using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Services.ReferenceLayers;

/// <summary>
/// Reads another organization's reference features (XD01-331) from
/// <c>GET /api/geoassets/organizations/{organizationId}/reference-features?bbox=…</c> (XD01-330).
/// </summary>
public interface IReferenceFeatureClient
{
    /// <summary>
    /// The features of <paramref name="organizationId"/> intersecting <paramref name="bbox"/> — a box the server accepts
    /// (<see cref="ReferenceViewport.Normalize"/>) — or <see cref="ReferenceFeatureResponse.Forbidden"/> when the server
    /// refuses the caller. Any other failure throws.
    /// </summary>
    Task<ReferenceFeatureResponse> GetAsync(Guid organizationId, BoundingBox bbox, CancellationToken ct);
}
