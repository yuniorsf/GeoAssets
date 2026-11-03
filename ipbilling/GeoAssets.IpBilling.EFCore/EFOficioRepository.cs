using GeoAssets.IpBilling.Oficio;
using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IOficioRepository"/>.</summary>
public sealed class EFOficioRepository(IpBillingDbContext db) : IOficioRepository
{
    public async Task<OficioHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var record = await db.OficioHeaders
            .FirstOrDefaultAsync(h => h.ServiceOrderId == serviceOrderId, ct).ConfigureAwait(false);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<OficioHeader?> GetHeaderByNumeroAsync(
        Guid municipioId, string numeroPrefeitura, CancellationToken ct = default)
    {
        var record = await db.OficioHeaders
            .FirstOrDefaultAsync(
                h => h.MunicipioId == municipioId && h.NumeroPrefeitura == numeroPrefeitura, ct)
            .ConfigureAwait(false);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task AddHeaderAsync(OficioHeader header, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(header);
        await db.OficioHeaders.AddAsync(IpBillingMapper.ToRecord(header), ct).ConfigureAwait(false);
    }

    public async Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var record = await db.OficioHeaders
            .FirstOrDefaultAsync(h => h.ServiceOrderId == serviceOrderId, ct).ConfigureAwait(false);
        if (record is not null) record.IsValidated = true;
    }

    public async Task ClearNumeroPrefeituraAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var record = await db.OficioHeaders
            .FirstOrDefaultAsync(h => h.ServiceOrderId == serviceOrderId, ct).ConfigureAwait(false);
        if (record is not null) record.NumeroPrefeitura = null;
    }

    public async Task<IReadOnlyList<OficioLine>> GetLinesAsync(
        string serviceOrderId, CancellationToken ct = default)
    {
        var records = await db.OficioLines
            .Include(l => l.OperationGroups)
            .Where(l => l.ServiceOrderId == serviceOrderId)
            .OrderBy(l => l.CodigoInstalacao)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task ReplaceLinesAsync(
        string serviceOrderId, IReadOnlyList<OficioLine> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var existing = await db.OficioLines
            .Where(l => l.ServiceOrderId == serviceOrderId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0) db.OficioLines.RemoveRange(existing);

        await db.OficioLines.AddRangeAsync(
            lines.Select(IpBillingMapper.ToRecord), ct).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
