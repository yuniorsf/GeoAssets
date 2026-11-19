namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to the <c>ServiceOrderTargets</c> table (XD01-306): one row per
/// feature a service order works on. Kept separate from the domain
/// <see cref="Targets.ServiceOrderTarget"/> so the domain model stays free of EF annotations —
/// same split as <see cref="ServiceOrderRecord"/>.
/// </summary>
internal sealed class ServiceOrderTargetRecord
{
    public string Id { get; set; } = string.Empty;

    /// <summary>The owning order's id within <see cref="VerticalId"/>.</summary>
    public string ServiceOrderId { get; set; } = string.Empty;

    /// <summary>The vertical that owns <see cref="ServiceOrderId"/> (XD01-379); <c>kernel</c> for an in-process order.</summary>
    public string VerticalId { get; set; } = Targets.TargetLedger.KernelVerticalId;

    /// <summary>
    /// FK to the owning <see cref="ServiceOrderRecord"/>, whose targets are deleted with it: <see cref="ServiceOrderId"/>
    /// for a <c>kernel</c> target, null for a remote vertical's (XD01-379) — whose order has no row there. A check
    /// constraint keeps the two columns in step.
    /// </summary>
    public string? KernelOrderId { get; set; }

    /// <summary>
    /// The owning order's type, for a remote vertical's order only (null for <c>kernel</c>, whose type is its order's):
    /// what the ledger's global usage search reports without holding the order.
    /// </summary>
    public string? OrderTypeId { get; set; }

    /// <summary>
    /// <see cref="Targets.OrderTargetExclusivity"/> (numeric) that the remote order's type declared when the target was
    /// reserved; null for <c>kernel</c>, whose exclusivity is read from its order type when it is needed.
    /// </summary>
    public int? Exclusivity { get; set; }

    public string FeatureId { get; set; } = string.Empty;
    public string AssetTypeId { get; set; } = string.Empty;
    public string? Alias { get; set; }

    /// <summary>JSON-serialised <c>string[]</c> of activity ids, in the order they were given.</summary>
    public string ActivityIdsJson { get; set; } = "[]";

    public string Status { get; set; } = string.Empty;

    /// <summary><see cref="Targets.OrderTargetConflictLevel"/> as its numeric value, so it sorts None, Warning, Blocked.</summary>
    public int ConflictLevel { get; set; }

    public string? Note { get; set; }
    public double? Longitude { get; set; }
    public double? Latitude { get; set; }
    public DateTime AddedAt { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; } = Guid.Empty;

    /// <summary>
    /// Optimistic-concurrency token managed by <c>EFServiceOrderTargetRepository</c>, not by the
    /// database: an update only applies to the row still carrying the version the caller read,
    /// and stores that version plus one. No trigger, so SQLite and PostgreSQL behave the same.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// UTC instant the owning order reached a terminal state, or null while the order is open.
    /// Persistence-only (not on the domain target): it is what lets the open-usage lookup read
    /// one partial index instead of joining every order's history.
    /// </summary>
    public DateTime? ReleasedAt { get; set; }
}
