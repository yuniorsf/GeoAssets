using System.Collections.Concurrent;

namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>
/// Thread-safe in-memory <see cref="IPoleAuditEvidenceRepository"/> — same stateful-singleton
/// shape as this host's other per-process registries (e.g. <c>NeutralPoleRegistry</c>). No
/// persistence-layer ticket exists yet for this data, so entries do not survive a restart; that's
/// an explicit scope cut, not an oversight — swapping in an EF Core-backed implementation later
/// only requires a new class behind this same interface.
/// </summary>
public sealed class InMemoryPoleAuditEvidenceRepository : IPoleAuditEvidenceRepository
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PoleAuditEvidenceEntry>> _evidence = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PoleLicenseRecord>> _licenseRecords = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PoleDossierRecord>> _dossierRecords = new(StringComparer.Ordinal);

    public void AddEvidence(PoleAuditEvidenceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _evidence.GetOrAdd(entry.PoleFeatureId, static _ => new ConcurrentQueue<PoleAuditEvidenceEntry>()).Enqueue(entry);
    }

    public void AddLicenseRecord(PoleLicenseRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _licenseRecords.GetOrAdd(record.PoleFeatureId, static _ => new ConcurrentQueue<PoleLicenseRecord>()).Enqueue(record);
    }

    public void AddDossierRecord(PoleDossierRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _dossierRecords.GetOrAdd(record.PoleFeatureId, static _ => new ConcurrentQueue<PoleDossierRecord>()).Enqueue(record);
    }

    public PoleDefenseBundle BuildDefenseBundle(string poleFeatureId, DateTimeOffset validInstallationTimestampUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(poleFeatureId);

        var evidence = _evidence.TryGetValue(poleFeatureId, out var evidenceQueue)
            ? evidenceQueue.Where(e => e.CapturedAtUtc >= validInstallationTimestampUtc).ToList()
            : (IReadOnlyList<PoleAuditEvidenceEntry>)[];

        var licenseRecords = _licenseRecords.TryGetValue(poleFeatureId, out var licenseQueue)
            ? licenseQueue.ToList()
            : (IReadOnlyList<PoleLicenseRecord>)[];

        var dossierRecords = _dossierRecords.TryGetValue(poleFeatureId, out var dossierQueue)
            ? dossierQueue.ToList()
            : (IReadOnlyList<PoleDossierRecord>)[];

        return new PoleDefenseBundle(poleFeatureId, evidence, licenseRecords, dossierRecords);
    }
}
