using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="ILampTypeRepository"/>.</summary>
public sealed class EFLampTypeRepository(IpBillingDbContext db) : ILampTypeRepository
{
    public async Task<LampType?> GetByCodeAsync(LampTypeCode code, CancellationToken ct = default)
    {
        var record = await db.LampTypes
            .Include(t => t.WattageOptions)
            .FirstOrDefaultAsync(t => t.Code == code, ct).ConfigureAwait(false);

        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<LampType>> GetAllAsync(CancellationToken ct = default)
    {
        var records = await db.LampTypes
            .Include(t => t.WattageOptions)
            .OrderBy(t => t.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(IpBillingMapper.ToDomain).ToList();
    }
}
