namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// The <c>code</c> of each <c>application/problem+json</c> refusal of the issued-grant writes (XD01-411,
/// <c>{prefix}/organizations/{orgId}/issued-grants</c>), shared by <c>GeoAssets.Server</c> and
/// <see cref="IScopedRoleAdministrationClient"/>. A caller who does not administer the organization gets
/// <see cref="ScopedRoleAssignmentProblems.Forbidden"/> (403), the same answer as every other route on it.
/// </summary>
public static class IssuedGrantProblems
{
    /// <summary>400: the body is missing or invalid, or names an organization, permission or role that does not exist.</summary>
    public const string Invalid = "issued-grant.invalid";

    /// <summary>404: the grant does not exist, is no longer active, or was not issued by this organization.</summary>
    public const string NotFound = "issued-grant.not-found";

    /// <summary>409: an active grant with the same grantee, resource type and scope already exists; edit it instead.</summary>
    public const string Exists = "issued-grant.exists";

    /// <summary>409: the grant is owned by a vertical host (<c>OrganizationGrantSources.Service</c>), read-only here.</summary>
    public const string ServiceOwned = "issued-grant.service-owned";

    /// <summary>409: a concurrent write of a grant with the same key won the race (XD01-394); repeat the request.</summary>
    public const string Conflict = "organization-grant.conflict";
}
