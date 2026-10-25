using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IIpTransformerIndexRepository"/>.</summary>
public sealed class EFIpTransformerIndexRepository(IpBillingDbContext db) : IIpTransformerIndexRepository
{
    public async Task<IpTransformerIndex?> GetAsync(
        string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default)
    {
        var record = await db.IpTransformerIndexes.FirstOrDefaultAsync(
            i => i.MunicipioIbgeCode == municipioIbgeCode && i.CodigoInstalacao == codigoInstalacao, ct).ConfigureAwait(false);

        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<IpTransformerIndex>> GetByMunicipioAsync(
        string municipioIbgeCode, CancellationToken ct = default)
    {
        var records = await db.IpTransformerIndexes
            .Where(i => i.MunicipioIbgeCode == municipioIbgeCode)
            .OrderBy(i => i.CodigoInstalacao)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task AddAsync(IpTransformerIndex index, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        await db.IpTransformerIndexes.AddAsync(IpBillingMapper.ToRecord(index), ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(IpTransformerIndex index, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        var existing = await db.IpTransformerIndexes.FirstOrDefaultAsync(i => i.Id == index.Id, ct).ConfigureAwait(false);
        if (existing is null) return;

        existing.MunicipioIbgeCode    = index.MunicipioIbgeCode;
        existing.CodigoInstalacao     = index.CodigoInstalacao;
        existing.TransformerFeatureId = index.TransformerFeatureId;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.IpTransformerIndexes.FindAsync([id], ct).ConfigureAwait(false);
        if (record is not null) db.IpTransformerIndexes.Remove(record);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
