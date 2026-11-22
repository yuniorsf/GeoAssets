namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Wire-level state of what one user holds on one other organization (XD01-328), as returned by
/// <see cref="GeoAssets.Identity.Authorization.IScopedRoleAssignmentSyncClient.GetSyncBatchAsync"/> and
/// <see cref="GeoAssets.Identity.Authorization.Repositories.IScopedRoleAssignmentSyncFeed.GetSyncBatchAsync"/> — the shape
/// a vertical host upserts into its local scoped-role mirror, so it can decide a grant with
/// <see cref="OrganizationGrant.RequiresScopedRole"/> without the identity database.
///
/// One entry per (<see cref="UserId"/>, <see cref="ResourceOrganizationId"/>) change, resolved to what a consumer needs to
/// decide: the role names (for <see cref="OrganizationGrant.RequiredRole"/>) and the permission codes they carry there,
/// exactly as <c>AuthorizationContext.ScopedRoles</c>/<c>ScopedPermissions</c> resolve them. An entry with
/// <see cref="IsActive"/> false is a tombstone — the user holds nothing there any more (the last assignment was revoked,
/// its role or organization deleted, or the organization became the user's home) — and the consumer drops its row.
/// </summary>
public sealed record ScopedRoleAssignmentSyncEntry
{
    /// <summary>Monotonically increasing cursor — pass the highest value seen as the next call's <c>afterSequence</c>.</summary>
    public required long SequenceNumber { get; init; }

    /// <summary>The user's <see cref="AppUser.Id"/>.</summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// The user's <see cref="AppUser.ExternalObjectId"/> — the identity provider's id a vertical host knows its caller by
    /// (the token's <c>oid</c>). Empty for a user who never signed in.
    /// </summary>
    public string ExternalObjectId { get; init; } = string.Empty;

    /// <summary>The organization the roles are held on.</summary>
    public required Guid ResourceOrganizationId { get; init; }

    /// <summary>Role names the user holds on <see cref="ResourceOrganizationId"/>; empty for a tombstone.</summary>
    public IReadOnlyList<string> RoleNames { get; init; } = [];

    /// <summary>Permission codes of <see cref="RoleNames"/>; empty for a tombstone.</summary>
    public IReadOnlyList<string> PermissionCodes { get; init; } = [];

    /// <summary>False for a tombstone: the user holds no role on <see cref="ResourceOrganizationId"/>.</summary>
    public required bool IsActive { get; init; }
}
