namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// Local, eventually-consistent mirror of what one user holds on one other organization (XD01-328), kept current by
/// <c>ScopedRoleAssignmentSyncWorker</c> polling <see cref="GeoAssets.Identity.Authorization.IScopedRoleAssignmentSyncClient"/>
/// — the twin of <see cref="OrganizationGrantMirrorRecord"/> for the individual half of a grant that requires a scoped role
/// (XD01-326): the grant is the organization's ceiling, these rows say which of its users hold what on the resource
/// organization.
///
/// Keyed by (<see cref="UserId"/>, <see cref="ResourceOrganizationId"/>), the pair every sync entry is about; a tombstone
/// entry deletes the row, so a row exists exactly while the user holds at least one role there. Read by the caller's
/// <see cref="ExternalObjectId"/> (the token's <c>oid</c>), the id this host knows its caller by.
/// </summary>
internal sealed class ScopedRoleAssignmentMirrorRecord
{
    public Guid UserId { get; set; }
    public Guid ResourceOrganizationId { get; set; }
    public string ExternalObjectId { get; set; } = string.Empty;

    /// <summary>JSON array of the role names held on <see cref="ResourceOrganizationId"/>.</summary>
    public string RoleNamesJson { get; set; } = "[]";

    /// <summary>JSON array of the permission codes those roles carry.</summary>
    public string PermissionCodesJson { get; set; } = "[]";

    /// <summary>When this row was last written — the staleness bound is "now minus this", not a fixed constant.</summary>
    public DateTime SyncedAtUtc { get; set; }
}
