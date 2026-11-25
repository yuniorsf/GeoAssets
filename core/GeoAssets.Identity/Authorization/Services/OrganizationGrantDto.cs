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
    /// <summary><see cref="OrganizationGrant.RequiresScopedRole"/> (XD01-326); false when the field is absent.</summary>
    public bool RequiresScopedRole { get; init; }

    /// <summary><see cref="OrganizationGrant.OrderTypeIds"/> (XD01-326); empty (every order type) when the field is absent.</summary>
    public IReadOnlyList<string> OrderTypeIds { get; init; } = [];

    /// <summary>
    /// <see cref="OrganizationGrant.Source"/> (XD01-411): one of <see cref="OrganizationGrantSources"/>;
    /// <see cref="OrganizationGrantSources.Service"/> when the field is absent.
    /// </summary>
    public string Source { get; init; } = OrganizationGrantSources.Service;

    public static OrganizationGrantDto FromModel(OrganizationGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return new OrganizationGrantDto(
            grant.Id, grant.GranteeOrganizationId, grant.ResourceOrganizationId, grant.ResourceType, grant.ScopeId,
            [.. grant.AllowedActions], grant.RequiredRole, grant.ExpiresAt, grant.GrantedBy, grant.GrantedAt, grant.IsActive)
        {
            RequiresScopedRole = grant.RequiresScopedRole,
            OrderTypeIds = [.. grant.OrderTypeIds],
            Source = grant.Source,
        };
    }
}
