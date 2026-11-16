using System.Globalization;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Shared.Services.Occupancy;

/// <summary>
/// The text a user types to describe one occupancy entry in <c>OccupancyChangeEditor</c>
/// (XD01-315), with the checks that can be made before anything is sent: each field on its own
/// (<see cref="Validate"/>) and the conversion to a change's payload (<see cref="ToChange"/>).
/// Pure — no rendering, no I/O — so it is tested without a browser.
/// </summary>
public sealed class OccupancyEntryForm
{
    public const string StatusField = "status";
    public const string OrganizationField = "organization";
    public const string CnpjField = "cnpj";
    public const string LicenseField = "license";
    public const string DiameterField = "diameter";
    public const string LoadField = "load";
    public const string CableField = "cable";

    public PoleAttachmentOccupantStatus Status { get; set; } = PoleAttachmentOccupantStatus.AuthorizedContracted;

    /// <summary>The occupant organization's id (a GUID), or blank when it is not known.</summary>
    public string OccupantOrganizationId { get; set; } = string.Empty;

    public string OperatorCnpj { get; set; } = string.Empty;
    public string AnatelLicenseNumber { get; set; } = string.Empty;

    /// <summary>Cable diameter in millimetres, as typed (invariant decimal point).</summary>
    public string CableDiameterMm { get; set; } = "0";

    /// <summary>Cable load in daN, as typed (invariant decimal point).</summary>
    public string CableLoadDaN { get; set; } = "0";

    public string AttachedCableFeatureId { get; set; } = string.Empty;

    /// <summary>A form pre-filled with an existing entry's data, for modifying it.</summary>
    public static OccupancyEntryForm From(PoleOccupancyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new OccupancyEntryForm
        {
            Status = entry.Status,
            OccupantOrganizationId = entry.OccupantOrganizationId?.ToString("D") ?? string.Empty,
            OperatorCnpj = entry.OperatorCnpj ?? string.Empty,
            AnatelLicenseNumber = entry.AnatelLicenseNumber ?? string.Empty,
            CableDiameterMm = entry.CableDiameterMm.ToString(CultureInfo.InvariantCulture),
            CableLoadDaN = entry.CableLoadDaN.ToString(CultureInfo.InvariantCulture),
            AttachedCableFeatureId = entry.AttachedCableFeatureId ?? string.Empty,
        };
    }

    /// <summary>A form pre-filled with the payload of a pending Add or Modify, for editing it further.</summary>
    public static OccupancyEntryForm From(OccupancyChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new OccupancyEntryForm
        {
            Status = change.Status ?? PoleAttachmentOccupantStatus.AuthorizedContracted,
            OccupantOrganizationId = change.OccupantOrganizationId?.ToString("D") ?? string.Empty,
            OperatorCnpj = change.OperatorCnpj ?? string.Empty,
            AnatelLicenseNumber = change.AnatelLicenseNumber ?? string.Empty,
            CableDiameterMm = change.CableDiameterMm.ToString(CultureInfo.InvariantCulture),
            CableLoadDaN = change.CableLoadDaN.ToString(CultureInfo.InvariantCulture),
            AttachedCableFeatureId = change.AttachedCableFeatureId ?? string.Empty,
        };
    }

    /// <summary>
    /// The problems of each field, as field name → localization key; empty when the form can be
    /// turned into a change.
    /// </summary>
    /// <param name="beyondRegulatedSlots">True when the entry has no regulated slot: it cannot be a reservation.</param>
    public IReadOnlyDictionary<string, string> Validate(bool beyondRegulatedSlots)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!Enum.IsDefined(Status))
            errors[StatusField] = "occupancy.validation.status";
        else if (beyondRegulatedSlots && Status == PoleAttachmentOccupantStatus.ActiveReservation)
            errors[StatusField] = "occupancy.validation.reservationBeyondSlots";

        if (!string.IsNullOrWhiteSpace(OccupantOrganizationId)
            && (!Guid.TryParse(OccupantOrganizationId.Trim(), out var organizationId) || organizationId == Guid.Empty))
        {
            errors[OrganizationField] = "occupancy.validation.organization";
        }

        if (OperatorCnpj.Trim().Length > PoleOccupancyEntry.MaxOperatorCnpjLength)
            errors[CnpjField] = "occupancy.validation.cnpj";
        if (AnatelLicenseNumber.Trim().Length > PoleOccupancyEntry.MaxAnatelLicenseNumberLength)
            errors[LicenseField] = "occupancy.validation.license";
        if (AttachedCableFeatureId.Trim().Length > PoleOccupancyEntry.MaxFeatureIdLength)
            errors[CableField] = "occupancy.validation.cable";

        if (!TryMeasurement(CableDiameterMm, PoleOccupancyEntry.MaxCableDiameterMm, out _))
            errors[DiameterField] = "occupancy.validation.diameter";
        if (!TryMeasurement(CableLoadDaN, PoleOccupancyEntry.MaxCableLoadDaN, out _))
            errors[LoadField] = "occupancy.validation.load";

        return errors;
    }

    /// <summary>
    /// The change carrying this form as its payload. Call only when <see cref="Validate"/>
    /// reported nothing; blank text becomes null.
    /// </summary>
    /// <exception cref="InvalidOperationException">The form does not validate.</exception>
    public OccupancyChange ToChange(OccupancyChangeKind kind, int? slotIndex, Guid? entryId)
    {
        if (Validate(beyondRegulatedSlots: false).Keys.Any(field => !string.Equals(field, StatusField, StringComparison.Ordinal)))
            throw new InvalidOperationException("The occupancy entry form has invalid fields.");

        TryMeasurement(CableDiameterMm, PoleOccupancyEntry.MaxCableDiameterMm, out var diameter);
        TryMeasurement(CableLoadDaN, PoleOccupancyEntry.MaxCableLoadDaN, out var load);

        return new OccupancyChange
        {
            Kind = kind,
            SlotIndex = slotIndex,
            EntryId = entryId,
            Status = Status,
            OccupantOrganizationId = Guid.TryParse(OccupantOrganizationId.Trim(), out var organizationId) ? organizationId : null,
            OperatorCnpj = NullIfBlank(OperatorCnpj),
            AnatelLicenseNumber = NullIfBlank(AnatelLicenseNumber),
            CableDiameterMm = diameter,
            CableLoadDaN = load,
            AttachedCableFeatureId = NullIfBlank(AttachedCableFeatureId),
        };
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A non-negative number up to <paramref name="max"/> with at most three decimals; blank reads as 0.</summary>
    private static bool TryMeasurement(string text, decimal max, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return true;

        return decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && value >= 0
            && value <= max
            && decimal.Round(value, PoleOccupancyEntry.MeasurementDecimals) == value;
    }
}
