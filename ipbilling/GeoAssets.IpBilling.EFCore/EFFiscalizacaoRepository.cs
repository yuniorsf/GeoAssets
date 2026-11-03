using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IFiscalizacaoRepository"/>.</summary>
public sealed class EFFiscalizacaoRepository(IpBillingDbContext db) : IFiscalizacaoRepository
{
    public async Task<FiscalizacaoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var record = await db.FiscalizacaoHeaders
            .FirstOrDefaultAsync(h => h.ServiceOrderId == serviceOrderId, ct).ConfigureAwait(false);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task AddHeaderAsync(FiscalizacaoHeader header, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(header);
        await db.FiscalizacaoHeaders.AddAsync(IpBillingMapper.ToRecord(header), ct).ConfigureAwait(false);
    }

    public async Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var record = await db.FiscalizacaoHeaders
            .FirstOrDefaultAsync(h => h.ServiceOrderId == serviceOrderId, ct).ConfigureAwait(false);
        if (record is not null) record.IsValidated = true;
    }

    public async Task<IReadOnlyList<FiscalizacaoLine>> GetLinesAsync(
        string serviceOrderId, CancellationToken ct = default)
    {
        var records = await db.FiscalizacaoLines
            .Include(l => l.OperationGroups)
            .Where(l => l.ServiceOrderId == serviceOrderId)
            .OrderBy(l => l.CodigoInstalacao)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task ReplaceLinesAsync(
        string serviceOrderId, IReadOnlyList<FiscalizacaoLine> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var existing = await db.FiscalizacaoLines
            .Where(l => l.ServiceOrderId == serviceOrderId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0) db.FiscalizacaoLines.RemoveRange(existing);

        await db.FiscalizacaoLines.AddRangeAsync(
            lines.Select(IpBillingMapper.ToRecord), ct).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
