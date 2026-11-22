namespace GeoAssets.Workflow.Persistence.Outbox;

/// <summary>
/// Configuration options for <see cref="OrganizationGrantSyncWorker"/> (XD01-290) and, on the same section,
/// <see cref="ScopedRoleAssignmentSyncWorker"/> (XD01-328) — one batch size, one poll interval, one switch for both mirrors.
/// </summary>
public sealed class OrganizationGrantSyncOptions
{
    public const string SectionName = "OrganizationGrantSync";

    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// How often the worker polls for new grant changes — directly the upper bound on how stale
    /// the local mirror (and therefore the grant-aware RLS exception checking it) can be relative
    /// to the Core Platform's real <c>OrganizationGrants</c> table. A revoked grant can still
    /// authorize access here for up to roughly this long after revocation; document that
    /// explicitly wherever this isolation's security model is discussed, don't just lower the
    /// default and call it fixed.
    ///
    /// The same bound holds for scoped role assignments (XD01-328): a role revoked on another
    /// organization in the Core Platform (or its role deleted, its last permission removed) still
    /// counts for the cross-organization rule here until the next poll after the change is
    /// recorded — about one interval, plus however long the worker takes to drain a backlog of
    /// <see cref="BatchSize"/>-sized batches. Each poll that finds entries is followed immediately
    /// by the next, so the bound grows with the backlog, not with the interval.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    public bool EnableSyncWorker { get; set; } = true;
}
