using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Wire shape of one <see cref="OrganizationGrant"/> on the kernel's generic grant routes (XD01-385,
/// <c>{prefix}/organization-grants</c>) — produced by <c>GeoAssets.Server</c>, consumed by the vertical hosts' REST client,
/// so the shape can't drift between them.
/// </summary>
public sealed record OrganizationGrantDto(
    Guid                  Id,
    Guid                  GranteeOrganizationId,
    Guid                  ResourceOrganizationId,
    string?               ResourceType,
    string?               ScopeId,
    IReadOnlyList<string> AllowedActions,
    string?               RequiredRole,
    DateTime?             ExpiresAt,
    string                GrantedBy,
    DateTime              GrantedAt,
    bool                  IsActive)
{
    public static OrganizationGrantDto FromModel(OrganizationGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return new OrganizationGrantDto(
            grant.Id, grant.GranteeOrganizationId, grant.ResourceOrganizationId, grant.ResourceType, grant.ScopeId,
            [.. grant.AllowedActions], grant.RequiredRole, grant.ExpiresAt, grant.GrantedBy, grant.GrantedAt, grant.IsActive);
    }
}
