using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IMunicipioRepository"/>.</summary>
public sealed class EFMunicipioRepository(IpBillingDbContext db) : IMunicipioRepository
{
    public async Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.Municipios.FirstOrDefaultAsync(m => m.Id == id, ct);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default)
    {
        var record = await db.Municipios.FirstOrDefaultAsync(m => m.IbgeCode == ibgeCode, ct);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await db.Municipios.OrderBy(m => m.Name).ToListAsync(ct);
        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task AddAsync(Municipio municipio, CancellationToken ct = default)
        => await db.Municipios.AddAsync(IpBillingMapper.ToRecord(municipio), ct);

    public async Task UpdateAsync(Municipio municipio, CancellationToken ct = default)
    {
        var existing = await db.Municipios.FirstOrDefaultAsync(m => m.Id == municipio.Id, ct);
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
        var record = await db.Municipios.FindAsync([id], ct);
        if (record is not null) db.Municipios.Remove(record);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
