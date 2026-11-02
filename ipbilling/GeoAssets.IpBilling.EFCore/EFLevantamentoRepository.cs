using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="ILevantamentoRepository"/>.</summary>
public sealed class EFLevantamentoRepository(IpBillingDbContext db) : ILevantamentoRepository
{
    public async Task<LevantamentoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var record = await db.LevantamentoHeaders
            .FirstOrDefaultAsync(h => h.ServiceOrderId == serviceOrderId, ct).ConfigureAwait(false);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task AddHeaderAsync(LevantamentoHeader header, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(header);
        await db.LevantamentoHeaders.AddAsync(IpBillingMapper.ToRecord(header), ct).ConfigureAwait(false);
    }

    public async Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
    {
        var record = await db.LevantamentoHeaders
            .FirstOrDefaultAsync(h => h.ServiceOrderId == serviceOrderId, ct).ConfigureAwait(false);
        if (record is not null) record.IsValidated = true;
    }

    public async Task<IReadOnlyList<LevantamentoTransformerLine>> GetTransformerLinesAsync(
        string serviceOrderId, CancellationToken ct = default)
    {
        var records = await db.LevantamentoTransformerLines
            .Include(l => l.LampDetails)
            .Where(l => l.ServiceOrderId == serviceOrderId)
            .OrderBy(l => l.CodigoInstalacao)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task ReplaceTransformerLinesAsync(
        string serviceOrderId, IReadOnlyList<LevantamentoTransformerLine> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var existing = await db.LevantamentoTransformerLines
            .Where(l => l.ServiceOrderId == serviceOrderId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0) db.LevantamentoTransformerLines.RemoveRange(existing);

        await db.LevantamentoTransformerLines.AddRangeAsync(
            lines.Select(IpBillingMapper.ToRecord), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Levantamento24hLine>> Get24hLinesAsync(
        string serviceOrderId, CancellationToken ct = default)
    {
        var records = await db.Levantamento24hLines
            .Where(l => l.ServiceOrderId == serviceOrderId)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task Replace24hLinesAsync(
        string serviceOrderId, IReadOnlyList<Levantamento24hLine> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var existing = await db.Levantamento24hLines
            .Where(l => l.ServiceOrderId == serviceOrderId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0) db.Levantamento24hLines.RemoveRange(existing);

        await db.Levantamento24hLines.AddRangeAsync(
            lines.Select(IpBillingMapper.ToRecord), ct).ConfigureAwait(false);
    }

    public async Task<LevantamentoHeader?> GetMostRecentValidatedAsync(
        Guid municipioId, string beforeCompetencia, CancellationToken ct = default)
    {
        var record = await db.LevantamentoHeaders
            .Where(h => h.MunicipioId == municipioId && h.IsValidated &&
                        h.Competencia.CompareTo(beforeCompetencia) < 0)
            .OrderByDescending(h => h.Competencia)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<LevantamentoHeader?> GetFirstValidatedAsync(Guid municipioId, CancellationToken ct = default)
    {
        var record = await db.LevantamentoHeaders
            .Where(h => h.MunicipioId == municipioId && h.IsValidated)
            .OrderBy(h => h.Competencia)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<IReadOnlySet<string>> GetAllValidatedCodigosInstalacaoAsync(
        Guid municipioId, CancellationToken ct = default)
    {
        var validatedOrderIds = db.LevantamentoHeaders
            .Where(h => h.MunicipioId == municipioId && h.IsValidated)
            .Select(h => h.ServiceOrderId);

        var codigos = await db.LevantamentoTransformerLines
            .Where(l => validatedOrderIds.Contains(l.ServiceOrderId))
            .Select(l => l.CodigoInstalacao)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        return codigos.ToHashSet(StringComparer.Ordinal);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
