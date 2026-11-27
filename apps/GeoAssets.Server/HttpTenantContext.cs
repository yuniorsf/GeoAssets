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
///
/// A caller whose token carries no organization but whose identity record does — <paramref name="membership"/>, resolved
/// only when <c>GeoAssets:Tenancy:OrganizationFromMembership</c> is on (XD01-471) — has that organization, exactly as if
/// the token carried it.
///
/// A caller whose token carries no organization (a <c>GeoAssets.ServiceCaller</c> service principal, or a message
/// consumer with no user at all) resolves to the organization kernel code entered in <paramref name="organizationScope"/>
/// for this unit of work (XD01-379 — the target ledger routes enter the organization they validated from the request
/// body, never from a header). A caller with an organization always keeps its own; the scope is ignored for it.
/// </summary>
public sealed class HttpTenantContext(
    ICurrentUserAccessor currentUserAccessor,
    ITenantOrganizationScope? organizationScope = null,
    CallerMembershipOrganization? membership = null) : ITenantContext
{
    public Guid CurrentOrganizationId =>
        OwnOrganizationId() is { } own
            ? own
            : organizationScope?.OrganizationId ?? Guid.Empty;

    private Guid? OwnOrganizationId() =>
        currentUserAccessor.GetCurrentUser()?.OrganizationId is { } claimed && claimed != Guid.Empty
            ? claimed
            : membership?.OrganizationId;
}
