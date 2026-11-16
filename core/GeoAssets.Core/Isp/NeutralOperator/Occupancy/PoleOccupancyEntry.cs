namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// One attachment point on a pole and who holds it (XD01-314).
///
/// A pole has <see cref="PoleAttachmentSlotMatrix.MaxSlots"/> (6) regulated slots. An entry
/// with a <see cref="SlotIndex"/> sits on one of them. Reality has poles with more occupants
/// than that, and every one of them is still recorded: an entry <b>without</b> a
/// <see cref="SlotIndex"/> is an attachment beyond the regulated slots (an "over-capacity
/// entry"). Nothing caps how many of those a pole may have.
///
/// An entry is a value: it is only ever read from, or handed whole to,
/// <see cref="PoleOccupancy"/>, which is what checks it (see <see cref="PoleOccupancy.Create"/>
/// for the rules).
/// </summary>
public sealed record PoleOccupancyEntry
{
    /// <summary>Longest <see cref="PoleFeatureId"/> / <see cref="AttachedCableFeatureId"/> a store accepts.</summary>
    public const int MaxFeatureIdLength = 64;

    /// <summary>Longest <see cref="OperatorCnpj"/>: a punctuated CNPJ (<c>00.000.000/0000-00</c>).</summary>
    public const int MaxOperatorCnpjLength = 18;

    /// <summary>Longest <see cref="AnatelLicenseNumber"/>.</summary>
    public const int MaxAnatelLicenseNumberLength = 64;

    /// <summary>Decimal places kept for <see cref="CableDiameterMm"/> and <see cref="CableLoadDaN"/>.</summary>
    public const int MeasurementDecimals = 3;

    /// <summary>Greatest <see cref="CableDiameterMm"/> a store accepts.</summary>
    public const decimal MaxCableDiameterMm = 99_999.999m;

    /// <summary>Greatest <see cref="CableLoadDaN"/> a store accepts.</summary>
    public const decimal MaxCableLoadDaN = 999_999_999.999m;

    /// <summary>
    /// Stable identity of the entry, chosen by whoever creates it. It is what identifies an
    /// over-capacity entry across writes (it has no slot index to go by). Never
    /// <see cref="Guid.Empty"/>; unique within its pole.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>The <c>GeoFeature.Id</c> of the pole.</summary>
    public required string PoleFeatureId { get; init; }

    /// <summary>The organization that owns the pole, and therefore this record.</summary>
    public required Guid PoleOwnerOrganizationId { get; init; }

    /// <summary>
    /// The regulated slot, 1..<see cref="PoleAttachmentSlotMatrix.MaxSlots"/>, or null for an
    /// over-capacity entry.
    /// </summary>
    public int? SlotIndex { get; init; }

    public required PoleAttachmentOccupantStatus Status { get; init; }

    /// <summary>The occupant, when it is known as an organization. Null means unidentified.</summary>
    public Guid? OccupantOrganizationId { get; init; }

    /// <summary>CNPJ of the attaching operator, when known. Informative: identity is <see cref="OccupantOrganizationId"/>.</summary>
    public string? OperatorCnpj { get; init; }

    /// <summary>ANATEL SCM licence number backing the attachment, when known.</summary>
    public string? AnatelLicenseNumber { get; init; }

    /// <summary>Attached cable diameter, in millimetres.</summary>
    public decimal CableDiameterMm { get; init; }

    /// <summary>Declared mechanical load of the attachment, in daN (see <see cref="PoleAttachmentSlot.CableLoadDaN"/>).</summary>
    public decimal CableLoadDaN { get; init; }

    /// <summary>The attached telecom-cable <c>GeoFeature.Id</c>, when known.</summary>
    public string? AttachedCableFeatureId { get; init; }

    /// <summary>True when the entry sits on one of the regulated slots.</summary>
    public bool IsRegulated => SlotIndex is not null;

    /// <summary>True when the entry is an attachment beyond the regulated slots (no <see cref="SlotIndex"/>).</summary>
    public bool IsBeyondRegulatedSlots => SlotIndex is null;

    /// <summary>
    /// True when a cable is physically there: every status except
    /// <see cref="PoleAttachmentOccupantStatus.ActiveReservation"/> (a slot held for a future
    /// tenant). The same reading as <c>GisExportProjector</c>'s occupied-slot count.
    /// </summary>
    public bool IsOccupied => Status != PoleAttachmentOccupantStatus.ActiveReservation;

    /// <summary>True when a cable is there and nobody is on file as its occupant organization.</summary>
    public bool IsUnidentified => IsOccupied && OccupantOrganizationId is null;

    /// <summary>
    /// True when the entry is an active contract, i.e. its occupant can be notified:
    /// <see cref="PoleAttachmentOccupantStatus.AuthorizedContracted"/> with a known occupant organization.
    /// </summary>
    public bool IsContracted =>
        Status == PoleAttachmentOccupantStatus.AuthorizedContracted && OccupantOrganizationId is not null;
}
