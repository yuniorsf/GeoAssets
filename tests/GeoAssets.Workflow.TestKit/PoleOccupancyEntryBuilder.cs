using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Fluent builder of one <see cref="PoleOccupancyEntry"/> (XD01-314). Defaults: a contracted,
/// identified occupant on regulated slot 1 of pole <see cref="DefaultPoleFeatureId"/>, owned by
/// <see cref="DefaultPoleOwnerOrganizationId"/>; a fresh id on every <see cref="Build"/> unless
/// <see cref="WithId"/> fixes one.
/// </summary>
public sealed class PoleOccupancyEntryBuilder
{
    public const string DefaultPoleFeatureId = "pole-100";

    /// <summary>The same organization the other TestKit builders default to.</summary>
    public static readonly Guid DefaultPoleOwnerOrganizationId = ServiceOrderTargetBuilder.DefaultOrganizationId;

    public static readonly Guid DefaultOccupantOrganizationId = new("0c0c0c0c-0000-4000-8000-000000000001");

    private Guid? _id;
    private string _poleFeatureId = DefaultPoleFeatureId;
    private Guid _poleOwnerOrganizationId = DefaultPoleOwnerOrganizationId;
    private int? _slotIndex = 1;
    private PoleAttachmentOccupantStatus _status = PoleAttachmentOccupantStatus.AuthorizedContracted;
    private Guid? _occupantOrganizationId = DefaultOccupantOrganizationId;
    private string? _operatorCnpj;
    private string? _anatelLicenseNumber;
    private decimal _cableDiameterMm = 12.5m;
    private decimal _cableLoadDaN;
    private string? _attachedCableFeatureId;

    public PoleOccupancyEntryBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public PoleOccupancyEntryBuilder ForPole(string poleFeatureId)
    {
        _poleFeatureId = poleFeatureId;
        return this;
    }

    public PoleOccupancyEntryBuilder OwnedBy(Guid poleOwnerOrganizationId)
    {
        _poleOwnerOrganizationId = poleOwnerOrganizationId;
        return this;
    }

    /// <summary>Puts the entry on a regulated slot (1..6 to be valid).</summary>
    public PoleOccupancyEntryBuilder OnSlot(int slotIndex)
    {
        _slotIndex = slotIndex;
        return this;
    }

    /// <summary>Makes it an over-capacity entry: no slot index.</summary>
    public PoleOccupancyEntryBuilder BeyondRegulatedSlots()
    {
        _slotIndex = null;
        return this;
    }

    public PoleOccupancyEntryBuilder WithStatus(PoleAttachmentOccupantStatus status)
    {
        _status = status;
        return this;
    }

    public PoleOccupancyEntryBuilder OccupiedBy(Guid? occupantOrganizationId)
    {
        _occupantOrganizationId = occupantOrganizationId;
        return this;
    }

    /// <summary>No occupant organization on file.</summary>
    public PoleOccupancyEntryBuilder Unidentified() => OccupiedBy(null);

    public PoleOccupancyEntryBuilder WithOperatorCnpj(string? operatorCnpj)
    {
        _operatorCnpj = operatorCnpj;
        return this;
    }

    public PoleOccupancyEntryBuilder WithAnatelLicenseNumber(string? anatelLicenseNumber)
    {
        _anatelLicenseNumber = anatelLicenseNumber;
        return this;
    }

    public PoleOccupancyEntryBuilder WithCableDiameterMm(decimal cableDiameterMm)
    {
        _cableDiameterMm = cableDiameterMm;
        return this;
    }

    public PoleOccupancyEntryBuilder WithCableLoadDaN(decimal cableLoadDaN)
    {
        _cableLoadDaN = cableLoadDaN;
        return this;
    }

    public PoleOccupancyEntryBuilder WithAttachedCableFeatureId(string? attachedCableFeatureId)
    {
        _attachedCableFeatureId = attachedCableFeatureId;
        return this;
    }

    public PoleOccupancyEntry Build() => new()
    {
        Id = _id ?? Guid.NewGuid(),
        PoleFeatureId = _poleFeatureId,
        PoleOwnerOrganizationId = _poleOwnerOrganizationId,
        SlotIndex = _slotIndex,
        Status = _status,
        OccupantOrganizationId = _occupantOrganizationId,
        OperatorCnpj = _operatorCnpj,
        AnatelLicenseNumber = _anatelLicenseNumber,
        CableDiameterMm = _cableDiameterMm,
        CableLoadDaN = _cableLoadDaN,
        AttachedCableFeatureId = _attachedCableFeatureId,
    };
}
