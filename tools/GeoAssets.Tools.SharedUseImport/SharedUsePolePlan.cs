using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// What the import will hold on file for one pole: its complete occupancy, built and checked by
/// <see cref="PoleOccupancy.Create"/>, and where it came from.
/// </summary>
/// <param name="Occupancy">The pole's occupancy as the import builds it (version and stamp are the plan's, not the store's).</param>
/// <param name="LegacyPoleIds">The legacy poles mapped to this pole feature, ascending.</param>
/// <param name="LegacyRowIds">The legacy rows imported onto it, ascending.</param>
/// <param name="FlaggedRows">The flags raised on those rows, by row id then flag.</param>
internal sealed record SharedUsePolePlan(
    PoleOccupancy Occupancy,
    IReadOnlyList<int> LegacyPoleIds,
    IReadOnlyList<int> LegacyRowIds,
    IReadOnlyList<FlaggedSharedUseRow> FlaggedRows)
{
    /// <summary>The pole feature id.</summary>
    public string PoleFeatureId => Occupancy.PoleFeatureId;

    /// <summary>The pole-level flags, in declaration order.</summary>
    public IReadOnlyList<SharedUsePoleFlag> Flags
    {
        get
        {
            var flags = new List<SharedUsePoleFlag>();
            if (Occupancy.IsOverCapacity) flags.Add(SharedUsePoleFlag.OverCapacity);
            if (Occupancy.HasUnidentifiedOccupant) flags.Add(SharedUsePoleFlag.UnidentifiedOccupant);
            if (LegacyPoleIds.Count > 1) flags.Add(SharedUsePoleFlag.MergedLegacyPoles);
            return flags;
        }
    }
}
