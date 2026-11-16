using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy;

/// <summary>Poles, entries and changes for the staged-change tests (XD01-315).</summary>
internal static class OccupancyTestPoles
{
    public const string Pole = "pole-1";

    public static readonly Guid Owner = new("0a0a0a0a-0000-4000-8000-000000000001");
    public static readonly Guid OccupantA = new("0c0c0c0c-0000-4000-8000-00000000000a");
    public static readonly Guid OccupantB = new("0c0c0c0c-0000-4000-8000-00000000000b");
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Deterministic entry ids: <c>Id(7)</c> ends in <c>…07</c>.</summary>
    public static Guid Id(int n) => new($"0e0e0e0e-0000-4000-8000-{n:D12}");

    public static PoleOccupancyEntry Entry(
        int id, int? slotIndex,
        PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted,
        Guid? occupant = null, bool identified = true) => new()
        {
            Id = Id(id),
            PoleFeatureId = Pole,
            PoleOwnerOrganizationId = Owner,
            SlotIndex = slotIndex,
            Status = status,
            OccupantOrganizationId = identified ? occupant ?? OccupantA : null,
            OperatorCnpj = identified ? "12.345.678/0001-90" : null,
            CableDiameterMm = 12.5m,
            CableLoadDaN = 100m,
        };

    /// <summary>A pole on file at <paramref name="version"/> with these entries.</summary>
    public static PoleOccupancy PoleOf(int version, params PoleOccupancyEntry[] entries) =>
        PoleOccupancy.Create(Pole, Owner, entries, version, Now);

    /// <summary>A pole with <paramref name="regulated"/> contracted slots (ids 1..) and <paramref name="beyond"/> over-capacity entries (ids 101..).</summary>
    public static PoleOccupancy PoleWith(int regulated, int beyond, int version = 3) => PoleOf(
        version,
        [
            .. Enumerable.Range(1, regulated).Select(slot => Entry(slot, slot)),
            .. Enumerable.Range(1, beyond).Select(n => Entry(100 + n, null)),
        ]);

    public static OccupancyChange Add(int id, int? slotIndex, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted, Guid? occupant = null) => new()
    {
        Kind = OccupancyChangeKind.Add,
        SlotIndex = slotIndex,
        EntryId = Id(id),
        Status = status,
        OccupantOrganizationId = occupant ?? OccupantB,
        OperatorCnpj = "98.765.432/0001-10",
        CableDiameterMm = 9.5m,
        CableLoadDaN = 80m,
    };

    public static OccupancyChange OnSlot(OccupancyChangeKind kind, int slotIndex, PoleAttachmentOccupantStatus? status = null) =>
        new() { Kind = kind, SlotIndex = slotIndex, Status = status, OccupantOrganizationId = OccupantB };

    public static OccupancyChange OnEntry(OccupancyChangeKind kind, int id, PoleAttachmentOccupantStatus? status = null) =>
        new() { Kind = kind, EntryId = Id(id), Status = status, OccupantOrganizationId = OccupantB };

    public static OccupancyChangeSet Stage(PoleOccupancy? baseline, params OccupancyChange[] changes) =>
        OccupancyChangeSet.Stage("ord-1", "tgt-1", Pole, Owner, changes, baseline, "crew-1", Now);
}
