using GeoAssets.Workflow.FieldWork;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>EF Core implementation of <see cref="IPavingMeasurementRepository"/> — append-only, no update/delete methods.</summary>
public sealed class EFPavingMeasurementRepository(ServiceOrderDbContext db) : IPavingMeasurementRepository
{
    public async Task<PavingMeasurement?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.PavingMeasurements
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

        return record is null ? null : PavingMeasurementMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<PavingMeasurement>> GetByParentServiceOrderIdAsync(string parentServiceOrderId, CancellationToken ct = default)
    {
        var records = await db.PavingMeasurements
            .Where(e => e.ParentServiceOrderId == parentServiceOrderId)
            .OrderBy(e => e.RecordedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(PavingMeasurementMapper.ToDomain).ToList();
    }

    public async Task AddAsync(PavingMeasurement measurement, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        await db.PavingMeasurements.AddAsync(PavingMeasurementMapper.ToRecord(measurement), ct).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
