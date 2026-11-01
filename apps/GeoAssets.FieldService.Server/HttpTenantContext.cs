using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authentication;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Resolves <see cref="ITenantContext.CurrentOrganizationId"/> from the current request's
/// authenticated organization claim, via the same <see cref="ICurrentUserAccessor"/> already
/// populated by <c>AddGeoAssetsAuthentication</c> — identical to
/// <c>GeoAssets.Server.HttpTenantContext</c> (XD01-200). Falls back to <see cref="Guid.Empty"/>
/// (unauthenticated requests, or a claim mapping with no
/// <see cref="ClaimMapping.OrganizationIdClaimTypes"/> configured — the default, since Entra
/// CIAM doesn't emit an organization claim out of the box) so this host behaves identically to
/// GeoAssets.Server until an operator configures a real org claim for their tenant.
/// </summary>
internal sealed class HttpTenantContext(ICurrentUserAccessor currentUserAccessor) : ITenantContext
{
    public Guid CurrentOrganizationId => currentUserAccessor.GetCurrentUser()?.OrganizationId ?? Guid.Empty;
}
