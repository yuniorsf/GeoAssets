using GeoAssets.NeutralOperator.Domain;

namespace GeoAssets.NeutralOperator.Application.SharedUse;

/// <summary>
/// One occupancy entry on a pole of the order that the fan-out does not dispatch to, and why.
/// Carries the entry's position and status, never who the occupant is beyond that: the reader of
/// this record is the pole owner, who can open the pole's occupancy for the rest.
/// </summary>
/// <param name="PoleFeatureId">The pole the entry is on.</param>
/// <param name="EntryId">The occupancy entry's id.</param>
/// <param name="SlotIndex">The regulated slot the entry sits on; null for an entry beyond the regulated slots.</param>
/// <param name="Status">The entry's status when the plan was made.</param>
/// <param name="Reason">Why the entry is skipped.</param>
public sealed record SharedUseSkippedEntry(
    string PoleFeatureId,
    Guid EntryId,
    int? SlotIndex,
    PoleAttachmentOccupantStatus Status,
    SharedUseSkipReason Reason);
