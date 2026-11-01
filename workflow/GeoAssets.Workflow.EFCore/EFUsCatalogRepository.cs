using GeoAssets.Workflow.Contractors;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>EF Core implementation of <see cref="IUsCatalogRepository"/>.</summary>
public sealed class EFUsCatalogRepository(ServiceOrderDbContext db) : IUsCatalogRepository
{
    public async Task<UsCatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.UsCatalogItems
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

        return record is null ? null : UsCatalogItemMapper.ToDomain(record);
    }

    public async Task<UsCatalogItem?> FindEffectiveAsync(Guid organizationId, string itemCode, DateTime asOfUtc, CancellationToken ct = default)
    {
        var record = await db.UsCatalogItems
            .Where(e => e.OrganizationId == organizationId && e.ItemCode == itemCode)
            .Where(e => e.EffectiveFrom <= asOfUtc && (e.EffectiveTo == null || asOfUtc < e.EffectiveTo))
            .OrderByDescending(e => e.EffectiveFrom)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return record is null ? null : UsCatalogItemMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<UsCatalogItem>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
    {
        var records = await db.UsCatalogItems
            .Where(e => e.OrganizationId == organizationId)
            .OrderBy(e => e.ItemCode).ThenBy(e => e.EffectiveFrom)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(UsCatalogItemMapper.ToDomain).ToList();
    }

    public async Task AddAsync(UsCatalogItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await db.UsCatalogItems.AddAsync(UsCatalogItemMapper.ToRecord(item), ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(UsCatalogItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var existing = await db.UsCatalogItems
            .FirstOrDefaultAsync(e => e.Id == item.Id, ct).ConfigureAwait(false);
        if (existing is null) return;

        var updated = UsCatalogItemMapper.ToRecord(item);
        existing.ItemCode = updated.ItemCode;
        existing.Description = updated.Description;
        existing.Unit = updated.Unit;
        existing.BaseUsFactor = updated.BaseUsFactor;
        existing.RegionalMultiplier = updated.RegionalMultiplier;
        existing.MonetaryConversionRate = updated.MonetaryConversionRate;
        existing.EffectiveFrom = updated.EffectiveFrom;
        existing.EffectiveTo = updated.EffectiveTo;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.UsCatalogItems
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (record is not null) db.UsCatalogItems.Remove(record);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
