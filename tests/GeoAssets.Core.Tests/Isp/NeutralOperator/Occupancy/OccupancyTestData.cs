using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy;

internal static class OccupancyTestData
{
    public const string Pole = "pole-1";

    public static readonly Guid Owner = new("0a0a0a0a-0000-4000-8000-000000000001");
    public static readonly Guid OccupantA = new("0c0c0c0c-0000-4000-8000-00000000000a");
    public static readonly Guid OccupantB = new("0c0c0c0c-0000-4000-8000-00000000000b");
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A contracted entry of <see cref="OccupantA"/> on <paramref name="slotIndex"/>, or beyond the regulated slots when null.</summary>
    public static PoleOccupancyEntry Entry(
        int? slotIndex,
        PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted,
        bool identified = true) => new()
        {
            Id = Guid.NewGuid(),
            PoleFeatureId = Pole,
            PoleOwnerOrganizationId = Owner,
            SlotIndex = slotIndex,
            Status = status,
            OccupantOrganizationId = identified ? OccupantA : null,
            CableDiameterMm = 12.5m,
        };

    /// <summary><paramref name="regulated"/> entries on slots 1.., then <paramref name="beyond"/> entries without a slot.</summary>
    public static List<PoleOccupancyEntry> Entries(int regulated, int beyond = 0) =>
        [.. Enumerable.Range(1, regulated).Select(slotIndex => Entry(slotIndex)), .. Enumerable.Range(0, beyond).Select(_ => Entry(null))];

    public static PoleOccupancy PoleOf(params PoleOccupancyEntry[] entries) =>
        PoleOccupancy.Create(Pole, Owner, entries, PoleOccupancy.FirstVersion, Now);
}
