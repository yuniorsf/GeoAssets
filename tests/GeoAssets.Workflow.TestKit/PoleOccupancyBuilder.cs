using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Fluent builder of a whole <see cref="PoleOccupancy"/> (XD01-314), for tests that need the
/// aggregate without going through a store.
/// </summary>
public sealed class PoleOccupancyBuilder
{
    public static readonly DateTimeOffset DefaultUpdatedAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly List<Func<PoleOccupancyEntryBuilder, PoleOccupancyEntryBuilder>> _entries = [];
    private string _poleFeatureId = PoleOccupancyEntryBuilder.DefaultPoleFeatureId;
    private Guid _poleOwnerOrganizationId = PoleOccupancyEntryBuilder.DefaultPoleOwnerOrganizationId;
    private int _version = PoleOccupancy.FirstVersion;
    private DateTimeOffset _updatedAt = DefaultUpdatedAt;

    public PoleOccupancyBuilder ForPole(string poleFeatureId)
    {
        _poleFeatureId = poleFeatureId;
        return this;
    }

    public PoleOccupancyBuilder OwnedBy(Guid poleOwnerOrganizationId)
    {
        _poleOwnerOrganizationId = poleOwnerOrganizationId;
        return this;
    }

    public PoleOccupancyBuilder AtVersion(int version)
    {
        _version = version;
        return this;
    }

    public PoleOccupancyBuilder UpdatedAt(DateTimeOffset updatedAt)
    {
        _updatedAt = updatedAt;
        return this;
    }

    /// <summary>Adds an entry on regulated slot <paramref name="slotIndex"/>.</summary>
    public PoleOccupancyBuilder WithSlot(
        int slotIndex,
        PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted,
        Guid? occupantOrganizationId = null,
        string? operatorCnpj = null)
    {
        _entries.Add(entry => entry.OnSlot(slotIndex).WithStatus(status).OccupiedBy(occupantOrganizationId).WithOperatorCnpj(operatorCnpj));
        return this;
    }

    /// <summary>Adds an entry beyond the regulated slots.</summary>
    public PoleOccupancyBuilder WithOverCapacityEntry(
        PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.ClandestineUnauthorized,
        Guid? occupantOrganizationId = null,
        string? operatorCnpj = null)
    {
        _entries.Add(entry => entry.BeyondRegulatedSlots().WithStatus(status).OccupiedBy(occupantOrganizationId).WithOperatorCnpj(operatorCnpj));
        return this;
    }

    /// <summary>Fills every regulated slot with a contracted entry of <paramref name="occupantOrganizationId"/>.</summary>
    public PoleOccupancyBuilder Full(Guid occupantOrganizationId)
    {
        for (var slotIndex = 1; slotIndex <= PoleAttachmentSlotMatrix.MaxSlots; slotIndex++)
            WithSlot(slotIndex, occupantOrganizationId: occupantOrganizationId);
        return this;
    }

    public PoleOccupancy Build() =>
        PoleOccupancy.Create(
            _poleFeatureId,
            _poleOwnerOrganizationId,
            _entries.Select(configure => configure(new PoleOccupancyEntryBuilder().ForPole(_poleFeatureId).OwnedBy(_poleOwnerOrganizationId)).Build()),
            _version,
            _updatedAt);
}
