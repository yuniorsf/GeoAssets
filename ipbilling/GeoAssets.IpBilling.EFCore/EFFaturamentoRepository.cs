using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IFaturamentoRepository"/>.</summary>
public sealed class EFFaturamentoRepository(IpBillingDbContext db) : IFaturamentoRepository
{
    public async Task<Faturamento?> GetByMunicipioAndCompetenciaAsync(
        Guid municipioId, string competencia, CancellationToken ct = default)
    {
        var record = await db.Faturamentos.FirstOrDefaultAsync(
            f => f.MunicipioId == municipioId && f.Competencia == competencia, ct);

        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<Faturamento>> GetByMunicipioAsync(
        Guid municipioId, CancellationToken ct = default)
    {
        var records = await db.Faturamentos
            .Where(f => f.MunicipioId == municipioId)
            .OrderBy(f => f.Competencia)
            .ToListAsync(ct);

        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task AddAsync(Faturamento faturamento, CancellationToken ct = default)
        => await db.Faturamentos.AddAsync(IpBillingMapper.ToRecord(faturamento), ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
