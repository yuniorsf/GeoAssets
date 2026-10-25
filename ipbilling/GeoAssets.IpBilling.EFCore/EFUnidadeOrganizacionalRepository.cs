using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IUnidadeOrganizacionalRepository"/>.</summary>
public sealed class EFUnidadeOrganizacionalRepository(IpBillingDbContext db) : IUnidadeOrganizacionalRepository
{
    public async Task<UnidadeOrganizacional?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.UnidadesOrganizacionais.FirstOrDefaultAsync(u => u.Id == id, ct).ConfigureAwait(false);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<UnidadeOrganizacional>> GetByMunicipioAsync(
        Guid municipioId, CancellationToken ct = default)
    {
        var records = await db.UnidadesOrganizacionais
            .Where(u => u.MunicipioId == municipioId)
            .OrderBy(u => u.Name)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task AddAsync(UnidadeOrganizacional unidade, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(unidade);
        await db.UnidadesOrganizacionais.AddAsync(IpBillingMapper.ToRecord(unidade), ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(UnidadeOrganizacional unidade, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(unidade);
        var existing = await db.UnidadesOrganizacionais.FirstOrDefaultAsync(u => u.Id == unidade.Id, ct).ConfigureAwait(false);
        if (existing is null) return;

        existing.MunicipioId = unidade.MunicipioId;
        existing.Name        = unidade.Name;
        existing.Code        = unidade.Code;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.UnidadesOrganizacionais.FindAsync([id], ct).ConfigureAwait(false);
        if (record is not null) db.UnidadesOrganizacionais.Remove(record);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
