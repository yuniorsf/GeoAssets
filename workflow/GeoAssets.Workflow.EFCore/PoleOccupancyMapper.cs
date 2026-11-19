using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Persistence.Entities;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Converts between the domain <see cref="PoleOccupancy"/> / <see cref="PoleOccupancyEntry"/>
/// and their EF entities. The pole feature id and the pole owner live once, on the header row;
/// <see cref="ToDomain"/> hands them back to every entry.
/// </summary>
internal static class PoleOccupancyMapper
{
    public static PoleOccupancyRecord ToRecord(PoleOccupancy occupancy, Guid id) => new()
    {
        Id = id,
        PoleFeatureId = occupancy.PoleFeatureId,
        OrganizationId = occupancy.PoleOwnerOrganizationId,
        Version = occupancy.Version,
        UpdatedAt = occupancy.UpdatedAt.UtcDateTime,
    };

    public static PoleOccupancyEntryRecord ToEntryRecord(PoleOccupancyEntry entry, Guid poleOccupancyId) => new()
    {
        Id = entry.Id,
        PoleOccupancyId = poleOccupancyId,
        SlotIndex = entry.SlotIndex,
        Status = ToStatusText(entry.Status),
        OccupantOrganizationId = entry.OccupantOrganizationId,
        OperatorCnpj = entry.OperatorCnpj,
        AnatelLicenseNumber = entry.AnatelLicenseNumber,
        CableDiameterMm = entry.CableDiameterMm,
        CableLoadDaN = entry.CableLoadDaN,
        AttachedCableFeatureId = entry.AttachedCableFeatureId,
        OrganizationId = entry.PoleOwnerOrganizationId,
    };

    /// <summary>
    /// Rebuilds the pole through <see cref="PoleOccupancy.Create"/>, so rows that no longer form
    /// a valid pole fail the read loudly instead of surfacing as a half-valid aggregate.
    /// </summary>
    public static PoleOccupancy ToDomain(PoleOccupancyRecord record, IEnumerable<PoleOccupancyEntryRecord> entries) =>
        PoleOccupancy.Create(
            record.PoleFeatureId,
            record.OrganizationId,
            entries.Select(entry => ToEntry(record, entry)),
            record.Version,
            new DateTimeOffset(ServiceOrderTargetMapper.ToUtc(record.UpdatedAt)));

    public static PoleOccupancyEntry ToEntry(PoleOccupancyRecord pole, PoleOccupancyEntryRecord entry) => new()
    {
        Id = entry.Id,
        PoleFeatureId = pole.PoleFeatureId,
        PoleOwnerOrganizationId = pole.OrganizationId,
        SlotIndex = entry.SlotIndex,
        Status = FromStatusText(entry.Status),
        OccupantOrganizationId = entry.OccupantOrganizationId,
        OperatorCnpj = entry.OperatorCnpj,
        AnatelLicenseNumber = entry.AnatelLicenseNumber,
        CableDiameterMm = entry.CableDiameterMm,
        CableLoadDaN = entry.CableLoadDaN,
        AttachedCableFeatureId = entry.AttachedCableFeatureId,
    };

    /// <summary>The status is stored by name, so reordering the enum can never re-label stored rows.</summary>
    public static string ToStatusText(PoleAttachmentOccupantStatus status) => status switch
    {
        PoleAttachmentOccupantStatus.ActiveReservation => nameof(PoleAttachmentOccupantStatus.ActiveReservation),
        PoleAttachmentOccupantStatus.AuthorizedContracted => nameof(PoleAttachmentOccupantStatus.AuthorizedContracted),
        PoleAttachmentOccupantStatus.AbandonedDeadDrop => nameof(PoleAttachmentOccupantStatus.AbandonedDeadDrop),
        PoleAttachmentOccupantStatus.ClandestineUnauthorized => nameof(PoleAttachmentOccupantStatus.ClandestineUnauthorized),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown occupant status."),
    };

    public static PoleAttachmentOccupantStatus FromStatusText(string status) => status switch
    {
        nameof(PoleAttachmentOccupantStatus.ActiveReservation) => PoleAttachmentOccupantStatus.ActiveReservation,
        nameof(PoleAttachmentOccupantStatus.AuthorizedContracted) => PoleAttachmentOccupantStatus.AuthorizedContracted,
        nameof(PoleAttachmentOccupantStatus.AbandonedDeadDrop) => PoleAttachmentOccupantStatus.AbandonedDeadDrop,
        nameof(PoleAttachmentOccupantStatus.ClandestineUnauthorized) => PoleAttachmentOccupantStatus.ClandestineUnauthorized,
        _ => throw new InvalidOperationException($"Stored occupant status '{status}' is not a known {nameof(PoleAttachmentOccupantStatus)}."),
    };
}
