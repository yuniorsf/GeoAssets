namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to the <c>field_pole_occupancy_entries</c> table (XD01-314): one
/// attachment point of a pole. <see cref="SlotIndex"/> is 1..6 for a regulated slot and null for
/// an entry beyond them; a pole may have any number of the latter.
/// </summary>
internal sealed class PoleOccupancyEntryRecord
{
    public Guid Id { get; set; }

    /// <summary>FK to the owning <see cref="PoleOccupancyRecord"/>; its entries are deleted with it.</summary>
    public Guid PoleOccupancyId { get; set; }

    public int? SlotIndex { get; set; }

    /// <summary>The <c>PoleAttachmentOccupantStatus</c> member name.</summary>
    public string Status { get; set; } = string.Empty;

    public Guid? OccupantOrganizationId { get; set; }
    public string? OperatorCnpj { get; set; }
    public string? AnatelLicenseNumber { get; set; }
    public decimal CableDiameterMm { get; set; }
    public decimal CableLoadDaN { get; set; }
    public string? AttachedCableFeatureId { get; set; }

    /// <summary>The pole owner, repeated from the parent row so the table is tenant-scoped on its own.</summary>
    public Guid OrganizationId { get; set; } = Guid.Empty;
}
