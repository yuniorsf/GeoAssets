namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// Single-row table (XD01-328) tracking how far <c>ScopedRoleAssignmentSyncWorker</c> has consumed the Core Platform's
/// scoped role assignment feed — the twin of <see cref="OrganizationGrantSyncCursorRecord"/>. <see cref="Id"/> is always <c>1</c>.
/// </summary>
internal sealed class ScopedRoleAssignmentSyncCursorRecord
{
    public int Id { get; set; } = 1;
    public long LastSequenceNumber { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
