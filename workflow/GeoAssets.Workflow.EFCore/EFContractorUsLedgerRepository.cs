using GeoAssets.Workflow.Contractors;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>EF Core implementation of <see cref="IContractorUsLedgerRepository"/>.</summary>
public sealed class EFContractorUsLedgerRepository(ServiceOrderDbContext db) : IContractorUsLedgerRepository
{
    public async Task<ContractorUsLedgerEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.ContractorUsLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

        return record is null ? null : ContractorUsLedgerMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<ContractorUsLedgerEntry>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await db.ContractorUsLedgerEntries
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(ContractorUsLedgerMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<ContractorUsLedgerEntry>> GetByServiceOrderIdAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var records = await db.ContractorUsLedgerEntries
            .Where(e => e.ServiceOrderId == serviceOrderId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(ContractorUsLedgerMapper.ToDomain).ToList();
    }

    public async Task AddAsync(ContractorUsLedgerEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await db.ContractorUsLedgerEntries.AddAsync(ContractorUsLedgerMapper.ToRecord(entry), ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ContractorUsLedgerEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var existing = await db.ContractorUsLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == entry.Id, ct).ConfigureAwait(false);
        if (existing is null) return;

        var updated = ContractorUsLedgerMapper.ToRecord(entry);
        existing.ServiceOrderId = updated.ServiceOrderId;
        existing.UsCode = updated.UsCode;
        existing.Description = updated.Description;
        existing.Quantity = updated.Quantity;
        existing.UnitValue = updated.UnitValue;
        existing.PhotoProofJson = updated.PhotoProofJson;
        existing.UpdatedAt = updated.UpdatedAt;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.ContractorUsLedgerEntries
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (record is not null) db.ContractorUsLedgerEntries.Remove(record);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
