using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// The kernel's generic organization-grant API (XD01-385) for an out-of-process vertical, called as its
/// <c>GeoAssets.ServiceCaller</c> service principal — how a vertical keeps the cross-organization grants its own reference
/// data implies in the kernel's identity store. Every call names its organizations explicitly; a failure throws
/// <see cref="HttpRequestException"/> (with the status when the kernel answered).
/// </summary>
public interface IOrganizationGrantClient
{
    /// <summary><c>PUT /api/identity/organization-grants</c>: makes the grant exist as requested (idempotent).</summary>
    Task<OrganizationGrantUpsertResult> UpsertAsync(OrganizationGrantUpsertRequest request, CancellationToken ct);

    /// <summary><c>POST /api/identity/organization-grants/revoke</c>: deactivates the active grants with <paramref name="key"/> (idempotent).</summary>
    Task<OrganizationGrantRevokeResult> RevokeAsync(OrganizationGrantKey key, CancellationToken ct);

    /// <summary><c>GET /api/identity/organization-grants</c>: one page (from 1) of an organization pair's active grants.</summary>
    Task<OrganizationGrantPage> ListActiveAsync(
        Guid granteeOrganizationId, Guid resourceOrganizationId, int page, int pageSize, CancellationToken ct);
}
