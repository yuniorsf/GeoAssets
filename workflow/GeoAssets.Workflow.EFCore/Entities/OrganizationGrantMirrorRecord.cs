namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// Local, eventually-consistent mirror of one <c>OrganizationGrant</c> row (XD01-290), kept
/// current by <c>OrganizationGrantSyncWorker</c> polling
/// <see cref="GeoAssets.Identity.Authorization.IOrganizationGrantSyncClient"/>.
///
/// Exists because <c>ServiceOrders</c>' grant-aware Row-Level-Security exception needs an
/// <c>EXISTS</c> subquery against grant data, and Postgres RLS can't reach the real
/// <c>OrganizationGrants</c> table once <see cref="ServiceOrderDbContext"/> lives in its own
/// <c>geoassets_fieldservice</c> database. Column shape is a deliberate 1:1 copy of the grant
/// fields the RLS policy actually checks, so the policy SQL itself barely changes — see the
/// isolation migration's raw-SQL policy text for exactly which columns it reads.
///
/// <see cref="GrantId"/> (not a locally-generated key) is the primary key — the worker always
/// upserts by it, so a replayed/out-of-order sync entry for the same grant simply overwrites with
/// whichever snapshot arrived, which is always safe since every entry is a full snapshot, not a
/// diff (see <c>OrganizationGrantSyncOutboxRecord</c>'s own doc comment on the source side).
/// </summary>
internal sealed class OrganizationGrantMirrorRecord
{
    public Guid GrantId { get; set; }
    public Guid GranteeOrganizationId { get; set; }
    public Guid ResourceOrganizationId { get; set; }
    public string? ResourceType { get; set; }
    public string? ScopeId { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// The rest of the grant (XD01-328): what the cross-organization rule (<c>CrossOrgGrantMatcher</c>, XD01-326) decides by,
    /// so this host evaluates <c>ServiceOrderRules</c> over the mirror instead of only the row-level exception. The RLS policy
    /// reads none of these: the database stays an organization-level backstop, the rule decides per user and order type.
    /// The two lists are JSON arrays of strings, like <see cref="ServiceOrderRecord.FeatureIdsJson"/>.
    /// </summary>
    public string AllowedActionsJson { get; set; } = "[]";
    public string? RequiredRole { get; set; }
    public bool RequiresScopedRole { get; set; }
    public string OrderTypeIdsJson { get; set; } = "[]";

    /// <summary>When this mirror row was last written — the staleness bound is "now minus this", not a fixed constant.</summary>
    public DateTime SyncedAtUtc { get; set; }
}
