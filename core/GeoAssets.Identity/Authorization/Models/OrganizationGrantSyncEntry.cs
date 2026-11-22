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

    /// <summary>
    /// The grant's <see cref="OrganizationGrant.AllowedActions"/> (XD01-328) — with the three fields below, what a
    /// consumer needs to decide the cross-organization rule (XD01-326) itself rather than only the row-level exception.
    /// An entry recorded before these fields existed carries none of them, and is followed by a fresh snapshot of every
    /// grant (the <c>AddOrganizationGrantSyncRuleFields</c> migration re-records each one).
    /// </summary>
    public IReadOnlyList<string> AllowedActions { get; init; } = [];

    /// <summary>The grant's <see cref="OrganizationGrant.RequiredRole"/> (XD01-328).</summary>
    public string? RequiredRole { get; init; }

    /// <summary>The grant's <see cref="OrganizationGrant.RequiresScopedRole"/> (XD01-328).</summary>
    public bool RequiresScopedRole { get; init; }

    /// <summary>The grant's <see cref="OrganizationGrant.OrderTypeIds"/> (XD01-328); empty means every order type.</summary>
    public IReadOnlyList<string> OrderTypeIds { get; init; } = [];
}
