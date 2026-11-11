namespace GeoAssets.Workflow.Persistence.Outbox;

/// <summary>Configuration options for <see cref="OrganizationGrantSyncWorker"/> (XD01-290).</summary>
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
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    public bool EnableSyncWorker { get; set; } = true;
}
