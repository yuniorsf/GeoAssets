using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Services.ReferenceLayers;

/// <summary>
/// The <see cref="IReferenceFeatureClient"/> of a host that registers no REST client for reference features (the MAUI
/// app, whose map is out of XD01-331's scope): every organization answers <see cref="ReferenceFeatureResponse.Forbidden"/>,
/// so a layer that somehow got switched on is cleared and its toggle removed, as for a revoked entitlement. Such a host
/// also has no entitlements (<c>NoCrossOrgEntitlementsService</c>), so no toggle is offered and this is never called.
/// </summary>
public sealed class UnavailableReferenceFeatureClient : IReferenceFeatureClient
{
    public Task<ReferenceFeatureResponse> GetAsync(Guid organizationId, BoundingBox bbox, CancellationToken ct) =>
        Task.FromResult(ReferenceFeatureResponse.Forbidden);
}
