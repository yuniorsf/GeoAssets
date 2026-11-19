using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Short factories for the <see cref="OccupancyChange"/>s tests stage (XD01-315). Payload
/// defaults: a contracted occupant <see cref="DefaultOccupantOrganizationId"/> with a CNPJ and a
/// 9.5 mm cable.
/// </summary>
public static class OccupancyChangeBuilder
{
    public static readonly Guid DefaultOccupantOrganizationId = new("0c0c0c0c-0000-4000-8000-000000000002");

    /// <summary>An Add on a regulated slot (<paramref name="slotIndex"/>) or, with null, beyond them.</summary>
    public static OccupancyChange Add(
        int? slotIndex, Guid? entryId = null,
        PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted) => new()
        {
            Kind = OccupancyChangeKind.Add,
            SlotIndex = slotIndex,
            EntryId = entryId ?? Guid.NewGuid(),
            Status = status,
            OccupantOrganizationId = DefaultOccupantOrganizationId,
            OperatorCnpj = "98.765.432/0001-10",
            CableDiameterMm = 9.5m,
            CableLoadDaN = 80m,
        };

    /// <summary>An Add beyond the regulated slots: an over-capacity entry.</summary>
    public static OccupancyChange AddBeyondRegulatedSlots(Guid? entryId = null) => Add(slotIndex: null, entryId);

    /// <summary>A Modify of the entry on a regulated slot, to <paramref name="status"/> with the default payload.</summary>
    public static OccupancyChange ModifySlot(int slotIndex, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted) =>
        Add(slotIndex, status: status) with { Kind = OccupancyChangeKind.Modify, EntryId = null };

    /// <summary>A Modify of an entry addressed by its id — the way to reach an over-capacity entry.</summary>
    public static OccupancyChange ModifyEntry(Guid entryId, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted) =>
        Add(slotIndex: null, entryId, status) with { Kind = OccupancyChangeKind.Modify };

    public static OccupancyChange RemoveSlot(int slotIndex) => new() { Kind = OccupancyChangeKind.Remove, SlotIndex = slotIndex };

    public static OccupancyChange RemoveEntry(Guid entryId) => new() { Kind = OccupancyChangeKind.Remove, EntryId = entryId };

    public static OccupancyChange MarkSlotUnidentified(int slotIndex) => new() { Kind = OccupancyChangeKind.MarkUnidentified, SlotIndex = slotIndex };

    public static OccupancyChange MarkEntryUnidentified(Guid entryId) => new() { Kind = OccupancyChangeKind.MarkUnidentified, EntryId = entryId };
}
