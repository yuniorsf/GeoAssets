using System.Collections.Concurrent;

namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// Thread-safe in-memory <see cref="ITechnicianComplianceRepository"/> — same stateful-singleton
/// shape as this codebase's other per-process sync snapshots (e.g.
/// <c>InMemoryPoleAuditEvidenceRepository</c>). No persistence-layer ticket exists yet for this
/// data, so entries do not survive a restart; that's an explicit scope cut, not an oversight —
/// swapping in an EF Core-backed implementation later only requires a new class behind this same
/// interface, and the nightly sync repopulates this store from scratch every run anyway.
/// </summary>
public sealed class InMemoryTechnicianComplianceRepository : ITechnicianComplianceRepository
{
    private readonly ConcurrentDictionary<string, TechnicianComplianceRecord> _records = new(StringComparer.Ordinal);

    public Task<TechnicianComplianceRecord?> GetAsync(string technicianId, CancellationToken ct = default) =>
        Task.FromResult(_records.GetValueOrDefault(technicianId));

    public Task UpsertAsync(TechnicianComplianceRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[record.TechnicianId] = record;
        return Task.CompletedTask;
    }
}
