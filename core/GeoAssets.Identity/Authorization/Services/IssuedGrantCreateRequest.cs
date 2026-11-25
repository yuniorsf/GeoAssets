namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Body of <c>POST {prefix}/organizations/{orgId}/issued-grants</c> (XD01-411): the grant an administrator of <c>orgId</c>
/// issues to <see cref="GranteeOrganizationId"/> over its resources of <see cref="ResourceType"/> (null = every kind)
/// within <see cref="ScopeId"/> (null = unscoped). Who issued it, when, and that an administrator owns it are the server's.
/// </summary>
public sealed record IssuedGrantCreateRequest(
    Guid                  GranteeOrganizationId,
    string?               ResourceType,
    string?               ScopeId,
    IssuedGrantRules      Rules)
{
    /// <summary>Why this request can't create a grant issued by <paramref name="resourceOrganizationId"/>, or null when it can.</summary>
    public string? FindProblem(Guid resourceOrganizationId)
    {
        if (Rules is null) return "rules are required.";
        return Key(resourceOrganizationId).FindProblem() ?? Rules.FindProblem(ResourceType);
    }

    /// <summary>The key of the grant this request creates.</summary>
    public OrganizationGrantKey Key(Guid resourceOrganizationId) =>
        new(GranteeOrganizationId, resourceOrganizationId, ResourceType, ScopeId);
}
