namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Body of <c>POST {prefix}/organizations/{orgId}/scoped-roles</c> (XD01-327): give <see cref="UserId"/> the role
/// <see cref="RoleId"/> on the organization of the route. Who assigned it and when are set by the server.
/// </summary>
public sealed record ScopedRoleAssignRequest(Guid UserId, Guid RoleId);
