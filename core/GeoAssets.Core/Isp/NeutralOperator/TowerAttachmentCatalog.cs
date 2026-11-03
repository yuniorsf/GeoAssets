using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>
/// The attachment-catalog aggregate for one wireless tower (XD01-266) — the tower analogue of
/// <see cref="PoleAttachmentSlotMatrix"/>. A tower has no fixed slot ceiling, so this tracks an
/// open catalog of <see cref="TowerAttachmentRecord"/> entries keyed by
/// <see cref="TowerAttachmentRecord.RecordId"/> rather than a bounded 1-6 index. Equipment
/// topology is read through <see cref="TopoGraph"/> via the same
/// <see cref="PoleAttachmentSlotMatrix.SupportsEdgeKind"/> "supports" edge kind poles use, so
/// pole and tower records share one multi-tenant query/report surface — both are plain
/// GeoFeature-id-keyed aggregates over whatever already-tenant-scoped feature collection the
/// caller's <c>IAssetProvider</c> returns; no tower-specific persistence or query path is
/// introduced here.
/// </summary>
public sealed class TowerAttachmentCatalog
{
    private readonly List<TowerAttachmentRecord> _records = [];

    public required string TowerFeatureId { get; init; }

    public IReadOnlyList<TowerAttachmentRecord> Records => _records;

    /// <summary>Adds a new record. Throws when <paramref name="record"/>'s
    /// <see cref="TowerAttachmentRecord.RecordId"/> is already registered on this tower, or when
    /// a kind-specific required field is missing (<see cref="TowerAttachmentKind.Bracket"/>
    /// requires <see cref="TowerAttachmentRecord.ElevationMeters"/>;
    /// <see cref="TowerAttachmentKind.PowerMeter"/> requires
    /// <see cref="TowerAttachmentRecord.MeteredPowerDrawWatts"/>).</summary>
    public void AddRecord(TowerAttachmentRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_records.Any(r => string.Equals(r.RecordId, record.RecordId, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Record id '{record.RecordId}' is already registered on tower '{TowerFeatureId}'.");

        if (record.Kind == TowerAttachmentKind.Bracket && record.ElevationMeters is null)
            throw new ArgumentException(
                "A bracket record must specify ElevationMeters.", nameof(record));

        if (record.Kind == TowerAttachmentKind.PowerMeter && record.MeteredPowerDrawWatts is null)
            throw new ArgumentException(
                "A power-meter record must specify MeteredPowerDrawWatts.", nameof(record));

        _records.Add(record);
    }

    /// <summary>Removes a record by id. Throws when no record with that id is registered.</summary>
    public void RemoveRecord(string recordId)
    {
        var record = _records.FirstOrDefault(r => string.Equals(r.RecordId, recordId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Tower '{TowerFeatureId}' has no record with id '{recordId}'.");

        _records.Remove(record);
    }

    /// <summary>The equipment/cable features this tower physically supports, via the shared
    /// "supports" <c>TopoEdge</c> kind — same convention
    /// <see cref="PoleAttachmentSlotMatrix.GetAttachedCableFeatures"/> uses for poles.</summary>
    public IReadOnlyList<GeoFeature> GetAttachedEquipmentFeatures(IEnumerable<GeoFeature> features) =>
        TopoGraph.GetNeighbors(TowerFeatureId, features, PoleAttachmentSlotMatrix.SupportsEdgeKind);
}
