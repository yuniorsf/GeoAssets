using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IMunicipioRepository"/>.</summary>
public sealed class EFMunicipioRepository(IpBillingDbContext db) : IMunicipioRepository
{
    public async Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.Municipios.FirstOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default)
    {
        var record = await db.Municipios.FirstOrDefaultAsync(m => m.IbgeCode == ibgeCode, ct).ConfigureAwait(false);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await db.Municipios.OrderBy(m => m.Name).ToListAsync(ct).ConfigureAwait(false);
        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task AddAsync(Municipio municipio, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(municipio);
        await db.Municipios.AddAsync(IpBillingMapper.ToRecord(municipio), ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Municipio municipio, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(municipio);
        var existing = await db.Municipios.FirstOrDefaultAsync(m => m.Id == municipio.Id, ct).ConfigureAwait(false);
        if (existing is null) return;

        existing.IbgeCode                    = municipio.IbgeCode;
        existing.Name                        = municipio.Name;
        existing.Uf                          = municipio.Uf;
        existing.DistribuidoraOrganizationId = municipio.DistribuidoraOrganizationId;
        existing.PrefeituraOrganizationId    = municipio.PrefeituraOrganizationId;
        existing.OrigemLevantamento          = municipio.OrigemLevantamento;
        existing.OrigemFiscalizacao          = municipio.OrigemFiscalizacao;
        existing.OrigemOficio                = municipio.OrigemOficio;
        existing.OrigemMemoriaCalculo        = municipio.OrigemMemoriaCalculo;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.Municipios.FindAsync([id], ct).ConfigureAwait(false);
        if (record is not null) db.Municipios.Remove(record);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
