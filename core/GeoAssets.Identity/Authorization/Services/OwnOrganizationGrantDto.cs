using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Wire shape for <c>GET /api/identity/organization-grants/me</c> (XD01-332): one active,
/// unexpired <see cref="OrganizationGrant"/> held by the caller's own organization — what a
/// client needs to evaluate the service-order organization gate the way the server does, and
/// nothing else (no grant id, no grantor, no audit fields). Shared by <c>GeoAssets.Server</c>
/// (produces) and the REST-backed clients (consume) so the shape can't drift between them.
/// </summary>
public sealed record OwnOrganizationGrantDto(
    Guid                  ResourceOrganizationId,
    string?               ResourceType,
    string?               ScopeId,
    IReadOnlyList<string> AllowedActions,
    string?               RequiredRole,
    DateTime?             ExpiresAt)
{
    /// <summary>
    /// <see cref="OrganizationGrant.RequiresScopedRole"/> (XD01-326). A consumer must carry it into its rule evaluation:
    /// read without it, a grant that only roles held on the resource organization may use would look organization-wide.
    /// </summary>
    public bool RequiresScopedRole { get; init; }

    /// <summary><see cref="OrganizationGrant.OrderTypeIds"/> (XD01-326); empty means every order type.</summary>
    public IReadOnlyList<string> OrderTypeIds { get; init; } = [];

    public static OwnOrganizationGrantDto FromModel(OrganizationGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return new OwnOrganizationGrantDto(
            grant.ResourceOrganizationId, grant.ResourceType, grant.ScopeId,
            [.. grant.AllowedActions], grant.RequiredRole, grant.ExpiresAt)
        {
            RequiresScopedRole = grant.RequiresScopedRole,
            OrderTypeIds = [.. grant.OrderTypeIds],
        };
    }

    /// <summary>The grant as held by <paramref name="granteeOrganizationId"/>, the caller's organization.</summary>
    public OrganizationGrant ToModel(Guid granteeOrganizationId) => new()
    {
        GranteeOrganizationId = granteeOrganizationId,
        ResourceOrganizationId = ResourceOrganizationId,
        ResourceType = ResourceType,
        ScopeId = ScopeId,
        AllowedActions = [.. AllowedActions],
        RequiredRole = RequiredRole,
        RequiresScopedRole = RequiresScopedRole,
        OrderTypeIds = [.. OrderTypeIds ?? []],
        ExpiresAt = ExpiresAt,
        IsActive = true,
    };
}
