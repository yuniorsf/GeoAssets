namespace GeoAssets.Workflow.Infrastructure;

/// <summary>Persistence abstraction for <see cref="ManholeAudit"/> (XD01-265). Append-only, same convention as <see cref="Contractors.IContractorUsPostingRepository"/>.</summary>
public interface IManholeAuditRepository
{
    Task<ManholeAudit?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Every audit recorded for one conduit, most recent first — "queryable per-conduit" (XD01-265's own AC).</summary>
    Task<IReadOnlyList<ManholeAudit>> GetByConduitFeatureIdAsync(string conduitFeatureId, CancellationToken ct = default);

    Task AddAsync(ManholeAudit audit, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
