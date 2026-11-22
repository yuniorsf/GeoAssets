namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Wire shape of one organization-scoped role assignment (XD01-327): <see cref="UserId"/>, a member of
/// <see cref="UserOrganizationId"/>, holds <see cref="RoleName"/> on <see cref="OrganizationId"/>. Produced by
/// <c>GeoAssets.Server</c>'s <c>{prefix}/organizations/{orgId}/scoped-roles</c> and <c>{prefix}/users/{id}/scoped-roles</c>,
/// consumed by <see cref="IScopedRoleAdministrationClient"/>. <see cref="AssignedBy"/> and <see cref="AssignedAt"/> are always
/// the server's, never the client's.
/// </summary>
public sealed record ScopedRoleAssignmentDto(
    Guid      Id,
    Guid      UserId,
    string    UserDisplayName,
    string    UserEmail,
    Guid?     UserOrganizationId,
    string?   UserOrganizationName,
    Guid      OrganizationId,
    string?   OrganizationName,
    Guid      RoleId,
    string    RoleName,
    string?   AssignedBy,
    DateTime  AssignedAt);
