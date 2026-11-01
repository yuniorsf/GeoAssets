namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Persistence abstraction for <see cref="ContractorUsLedgerEntry"/>. Every method is
/// scoped to the caller's own tenant via a global query filter on
/// <c>ContractorUsLedgerEntryRecord.OrganizationId</c> (<c>ServiceOrderDbContext</c>) plus a
/// database-level Row Level Security policy on the underlying table — equality only, no
/// cross-contractor grant exception (XD01-202; contrast with <c>IServiceOrderRepository</c>,
/// which does carve out one for <c>CrossOrgGrantRule</c>).
/// </summary>
public interface IContractorUsLedgerRepository
{
    Task<ContractorUsLedgerEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ContractorUsLedgerEntry>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContractorUsLedgerEntry>> GetByServiceOrderIdAsync(string serviceOrderId, CancellationToken ct = default);

    /// <summary>
    /// Entries created in [<paramref name="fromUtc"/>, <paramref name="toExclusiveUtc"/>) —
    /// backs a monthly "Espelho de Medição" style statement (XD01-207).
    /// </summary>
    Task<IReadOnlyList<ContractorUsLedgerEntry>> GetByPeriodAsync(DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct = default);

    Task AddAsync(ContractorUsLedgerEntry entry, CancellationToken ct = default);
    Task UpdateAsync(ContractorUsLedgerEntry entry, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
