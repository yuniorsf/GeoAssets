namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// Write side of the pole occupancy store (XD01-314). Like <see cref="IPoleOccupancyReader"/>,
/// an adapter is scoped to the caller's organization and writes only poles that organization owns.
/// </summary>
public interface IPoleOccupancyWriter
{
    /// <summary>
    /// Replaces, as a whole, the entries on file for one pole and returns the pole as now stored.
    ///
    /// <para><b>Adapter contract.</b></para>
    /// <list type="number">
    ///   <item><description><b>Arguments, then cancellation.</b> A token already cancelled on
    ///   entry throws <see cref="OperationCanceledException"/> and writes nothing.</description></item>
    ///   <item><description><b>Version.</b> <paramref name="expectedVersion"/> must be the stored
    ///   <see cref="PoleOccupancy.Version"/>, or <see cref="PoleOccupancy.UnsavedVersion"/> (0)
    ///   when the pole has nothing on file. Anything else throws
    ///   <see cref="OccupancyConflictException"/> naming the pole — also when two writers race on
    ///   the same version: exactly one of them wins. This is checked before the entries are.</description></item>
    ///   <item><description><b>Rules.</b> The entries must form a valid pole
    ///   (<see cref="PoleOccupancy.Create"/>: owner equal to the adapter's organization, at most
    ///   one entry per regulated slot, slot index 1..6 or absent, …) and, against what is stored,
    ///   a legal revision (<see cref="PoleOccupancy.Revise"/>: status changes on a regulated slot
    ///   follow <see cref="PoleAttachmentSlotTransitions"/>). The exceptions are those two
    ///   methods' own.</description></item>
    ///   <item><description><b>Over capacity is stored, never refused.</b> Any number of entries
    ///   without a slot index is accepted and kept.</description></item>
    ///   <item><description><b>Result.</b> The first write stores
    ///   <see cref="PoleOccupancy.FirstVersion"/> (1); every later one the expected version plus
    ///   one, even when the entries are unchanged. <see cref="PoleOccupancy.UpdatedAt"/> is the
    ///   adapter's clock at the write. An empty <paramref name="entries"/> empties the pole but
    ///   keeps it on file with its version history.</description></item>
    ///   <item><description><b>All or nothing.</b> When the call throws, the stored pole is
    ///   exactly what it was. An adapter over a unit of work the caller controls (the EF Core
    ///   one, inside the caller's database transaction) takes part in it, so the write commits or
    ///   rolls back with the caller's other changes.</description></item>
    /// </list>
    /// </summary>
    /// <param name="poleFeatureId">The pole.</param>
    /// <param name="expectedVersion">The version the caller's decision was based on; 0 for a pole not on file.</param>
    /// <param name="entries">The complete new set of entries; may be empty.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentException"><paramref name="poleFeatureId"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedVersion"/> is negative.</exception>
    /// <exception cref="OccupancyConflictException">The stored version is not <paramref name="expectedVersion"/>.</exception>
    Task<PoleOccupancy> ReplaceEntriesAsync(
        string poleFeatureId,
        int expectedVersion,
        IReadOnlyCollection<PoleOccupancyEntry> entries,
        CancellationToken ct);
}
