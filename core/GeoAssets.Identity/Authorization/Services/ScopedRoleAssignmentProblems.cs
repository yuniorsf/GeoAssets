namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// The <c>code</c> of each <c>application/problem+json</c> refusal of the scoped-role administration routes (XD01-327) —
/// shared by <c>GeoAssets.Server</c> (answers them) and <see cref="IScopedRoleAdministrationClient"/> (reads them into
/// <see cref="ScopedRoleAdministrationException.Code"/>) so a UI can explain each one.
/// </summary>
public static class ScopedRoleAssignmentProblems
{
    /// <summary>403: the caller does not administer the organization of the route.</summary>
    public const string Forbidden = "scoped-role.forbidden";

    /// <summary>400: the body is missing, names no user or role, or names a role that does not exist.</summary>
    public const string Invalid = "scoped-role.invalid";

    /// <summary>400: the caller named themselves.</summary>
    public const string SelfAssignment = "scoped-role.self-assignment";

    /// <summary>404: the user, the assignment or (for a platform administrator) the organization does not exist.</summary>
    public const string NotFound = "scoped-role.not-found";

    /// <summary>409: the user is inactive, or their organization holds no active, unexpired grant from the organization.</summary>
    public const string NotContracted = "scoped-role.not-contracted";
}
