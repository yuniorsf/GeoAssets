namespace GeoAssets.Identity.Authorization.EFCore.Entities;

/// <summary>
/// Append-only change log of what users hold on other organizations (XD01-328): one row per (user, organization) whose
/// scoped role assignments (<c>UserRole.OrganizationId</c>, XD01-325) may have changed, written in the same
/// <c>SaveChanges</c> as the change itself by <see cref="ScopedRoleAssignmentChangeCapture"/>.
///
/// The twin of <see cref="OrganizationGrantSyncOutboxRecord"/>, with one difference: a row records <i>which</i> pair changed,
/// not a snapshot of it. What the pair holds depends on rows other writes change too — a role's permissions, its name, the
/// user's home organization — so the feed (<c>EFScopedRoleAssignmentSyncFeed</c>) resolves each row to the pair's state
/// when the batch is read. Replaying an older row is therefore harmless: it resolves to the current state, and so does every
/// later row for the same pair.
/// </summary>
internal sealed class ScopedRoleAssignmentSyncOutboxRecord
{
    /// <summary>Monotonically increasing cursor (Postgres identity column) — the sync protocol's ordering key.</summary>
    public long SequenceNumber { get; set; }

    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
}
