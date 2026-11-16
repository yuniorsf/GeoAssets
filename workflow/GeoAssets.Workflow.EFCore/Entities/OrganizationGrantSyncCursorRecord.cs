namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// Single-row table (XD01-290) tracking how far <c>OrganizationGrantSyncWorker</c> has consumed
/// the Core Platform's grant-change feed. <see cref="Id"/> is always <c>1</c> — there is exactly
/// one cursor per isolated database, not one per anything else.
/// </summary>
internal sealed class OrganizationGrantSyncCursorRecord
{
    public int Id { get; set; } = 1;
    public long LastSequenceNumber { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
