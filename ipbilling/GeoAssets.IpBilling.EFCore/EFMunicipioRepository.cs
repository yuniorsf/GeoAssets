using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence.Entities;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IMunicipioRepository"/>. Every read fills the município's grant-sync status
/// (<see cref="Municipio.GrantSyncState"/>, XD01-395) from the latest request <see cref="EFMunicipioGrantSyncOutbox"/>
/// queued for it — derived, not stored on the row, so it can never disagree with the outbox the dispatcher drains.
/// </summary>
public sealed class EFMunicipioRepository(IpBillingDbContext db) : IMunicipioRepository
{
    /// <summary>How many municípios' latest grant-sync requests one outbox query reads.</summary>
    internal const int GrantSyncStatusBatchSize = 500;

    public async Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.Municipios.FirstOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false);
        return record is null ? null : (await ToDomainAsync([record], ct).ConfigureAwait(false))[0];
    }

    public async Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default)
    {
        var record = await db.Municipios.FirstOrDefaultAsync(m => m.IbgeCode == ibgeCode, ct).ConfigureAwait(false);
        return record is null ? null : (await ToDomainAsync([record], ct).ConfigureAwait(false))[0];
    }

    public async Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await db.Municipios.OrderBy(m => m.Name).ToListAsync(ct).ConfigureAwait(false);
        return await ToDomainAsync(records, ct).ConfigureAwait(false);
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
        existing.DistribuidoraId             = municipio.DistribuidoraId;
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

    private async Task<List<Municipio>> ToDomainAsync(IReadOnlyList<MunicipioRecord> records, CancellationToken ct)
    {
        var municipios = records.Select(IpBillingMapper.ToDomain).ToList();
        if (municipios.Count == 0) return municipios;

        var latest = new Dictionary<Guid, LatestGrantSync>(municipios.Count);
        var requests = db.MunicipioGrantSyncRequests;
        foreach (var batch in municipios.Select(m => m.Id).Chunk(GrantSyncStatusBatchSize))
        {
            var rows = await requests
                .AsNoTracking()
                .Where(r => batch.Contains(r.MunicipioId)
                    && !requests.Any(o => o.MunicipioId == r.MunicipioId && o.Id > r.Id))
                .Select(r => new LatestGrantSync(r.MunicipioId, r.CompletedAt, r.FailedAt, r.LastError))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var row in rows) latest[row.MunicipioId] = row;
        }

        foreach (var municipio in municipios)
        {
            if (!latest.TryGetValue(municipio.Id, out var request)) continue;

            if (request.CompletedAt is { } completedAt)
            {
                municipio.GrantSyncState = MunicipioGrantSyncState.Synced;
                municipio.GrantSyncedAt = new DateTimeOffset(DateTime.SpecifyKind(completedAt, DateTimeKind.Utc));
            }
            else
            {
                municipio.GrantSyncState = request.FailedAt is null ? MunicipioGrantSyncState.Pending : MunicipioGrantSyncState.Failed;
                municipio.GrantSyncError = request.LastError;
            }
        }

        return municipios;
    }

    private sealed record LatestGrantSync(Guid MunicipioId, DateTime? CompletedAt, DateTime? FailedAt, string? LastError);
}
