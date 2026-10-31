using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authentication;

namespace GeoAssets.Server;

/// <summary>
/// Resolves <see cref="ITenantContext.CurrentOrganizationId"/> from the current request's
/// authenticated organization claim (XD01-200), via the same <see cref="ICurrentUserAccessor"/>
/// already populated by <c>AddGeoAssetsAuthentication</c> — no separate claims-reading logic.
/// Falls back to <see cref="Guid.Empty"/> (unauthenticated requests, or a claim mapping with no
/// <see cref="ClaimMapping.OrganizationIdClaimTypes"/> configured) so existing single-tenant
/// callers are unaffected.
/// </summary>
public sealed class HttpTenantContext(ICurrentUserAccessor currentUserAccessor) : ITenantContext
{
    public Guid CurrentOrganizationId => currentUserAccessor.GetCurrentUser()?.OrganizationId ?? Guid.Empty;
}
