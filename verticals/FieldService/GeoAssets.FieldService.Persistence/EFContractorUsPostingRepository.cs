using GeoAssets.FieldService.Contractors;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.FieldService.Persistence;

/// <summary>EF Core implementation of <see cref="IContractorUsPostingRepository"/> — append-only, no update/delete methods.</summary>
public sealed class EFContractorUsPostingRepository(FieldServiceDbContext db) : IContractorUsPostingRepository
{
    public async Task<ContractorUsPosting?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        db.JoinAmbientTransaction();
        var record = await db.ContractorUsPostings
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

        return record is null ? null : ContractorUsPostingMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<ContractorUsPosting>> GetByServiceOrderIdAsync(string serviceOrderId, CancellationToken ct = default)
    {
        db.JoinAmbientTransaction();
        var records = await db.ContractorUsPostings
            .Where(e => e.ServiceOrderId == serviceOrderId)
            .OrderBy(e => e.PostedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(ContractorUsPostingMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<ContractorUsPosting>> GetByContractorAndPeriodAsync(
        Guid contractorOrganizationId, DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct = default)
    {
        db.JoinAmbientTransaction();
        var records = await db.ContractorUsPostings
            .Where(e => e.ContractorOrganizationId == contractorOrganizationId
                && e.PostedAtUtc >= fromUtc && e.PostedAtUtc < toExclusiveUtc)
            .OrderBy(e => e.PostedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(ContractorUsPostingMapper.ToDomain).ToList();
    }

    public async Task AddAsync(ContractorUsPosting posting, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(posting);
        db.JoinAmbientTransaction();
        await db.ContractorUsPostings.AddAsync(ContractorUsPostingMapper.ToRecord(posting), ct).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        db.JoinAmbientTransaction();
        return db.SaveChangesAsync(ct);
    }
}
