using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// One active grant of <see cref="CrossOrgEntitlementsDto.Grants"/> (XD01-329): the fields rule evaluation reads — the same
/// as <see cref="OwnOrganizationGrantDto"/> — plus the resource organization's name. No grant id, grantor or audit field.
/// </summary>
public sealed record CrossOrgGrantEntitlementDto(
    Guid                  ResourceOrganizationId,
    string?               ResourceOrganizationName,
    string?               ResourceType,
    string?               ScopeId,
    IReadOnlyList<string> OrderTypeIds,
    IReadOnlyList<string> AllowedActions,
    string?               RequiredRole,
    bool                  RequiresScopedRole,
    DateTime?             ExpiresAt)
{
    public static CrossOrgGrantEntitlementDto FromModel(OrganizationGrant grant, string? resourceOrganizationName)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return new CrossOrgGrantEntitlementDto(
            grant.ResourceOrganizationId, resourceOrganizationName, grant.ResourceType, grant.ScopeId,
            [.. grant.OrderTypeIds], [.. grant.AllowedActions], grant.RequiredRole, grant.RequiresScopedRole, grant.ExpiresAt);
    }

    /// <summary>The grant as held by <paramref name="granteeOrganizationId"/>, the caller's organization.</summary>
    public OrganizationGrant ToModel(Guid granteeOrganizationId) => new()
    {
        GranteeOrganizationId = granteeOrganizationId,
        ResourceOrganizationId = ResourceOrganizationId,
        ResourceType = ResourceType,
        ScopeId = ScopeId,
        AllowedActions = [.. AllowedActions ?? []],
        RequiredRole = RequiredRole,
        RequiresScopedRole = RequiresScopedRole,
        OrderTypeIds = [.. OrderTypeIds ?? []],
        ExpiresAt = ExpiresAt,
        IsActive = true,
    };
}
