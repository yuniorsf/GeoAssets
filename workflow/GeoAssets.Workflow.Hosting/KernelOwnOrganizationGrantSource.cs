using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The caller's own organization's active grants (XD01-382) as the kernel answers them for the caller's own token: the
/// <see cref="AuthorizationContext.OrganizationGrants"/> of <see cref="KernelGeoAuthorizationService"/>'s context, read from
/// <c>GET /api/identity/me/cross-org-entitlements</c> together with the caller's scoped roles (XD01-409), so the grants and
/// the roles that qualify them come from one answer, and a request reads no separate grant route. The Web client reads its
/// grants the same way (XD01-329). A kernel that does not answer yields no grant: a grantee is then offered less than the
/// kernel would allow, never more. A kernel that rejects the caller's token (401) throws
/// <see cref="KernelRejectedCallerTokenException"/> from <see cref="ForwardedBearerTokenHandler"/> instead (XD01-390).
/// </summary>
public sealed class KernelOwnOrganizationGrantSource(IGeoAuthorizationService authorization) : IOwnOrganizationGrantSource
{
    public async Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct)
    {
        var context = await authorization.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
        return context.User.OrganizationId == granteeOrganizationId ? context.OrganizationGrants : [];
    }
}
