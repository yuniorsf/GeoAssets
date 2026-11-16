using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence.Entities;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IDistribuidoraLampWattageRepository"/>.</summary>
public sealed class EFDistribuidoraLampWattageRepository(IpBillingDbContext db) : IDistribuidoraLampWattageRepository
{
    public async Task<PagedResult<DistribuidoraLampWattage>> GetPageAsync(
        Guid distribuidoraId, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var query = db.DistribuidoraLampWattages.Where(w => w.DistribuidoraId == distribuidoraId);

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);
        var records = await query
            .OrderBy(w => w.Code)
            .ThenBy(w => w.WattageW)
            .ThenBy(w => w.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return new PagedResult<DistribuidoraLampWattage>
        {
            Items      = records.Select(IpBillingMapper.ToDomain).ToList(),
            TotalCount = totalCount,
        };
    }

    public async Task<DistribuidoraLampWattage?> FindAsync(
        Guid distribuidoraId, LampTypeCode code, int wattageW, CancellationToken ct = default)
    {
        var record = await FindRecordAsync(distribuidoraId, code, wattageW, ct).ConfigureAwait(false);
        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public Task<decimal?> GetMeanTotalBilledWAsync(Guid distribuidoraId, CancellationToken ct = default) =>
        db.DistribuidoraLampWattages
            .Where(w => w.DistribuidoraId == distribuidoraId)
            .AverageAsync(w => (decimal?)(w.WattageW + w.BallastLossW), ct);

    public async Task<DistribuidoraLampWattage> UpsertAsync(DistribuidoraLampWattage row, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        // A row staged earlier in this unit of work is not visible to a database query yet, so
        // the change tracker is consulted first — otherwise two upserts of the same key before
        // one SaveChangesAsync would stage a duplicate and trip the unique index.
        var existing = db.DistribuidoraLampWattages.Local.FirstOrDefault(w =>
                w.DistribuidoraId == row.DistribuidoraId && w.Code == row.Code && w.WattageW == row.WattageW)
            ?? await FindRecordAsync(row.DistribuidoraId, row.Code, row.WattageW, ct).ConfigureAwait(false);

        if (existing is not null)
        {
            existing.BallastLossW = row.BallastLossW;
            return IpBillingMapper.ToDomain(existing);
        }

        var record = IpBillingMapper.ToRecord(row);
        await db.DistribuidoraLampWattages.AddAsync(record, ct).ConfigureAwait(false);
        return IpBillingMapper.ToDomain(record);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.DistribuidoraLampWattages.FindAsync([id], ct).ConfigureAwait(false);
        if (record is not null) db.DistribuidoraLampWattages.Remove(record);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    private Task<DistribuidoraLampWattageRecord?> FindRecordAsync(
        Guid distribuidoraId, LampTypeCode code, int wattageW, CancellationToken ct) =>
        db.DistribuidoraLampWattages.FirstOrDefaultAsync(
            w => w.DistribuidoraId == distribuidoraId && w.Code == code && w.WattageW == wattageW, ct);
}
