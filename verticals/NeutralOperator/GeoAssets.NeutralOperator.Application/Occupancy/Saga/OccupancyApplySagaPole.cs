using GeoAssets.NeutralOperator.Domain.Occupancy;

namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// One pole an occupancy approval wrote while its apply saga is open (XD01-383): what it held before, so a rejected batch
/// restores it, and the version the approval left it at, so the restore refuses to overwrite anything written since.
/// </summary>
/// <param name="PoleFeatureId">The pole.</param>
/// <param name="BaselineVersion">The pole's version before the approval; <see cref="PoleOccupancy.UnsavedVersion"/> when it had nothing on file.</param>
/// <param name="AppliedVersion">The version the approval's write left the pole at.</param>
/// <param name="BaselineEntries">The pole's entries before the approval; empty when it had nothing on file.</param>
public sealed record OccupancyApplySagaPole(
    string PoleFeatureId,
    int BaselineVersion,
    int AppliedVersion,
    IReadOnlyList<PoleOccupancyEntry> BaselineEntries);
