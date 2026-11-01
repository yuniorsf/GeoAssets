namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Persistence abstraction for <see cref="ContractorUsPosting"/> (XD01-242). Deliberately has no
/// update or delete method — the ledger is append-only, matching the ticket's own AC ("no update
/// path, only insert"). Scoped to the caller's own tenant the same way
/// <see cref="IContractorUsLedgerRepository"/> is.
/// </summary>
public interface IContractorUsPostingRepository
{
    Task<ContractorUsPosting?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ContractorUsPosting>> GetByServiceOrderIdAsync(string serviceOrderId, CancellationToken ct = default);

    /// <summary>Postings for one contractor in [<paramref name="fromUtc"/>, <paramref name="toExclusiveUtc"/>) — a billing-cycle query.</summary>
    Task<IReadOnlyList<ContractorUsPosting>> GetByContractorAndPeriodAsync(
        Guid contractorOrganizationId, DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct = default);

    Task AddAsync(ContractorUsPosting posting, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
