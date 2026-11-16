using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Shared.Services.Occupancy;

/// <summary>
/// One line of <c>OccupancyChangeEditor</c>'s table (XD01-315): a regulated slot or an entry
/// beyond them, what is on file there, and the change pending on it.
/// </summary>
/// <param name="Key">Stable key of the row within the draft (<c>slot:3</c>, <c>entry:{id}</c>).</param>
/// <param name="SlotIndex">The regulated slot; null for an entry beyond the regulated slots.</param>
/// <param name="Current">What is on file there now; null for a free slot or an entry being added.</param>
/// <param name="Change">The pending change; null when the row is untouched.</param>
/// <param name="Projected">What the row holds once the changes apply; null when it ends up empty or the draft does not apply.</param>
public sealed record OccupancyDraftRow(
    string Key, int? SlotIndex, PoleOccupancyEntry? Current, OccupancyChange? Change, PoleOccupancyEntry? Projected)
{
    /// <summary>True for a row beyond the six regulated slots.</summary>
    public bool IsBeyondRegulatedSlots => SlotIndex is null;

    /// <summary>True when there is nothing on file here and nothing pending: a free regulated slot.</summary>
    public bool IsFree => Current is null && Change is null;

    /// <summary>True when the pending change addresses an entry that is no longer on the pole.</summary>
    public bool IsOrphan => Current is null && Change is { Kind: not OccupancyChangeKind.Add };
}
