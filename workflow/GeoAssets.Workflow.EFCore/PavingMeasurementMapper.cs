using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Persistence.Entities;

namespace GeoAssets.Workflow.Persistence;

/// <summary>Converts between the domain <see cref="PavingMeasurement"/> and the EF entity <see cref="PavingMeasurementRecord"/>.</summary>
internal static class PavingMeasurementMapper
{
    public static PavingMeasurementRecord ToRecord(PavingMeasurement measurement) => new()
    {
        Id = measurement.Id,
        ParentServiceOrderId = measurement.ParentServiceOrderId,
        ChildServiceOrderId = measurement.ChildServiceOrderId,
        OrganizationId = measurement.OrganizationId,
        LengthMeters = measurement.LengthMeters,
        WidthMeters = measurement.WidthMeters,
        DepthMeters = measurement.DepthMeters,
        RecordedAreaSquareMeters = measurement.RecordedAreaSquareMeters,
        ActualAreaSquareMeters = measurement.ActualAreaSquareMeters,
        VolumeCubicMeters = measurement.VolumeCubicMeters,
        RecordedAtUtc = measurement.RecordedAtUtc,
    };

    public static PavingMeasurement ToDomain(PavingMeasurementRecord record) => new()
    {
        Id = record.Id,
        ParentServiceOrderId = record.ParentServiceOrderId,
        ChildServiceOrderId = record.ChildServiceOrderId,
        OrganizationId = record.OrganizationId,
        LengthMeters = record.LengthMeters,
        WidthMeters = record.WidthMeters,
        DepthMeters = record.DepthMeters,
        RecordedAreaSquareMeters = record.RecordedAreaSquareMeters,
        ActualAreaSquareMeters = record.ActualAreaSquareMeters,
        VolumeCubicMeters = record.VolumeCubicMeters,
        RecordedAtUtc = record.RecordedAtUtc,
    };
}
