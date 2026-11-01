namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to the <c>field_icerp_paving_measurements</c> table (XD01-291). Kept
/// separate from the domain <see cref="GeoAssets.Workflow.FieldWork.PavingMeasurement"/> so the
/// domain model stays free of EF annotations — same split as <see cref="ContractorUsPostingRecord"/>.
/// </summary>
internal sealed class PavingMeasurementRecord
{
    public Guid Id { get; set; } = Guid.Empty;
    public string ParentServiceOrderId { get; set; } = string.Empty;
    public string ChildServiceOrderId { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; } = Guid.Empty;
    public double LengthMeters { get; set; }
    public double WidthMeters { get; set; }
    public double DepthMeters { get; set; }
    public double RecordedAreaSquareMeters { get; set; }
    public double ActualAreaSquareMeters { get; set; }
    public double VolumeCubicMeters { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
