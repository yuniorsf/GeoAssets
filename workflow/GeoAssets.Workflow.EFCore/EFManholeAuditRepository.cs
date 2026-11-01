using GeoAssets.Workflow.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>EF Core implementation of <see cref="IManholeAuditRepository"/> — append-only, no update/delete methods.</summary>
public sealed class EFManholeAuditRepository(ServiceOrderDbContext db) : IManholeAuditRepository
{
    public async Task<ManholeAudit?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var record = await db.ManholeAudits
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

        return record is null ? null : ManholeAuditMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<ManholeAudit>> GetByConduitFeatureIdAsync(string conduitFeatureId, CancellationToken ct = default)
    {
        var records = await db.ManholeAudits
            .Where(e => e.ConduitFeatureId == conduitFeatureId)
            .OrderByDescending(e => e.AuditedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(ManholeAuditMapper.ToDomain).ToList();
    }

    public async Task AddAsync(ManholeAudit audit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        await db.ManholeAudits.AddAsync(ManholeAuditMapper.ToRecord(audit), ct).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
