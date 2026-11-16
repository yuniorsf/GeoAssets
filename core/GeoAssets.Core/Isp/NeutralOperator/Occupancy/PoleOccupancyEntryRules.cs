namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>The per-entry checks and the entry ordering <see cref="PoleOccupancy"/> applies.</summary>
internal static class PoleOccupancyEntryRules
{
    /// <summary>Throws unless <paramref name="entry"/> is a valid entry of the given pole and owner.</summary>
    public static void Check(PoleOccupancyEntry? entry, string poleFeatureId, Guid poleOwnerOrganizationId)
    {
        if (entry is null)
            throw new ArgumentException("An entry is null.", nameof(entry));

        if (entry.Id == Guid.Empty)
            throw new ArgumentException("An entry has an empty id.", nameof(entry));

        if (!string.Equals(entry.PoleFeatureId, poleFeatureId, StringComparison.Ordinal))
            throw new ArgumentException($"Entry '{entry.Id}' does not belong to pole '{poleFeatureId}'.", nameof(entry));

        if (entry.PoleOwnerOrganizationId != poleOwnerOrganizationId)
            throw new ArgumentException($"Entry '{entry.Id}' does not carry the pole owner '{poleOwnerOrganizationId}'.", nameof(entry));

        if (entry.SlotIndex is < 1 or > PoleAttachmentSlotMatrix.MaxSlots)
            throw new ArgumentOutOfRangeException(
                nameof(entry), entry.SlotIndex, $"Slot index must be between 1 and {PoleAttachmentSlotMatrix.MaxSlots}, or absent for an over-capacity entry.");

        if (!Enum.IsDefined(entry.Status))
            throw new ArgumentOutOfRangeException(nameof(entry), entry.Status, "Unknown occupant status.");

        if (entry is { SlotIndex: null, Status: PoleAttachmentOccupantStatus.ActiveReservation })
            throw new ArgumentException(
                $"Entry '{entry.Id}' is beyond the regulated slots and cannot be a reservation: only a regulated slot can be reserved.", nameof(entry));

        if (entry.OccupantOrganizationId == Guid.Empty)
            throw new ArgumentException($"Entry '{entry.Id}' has an empty occupant organization; use null for an unidentified occupant.", nameof(entry));

        CheckText(entry.OperatorCnpj, PoleOccupancyEntry.MaxOperatorCnpjLength, nameof(PoleOccupancyEntry.OperatorCnpj), entry.Id);
        CheckText(entry.AnatelLicenseNumber, PoleOccupancyEntry.MaxAnatelLicenseNumberLength, nameof(PoleOccupancyEntry.AnatelLicenseNumber), entry.Id);
        CheckText(entry.AttachedCableFeatureId, PoleOccupancyEntry.MaxFeatureIdLength, nameof(PoleOccupancyEntry.AttachedCableFeatureId), entry.Id);
        CheckMeasurement(entry.CableDiameterMm, PoleOccupancyEntry.MaxCableDiameterMm, nameof(PoleOccupancyEntry.CableDiameterMm));
        CheckMeasurement(entry.CableLoadDaN, PoleOccupancyEntry.MaxCableLoadDaN, nameof(PoleOccupancyEntry.CableLoadDaN));
    }

    /// <summary>Regulated entries by slot index, then over-capacity entries by the ordinal order of their id's text.</summary>
    public static int Compare(PoleOccupancyEntry left, PoleOccupancyEntry right)
    {
        var bySlot = (left.SlotIndex ?? int.MaxValue).CompareTo(right.SlotIndex ?? int.MaxValue);
        return bySlot != 0
            ? bySlot
            : string.CompareOrdinal(left.Id.ToString("D"), right.Id.ToString("D"));
    }

    private static void CheckText(string? value, int maxLength, string field, Guid entryId)
    {
        if (value is null) return;

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Entry '{entryId}': {field} is blank; use null when it is not known.", nameof(value));

        if (value.Length > maxLength)
            throw new ArgumentException($"Entry '{entryId}': {field} is longer than {maxLength} characters.", nameof(value));
    }

    private static void CheckMeasurement(decimal value, decimal max, string field)
    {
        if (value < 0 || value > max || decimal.Round(value, PoleOccupancyEntry.MeasurementDecimals) != value)
            throw new ArgumentOutOfRangeException(
                nameof(value), value, $"{field} must be between 0 and {max}, with at most {PoleOccupancyEntry.MeasurementDecimals} decimal places.");
    }
}
