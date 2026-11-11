namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Wire-level snapshot of one <see cref="OrganizationGrant"/> mutation (XD01-290), as returned by
/// <see cref="GeoAssets.Identity.Authorization.IOrganizationGrantSyncClient.GetSyncBatchAsync"/>
/// and <see cref="GeoAssets.Identity.Authorization.Repositories.IOrganizationGrantRepository.GetSyncBatchAsync"/>
/// — the shape a FieldService-side consumer upserts into its local grant mirror table to keep
/// <c>ServiceOrders</c>' grant-aware Row-Level-Security policy working once that table's
/// database is isolated from the shared Core Platform database where the real
/// <see cref="OrganizationGrant"/> table lives.
/// </summary>
public sealed record OrganizationGrantSyncEntry
{
    /// <summary>Monotonically increasing cursor — pass the highest value seen as the next call's <c>afterSequence</c>.</summary>
    public required long SequenceNumber { get; init; }

    public required Guid GrantId { get; init; }
    public required Guid GranteeOrganizationId { get; init; }
    public required Guid ResourceOrganizationId { get; init; }
    public string? ResourceType { get; init; }
    public string? ScopeId { get; init; }
    public required bool IsActive { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
