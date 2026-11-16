namespace GeoAssets.Identity.Authorization.EFCore.Entities;

/// <summary>
/// Append-only change log of <c>OrganizationGrant</c> mutations (XD01-290), written in the same
/// transaction as every grant insert/update by
/// <see cref="GeoAssets.Identity.Authorization.EFCore.Repositories.EFOrganizationGrantRepository"/>.
///
/// Exists because <c>ServiceOrders</c>' grant-aware Row-Level-Security exception
/// (<c>AddServiceOrdersRowLevelSecurity</c>) does an <c>EXISTS</c> subquery against
/// <c>OrganizationGrants</c>, a table Postgres RLS can only reference within the same physical
/// database — once <c>ServiceOrderDbContext</c> is isolated into its own
/// <c>geoassets_fieldservice</c> database, that policy needs a local copy to check instead. This
/// log is the pull-based sync feed a consumer in that isolated database polls (ordered by
/// <see cref="SequenceNumber"/>) to keep a local mirror table current — see
/// <c>OrganizationGrantSyncWorker</c> in <c>GeoAssets.Workflow.EFCore</c>, fed via
/// <see cref="GeoAssets.Core.Interfaces.IGeoCoreClient.GetOrganizationGrantSyncBatchAsync"/>.
///
/// Each row is a full snapshot of the grant's state at the moment of the write, not a diff — the
/// consumer always upserts by <see cref="GrantId"/>, so only the latest row for a given grant
/// matters once applied; earlier rows for the same grant are harmless to replay.
///
/// Internal, like <c>OutboxMessageRecord</c> in <c>GeoAssets.Workflow.EFCore</c> — a persistence
/// detail, not a Core domain type. The wire-level shape returned to callers is
/// <see cref="GeoAssets.Core.Models.OrganizationGrantSyncEntry"/>.
/// </summary>
internal sealed class OrganizationGrantSyncOutboxRecord
{
    /// <summary>Monotonically increasing cursor (Postgres identity column) — the sync protocol's ordering key.</summary>
    public long SequenceNumber { get; set; }

    public Guid GrantId { get; set; }
    public Guid GranteeOrganizationId { get; set; }
    public Guid ResourceOrganizationId { get; set; }
    public string? ResourceType { get; set; }
    public string? ScopeId { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
