namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// One append-only commercial ledger posting (XD01-242) — the durable record a future ACID
/// commit (XD01-248) writes into and the Espelho de Medição (XD01-249) reads from. Every
/// property is <c>init</c>-only: there is deliberately no mutator anywhere on this type, and
/// <see cref="IContractorUsPostingRepository"/> has no update method either — a posting is
/// immutable once created, matching this ticket's own AC.
///
/// <see cref="UnitUs"/> and <see cref="MonetaryRate"/> are snapshotted at posting time (see
/// <see cref="ContractorUsPostingFactory.Post"/>) from whatever <see cref="UsCatalogItem"/>
/// version was effective then — a later catalog revision must never retroactively change an
/// already-posted row's totals, which is exactly why this isn't just a live FK to the catalog.
/// </summary>
public sealed class ContractorUsPosting
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The ServiceOrder this posting was claimed against.</summary>
    public string ServiceOrderId { get; init; } = string.Empty;

    /// <summary>The contractor's own tenant.</summary>
    public Guid ContractorOrganizationId { get; init; } = Guid.Empty;

    /// <summary>The <see cref="UsCatalogItem.ItemCode"/> this posting was claimed under.</summary>
    public string ItemCode { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    /// <summary>Snapshotted <c>BaseUsFactor × RegionalMultiplier</c> at posting time.</summary>
    public decimal UnitUs { get; init; }

    public decimal TotalUs => Quantity * UnitUs;

    /// <summary>Snapshotted BRL-per-US <see cref="UsCatalogItem.MonetaryConversionRate"/> at posting time.</summary>
    public decimal MonetaryRate { get; init; }

    public decimal TotalBrl => TotalUs * MonetaryRate;

    public DateTime PostedAtUtc { get; init; }
}
