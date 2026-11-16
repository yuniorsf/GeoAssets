using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IDistribuidoraRepository"/>.</summary>
public sealed class EFDistribuidoraRepository(IpBillingDbContext db) : IDistribuidoraRepository
{
    public async Task<Distribuidora?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.Distribuidoras.FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<PagedResult<Distribuidora>> GetPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var totalCount = await db.Distribuidoras.CountAsync(ct).ConfigureAwait(false);
        var records = await db.Distribuidoras
            .OrderBy(d => d.Name)
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return new PagedResult<Distribuidora>
        {
            Items      = records.Select(IpBillingMapper.ToDomain).ToList(),
            TotalCount = totalCount,
        };
    }

    public async Task AddAsync(Distribuidora distribuidora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(distribuidora);
        await db.Distribuidoras.AddAsync(IpBillingMapper.ToRecord(distribuidora), ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Distribuidora distribuidora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(distribuidora);
        var existing = await db.Distribuidoras
            .FirstOrDefaultAsync(d => d.Id == distribuidora.Id, ct).ConfigureAwait(false);
        if (existing is null) return;

        existing.OrganizationId = distribuidora.OrganizationId;
        existing.Name           = distribuidora.Name;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.Distribuidoras.FindAsync([id], ct).ConfigureAwait(false);
        if (record is not null) db.Distribuidoras.Remove(record);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
