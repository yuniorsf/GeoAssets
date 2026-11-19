namespace GeoAssets.NeutralOperator.Persistence.Entities;

/// <summary>
/// Read-only view of the kernel's <c>ServiceOrderTargets</c> table (XD01-380): the columns staging a change set checks — the
/// target must be the pole owner's, on that order, for that pole, and not released. The table stays the kernel's (its
/// migrations never touch it from this side); until Phase 7 replaces this read with the kernel's targets API, the vertical
/// reads it on the connection it shares with the kernel's context.
/// </summary>
internal sealed class KernelTargetRecord
{
    public string Id { get; set; } = string.Empty;

    public string ServiceOrderId { get; set; } = string.Empty;

    public string FeatureId { get; set; } = string.Empty;

    /// <summary>The ledger vertical that holds the target (XD01-379); occupancy only ever stages against the kernel's own orders.</summary>
    public string VerticalId { get; set; } = "kernel";

    /// <summary>UTC instant the order closed and released the target; null while the order is open.</summary>
    public DateTime? ReleasedAt { get; set; }

    public Guid OrganizationId { get; set; }
}
