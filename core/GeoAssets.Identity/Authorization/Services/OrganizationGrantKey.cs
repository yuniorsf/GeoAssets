namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// The natural key the kernel's generic grant routes (XD01-385) match an active grant by: the grantee and resource
/// organizations, the resource kind (null = every kind) and the scope within it (null = unscoped), compared ordinally.
/// </summary>
public sealed record OrganizationGrantKey(
    Guid    GranteeOrganizationId,
    Guid    ResourceOrganizationId,
    string? ResourceType,
    string? ScopeId)
{
    /// <summary>Why this key can't name a grant, or null when it can.</summary>
    public string? FindProblem()
    {
        if (GranteeOrganizationId == Guid.Empty) return "granteeOrganizationId is required.";
        if (ResourceOrganizationId == Guid.Empty) return "resourceOrganizationId is required.";
        if (GranteeOrganizationId == ResourceOrganizationId) return "A grant must cross organizations: grantee and resource organization are the same.";
        if (ResourceType is not null && string.IsNullOrWhiteSpace(ResourceType)) return "resourceType, when given, must not be blank.";
        if (ScopeId is not null && string.IsNullOrWhiteSpace(ScopeId)) return "scopeId, when given, must not be blank.";
        return null;
    }
}
