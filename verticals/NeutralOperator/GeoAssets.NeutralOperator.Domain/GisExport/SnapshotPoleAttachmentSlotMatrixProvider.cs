using GeoAssets.NeutralOperator.Domain.Occupancy;

namespace GeoAssets.NeutralOperator.Domain.GisExport;

/// <summary>
/// An <see cref="IPoleAttachmentSlotMatrixProvider"/> over occupancy that was loaded beforehand
/// (XD01-314) — typically <c>await reader.GetForPolesAsync(...)</c> right before
/// <see cref="GisExportProjector"/> or the lease billing engine runs. <see cref="GetForPole"/>
/// is synchronous by contract, so it must never reach a database: it only looks into the
/// snapshot it was given.
///
/// <para><b>Over-capacity entries are not in the matrix.</b> A
/// <see cref="PoleAttachmentSlotMatrix"/> holds the six regulated slots and nothing else, so
/// <see cref="GetForPole"/> projects the regulated entries only; a pole's attachments beyond
/// them are neither counted nor billed by code that reads the matrix. They are exposed
/// separately, through <see cref="GetOverCapacityEntries"/>, for the code that reports or bills
/// them.</para>
/// </summary>
public sealed class SnapshotPoleAttachmentSlotMatrixProvider : IPoleAttachmentSlotMatrixProvider
{
    private readonly IReadOnlyDictionary<string, PoleOccupancy> _occupancyByPole;

    /// <param name="occupancyByPole">Loaded occupancy, keyed by pole feature id.</param>
    /// <exception cref="ArgumentNullException"><paramref name="occupancyByPole"/> is null.</exception>
    public SnapshotPoleAttachmentSlotMatrixProvider(IReadOnlyDictionary<string, PoleOccupancy> occupancyByPole)
    {
        ArgumentNullException.ThrowIfNull(occupancyByPole);
        _occupancyByPole = occupancyByPole;
    }

    /// <summary>A provider over <paramref name="occupancies"/>, as a reader returns them (one per pole).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="occupancies"/> is null.</exception>
    /// <exception cref="ArgumentException">Two of them are for the same pole.</exception>
    public static SnapshotPoleAttachmentSlotMatrixProvider From(IEnumerable<PoleOccupancy> occupancies)
    {
        ArgumentNullException.ThrowIfNull(occupancies);
        return new SnapshotPoleAttachmentSlotMatrixProvider(
            occupancies.ToDictionary(o => o.PoleFeatureId, StringComparer.Ordinal));
    }

    /// <summary>
    /// The pole's regulated entries as a slot matrix (a new instance per call), or null when the
    /// snapshot has nothing for that pole. A pole on file with no regulated entry gives an empty matrix.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="poleFeatureId"/> is null.</exception>
    public PoleAttachmentSlotMatrix? GetForPole(string poleFeatureId)
    {
        ArgumentNullException.ThrowIfNull(poleFeatureId);
        return _occupancyByPole.TryGetValue(poleFeatureId, out var occupancy) ? occupancy.ToSlotMatrix() : null;
    }

    /// <summary>
    /// The pole's entries beyond the regulated slots — what <see cref="GetForPole"/> leaves out.
    /// Empty when the pole has none or is not in the snapshot.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="poleFeatureId"/> is null.</exception>
    public IReadOnlyList<PoleOccupancyEntry> GetOverCapacityEntries(string poleFeatureId)
    {
        ArgumentNullException.ThrowIfNull(poleFeatureId);
        return _occupancyByPole.TryGetValue(poleFeatureId, out var occupancy) ? occupancy.OverCapacityEntries : [];
    }
}
